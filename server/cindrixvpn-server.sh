#!/usr/bin/env bash
# CindrixVPN exit-server helper for Ubuntu (WireGuard).
#
#   sudo ./cindrixvpn-server.sh setup                      install and start WireGuard
#   sudo ./cindrixvpn-server.sh add-client <name>          make a ready-to-import <name>.conf
#   sudo ./cindrixvpn-server.sh add-peer <name> <pubkey>   authorize a device-generated key
#   sudo ./cindrixvpn-server.sh list                       show connected peers
#
# Optional environment: WG_PORT (default 51820), WG_NET (default 10.66.66), ENDPOINT (public IP or DNS name), DNS (default 1.1.1.1)
set -euo pipefail

WG_IF=wg0
WG_DIR=/etc/wireguard
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
PostUp = iptables -A FORWARD -i %i -j ACCEPT; iptables -A FORWARD -o %i -j ACCEPT; iptables -t nat -A POSTROUTING -o ${nic} -j MASQUERADE
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

case "${1:-}" in
  setup)      shift; cmd_setup "$@" ;;
  add-client) shift; cmd_add_client "$@" ;;
  add-peer)   shift; cmd_add_peer "$@" ;;
  list)       shift; cmd_list "$@" ;;
  *) sed -n '2,10p' "$0"; exit 1 ;;
esac
