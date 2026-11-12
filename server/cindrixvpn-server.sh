#!/usr/bin/env bash
# CindrixVPN exit-server helper for Ubuntu (WireGuard).
#
#   sudo ./cindrixvpn-server.sh setup                      install and start WireGuard
#   sudo ./cindrixvpn-server.sh add-client <name>          make a ready-to-import <name>.conf
#   sudo ./cindrixvpn-server.sh add-peer <name> <pubkey>   authorize a device-generated key
#   sudo ./cindrixvpn-server.sh list                       show connected peers
#
# P2P (port forwarding to one device; also open the port in the Oracle security list):
#   sudo ./cindrixvpn-server.sh forward-port <name> <port> [tcp|udp|both]
#   sudo ./cindrixvpn-server.sh unforward-port <name> <port>
#   sudo ./cindrixvpn-server.sh list-forwards
#
# Optional environment: WG_PORT (default 51820), WG_NET (default 10.66.66), ENDPOINT (public IP or DNS name), DNS (default 1.1.1.1)
set -euo pipefail

WG_IF="${WG_IF:-wg0}"
WG_DIR="${WG_DIR:-/etc/wireguard}"
WG_PORT="${WG_PORT:-51820}"
WG_NET="${WG_NET:-10.66.66}"
DNS="${DNS:-1.1.1.1}"

need_root() { [ "$(id -u)" -eq 0 ] || { echo "Run this with sudo." >&2; exit 1; }; }

next_ip() {
  local used n
  used=$(grep -oE "${WG_NET}\.[0-9]+/32" "$WG_DIR/$WG_IF.conf" 2>/dev/null | cut -d/ -f1 | cut -d. -f4 | sort -n || true)
  for n in $(seq 2 254); do
    if ! grep -qx "$n" <<<"$used"; then echo "${WG_NET}.$n"; return; fi
  done
  echo "No free addresses left." >&2; exit 1
}

cmd_setup() {
  need_root
  export DEBIAN_FRONTEND=noninteractive
  echo iptables-persistent iptables-persistent/autosave_v4 boolean false | debconf-set-selections
  echo iptables-persistent iptables-persistent/autosave_v6 boolean false | debconf-set-selections
  apt-get update -y
  apt-get install -y wireguard iptables iptables-persistent curl

  umask 077
  mkdir -p "$WG_DIR"
  if [ ! -f "$WG_DIR/server.key" ]; then
    wg genkey | tee "$WG_DIR/server.key" | wg pubkey > "$WG_DIR/server.pub"
  fi

  local nic ep
  nic=$(ip -4 route show default | awk '{print $5; exit}')
  ep="${ENDPOINT:-$(curl -4 -fsS https://api.ipify.org || true)}"
  [ -n "$ep" ] || { echo "Could not detect the public IP. Re-run with ENDPOINT=<ip-or-name>." >&2; exit 1; }
  echo "$ep" > "$WG_DIR/endpoint"

  if [ ! -f "$WG_DIR/$WG_IF.conf" ]; then
    cat > "$WG_DIR/$WG_IF.conf" <<EOF
[Interface]
Address = ${WG_NET}.1/24
ListenPort = ${WG_PORT}
PrivateKey = $(cat "$WG_DIR/server.key")
PostUp = iptables -I FORWARD 1 -i %i -j ACCEPT; iptables -I FORWARD 1 -o %i -j ACCEPT; iptables -t nat -A POSTROUTING -o ${nic} -j MASQUERADE
PostDown = iptables -D FORWARD -i %i -j ACCEPT; iptables -D FORWARD -o %i -j ACCEPT; iptables -t nat -D POSTROUTING -o ${nic} -j MASQUERADE
EOF
  fi

  echo "net.ipv4.ip_forward=1" > /etc/sysctl.d/99-cindrixvpn.conf
  sysctl --system >/dev/null

  iptables -C INPUT -p udp --dport "$WG_PORT" -j ACCEPT 2>/dev/null \
    || iptables -I INPUT 1 -p udp --dport "$WG_PORT" -j ACCEPT
  netfilter-persistent save >/dev/null

  systemctl enable --now "wg-quick@$WG_IF"

  echo
  echo "CindrixVPN server is running."
  echo "  Endpoint:          $ep:$WG_PORT"
  echo "  Server public key: $(cat "$WG_DIR/server.pub")"
  echo
  echo "Still to do in the Oracle console: allow inbound UDP $WG_PORT (see server/README.md)."
}

add_peer_block() { # name pubkey ip
  local name=$1 pub=$2 ip=$3
  umask 077
  {
    echo
    echo "# $name"
    echo "[Peer]"
    echo "PublicKey = $pub"
    echo "AllowedIPs = $ip/32"
  } >> "$WG_DIR/$WG_IF.conf"
  wg set "$WG_IF" peer "$pub" allowed-ips "$ip/32"
}

cmd_add_peer() {
  need_root
  local name=${1:?name required} pub=${2:?public key required} ip
  [[ "$name" =~ ^[A-Za-z0-9_-]+$ ]] || { echo "Use letters, numbers, - and _ in the name." >&2; exit 1; }
  grep -q "PublicKey = $pub" "$WG_DIR/$WG_IF.conf" && { echo "That key is already added." >&2; exit 1; }
  ip=$(next_ip)
  add_peer_block "$name" "$pub" "$ip"
  echo "Added $name."
  echo "  Address:           $ip/32"
  echo "  Endpoint:          $(cat "$WG_DIR/endpoint"):$WG_PORT"
  echo "  Server public key: $(cat "$WG_DIR/server.pub")"
}

cmd_add_client() {
  need_root
  local name=${1:?name required} priv pub ip out
  [[ "$name" =~ ^[A-Za-z0-9_-]+$ ]] || { echo "Use letters, numbers, - and _ in the name." >&2; exit 1; }
  umask 077
  priv=$(wg genkey)
  pub=$(wg pubkey <<<"$priv")
  ip=$(next_ip)
  add_peer_block "$name" "$pub" "$ip"
  out="$PWD/$name.conf"
  cat > "$out" <<EOF
[Interface]
PrivateKey = $priv
Address = $ip/32
DNS = $DNS

[Peer]
PublicKey = $(cat "$WG_DIR/server.pub")
Endpoint = $(cat "$WG_DIR/endpoint"):$WG_PORT
AllowedIPs = 0.0.0.0/0, ::/0
PersistentKeepalive = 25
EOF
  echo "Wrote $out  (contains a private key: copy it to the device, then delete it from the server)."
}

cmd_list() { need_root; wg show "$WG_IF"; }

# ---------- P2P: port forwarding ----------

default_nic() { ip -4 route show default | awk '{print $5; exit}'; }

peer_ip() { # name -> the peer's tunnel address on $WG_IF
  awk -v n="# $1" '$0 == n { f = 1; next } f && /AllowedIPs/ { sub(/\/32.*/, "", $3); print $3; exit }' "$WG_DIR/$WG_IF.conf"
}

check_forward_args() { # name port -> sets FWD_IP
  local name=${1:?name required} port=${2:?port required}
  [[ "$port" =~ ^[0-9]+$ ]] && [ "$port" -ge 1024 ] && [ "$port" -le 65535 ] \
    || { echo "Port must be a number from 1024 to 65535." >&2; exit 1; }
  case "$port" in 51820) echo "Port $port is used by CindrixVPN itself." >&2; exit 1 ;; esac
  FWD_IP=$(peer_ip "$name")
  [ -n "$FWD_IP" ] || { echo "No device named '$name' on $WG_IF. Run: $0 list" >&2; exit 1; }
}

cmd_forward_port() {
  need_root
  check_forward_args "$@"
  local name=$1 port=$2 which="${3:-both}" nic protos proto
  case "$which" in tcp|udp) protos=$which ;; both) protos="tcp udp" ;; *) echo "Use tcp, udp or both." >&2; exit 1 ;; esac
  nic=$(default_nic)
  for proto in $protos; do
    if iptables -t nat -S PREROUTING | grep -Eq -- "-p $proto .*--dport $port -j DNAT"; then
      echo "$proto port $port is already forwarded. See: $0 list-forwards" >&2; exit 1
    fi
  done
  for proto in $protos; do
    iptables -t nat -A PREROUTING -i "$nic" -p "$proto" --dport "$port" -j DNAT --to-destination "$FWD_IP:$port"
  done
  netfilter-persistent save >/dev/null
  echo "Forwarded $protos port $port to $name ($FWD_IP)."
  echo "Still to do:"
  echo "  1. Oracle console: add ingress rules (source 0.0.0.0/0) for the same port and protocol(s)."
  echo "  2. On the device: set the torrent client's listening port to $port and allow it in the device firewall."
}

cmd_unforward_port() {
  need_root
  check_forward_args "$@"
  local port=$2 nic proto removed=0
  nic=$(default_nic)
  for proto in tcp udp; do
    if iptables -t nat -C PREROUTING -i "$nic" -p "$proto" --dport "$port" -j DNAT --to-destination "$FWD_IP:$port" 2>/dev/null; then
      iptables -t nat -D PREROUTING -i "$nic" -p "$proto" --dport "$port" -j DNAT --to-destination "$FWD_IP:$port"
      removed=1
    fi
  done
  [ "$removed" -eq 1 ] || { echo "No forward of port $port to $1 was found." >&2; exit 1; }
  netfilter-persistent save >/dev/null
  echo "Removed forward of port $port to $1. You can also delete its Oracle ingress rules."
}

cmd_list_forwards() { need_root; iptables -t nat -S PREROUTING | grep -- "-j DNAT" || echo "No forwarded ports."; }

case "${1:-}" in
  setup)           shift; cmd_setup "$@" ;;
  add-client)      shift; cmd_add_client "$@" ;;
  add-peer)        shift; cmd_add_peer "$@" ;;
  list)            shift; cmd_list "$@" ;;
  forward-port)    shift; cmd_forward_port "$@" ;;
  unforward-port)  shift; cmd_unforward_port "$@" ;;
  list-forwards)   shift; cmd_list_forwards "$@" ;;
  *) sed -n '2,13p' "$0"; exit 1 ;;
esac
