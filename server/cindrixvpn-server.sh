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

cmd_setup_netshield() {
  need_root
  local gw="${WG_GATEWAY:-10.66.66.1}" arch ag_dir=/opt/AdGuardHome pass
  case "$(uname -m)" in x86_64) arch=amd64 ;; aarch64) arch=arm64 ;; *) echo "Unsupported CPU"; exit 1 ;; esac
  apt-get install -y curl python3 >/dev/null
  if [ ! -x "$ag_dir/AdGuardHome" ]; then
    curl -fsSL "https://static.adguard.com/adguardhome/release/AdGuardHome_linux_${arch}.tar.gz" | tar -xz -C /opt
  fi
  # Let AdGuard start only after the VPN interface (and its 10.66.66.1 address) exists.
  mkdir -p /etc/systemd/system/AdGuardHome.service.d
  printf '[Unit]\nAfter=wg-quick@%s.service\nRequires=wg-quick@%s.service\n' "$WG_IF" "$WG_IF" \
    > /etc/systemd/system/AdGuardHome.service.d/wg.conf
  (cd "$ag_dir" && ./AdGuardHome -s install) || true
  systemctl daemon-reload
  systemctl start AdGuardHome
  sleep 4
  pass=$(head -c 12 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 14)
  # First-run wizard API: bind the filter and its admin page to the VPN address only.
  curl -fsS -X POST http://127.0.0.1:3000/control/install/configure -H 'Content-Type: application/json' \
    -d "{\"web\":{\"ip\":\"$gw\",\"port\":3000},\"dns\":{\"ip\":\"$gw\",\"port\":53},\"username\":\"admin\",\"password\":\"$pass\"}" \
    || echo "(AdGuard may already be configured; skipping the wizard)"
  systemctl restart AdGuardHome

  # Tiny stats endpoint for the app's Server Load bar, reachable only inside the tunnel.
  cat > /usr/local/bin/cindrix-stats.py <<STATS
#!/usr/bin/env python3
import json, os
from http.server import BaseHTTPRequestHandler, HTTPServer
class H(BaseHTTPRequestHandler):
    def do_GET(self):
        load = min(100, int(os.getloadavg()[0] / (os.cpu_count() or 1) * 100))
        body = json.dumps({"load": load, "blocked": 0, "queries": 0}).encode()
        self.send_response(200); self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *a): pass
HTTPServer(("$gw", 8088), H).serve_forever()
STATS
  chmod +x /usr/local/bin/cindrix-stats.py
  cat > /etc/systemd/system/cindrix-stats.service <<UNIT
[Unit]
After=wg-quick@$WG_IF.service
Requires=wg-quick@$WG_IF.service
[Service]
ExecStart=/usr/local/bin/cindrix-stats.py
Restart=always
[Install]
WantedBy=multi-user.target
UNIT
  systemctl daemon-reload
  systemctl enable --now cindrix-stats

  # Oracle's INPUT chain rejects unknown traffic: allow DNS and stats from VPN clients only.
  for r in "-p udp --dport 53" "-p tcp --dport 53" "-p tcp --dport 8088" "-p tcp --dport 3000"; do
    iptables -C INPUT -i "$WG_IF" $r -j ACCEPT 2>/dev/null || iptables -I INPUT 1 -i "$WG_IF" $r -j ACCEPT
  done
  command -v netfilter-persistent >/dev/null && netfilter-persistent save
  echo
  echo "NetShield is on. Filter admin page (open while connected to the VPN): http://$gw:3000"
  echo "Login: admin / $pass   <- write this down; it is shown only once."
}

case "${1:-}" in
  setup)           shift; cmd_setup "$@" ;;
  add-client)      shift; cmd_add_client "$@" ;;
  add-peer)        shift; cmd_add_peer "$@" ;;
  list)            shift; cmd_list "$@" ;;
  setup-netshield) shift; cmd_setup_netshield "$@" ;;
  *) sed -n '2,9p' "$0"; exit 1 ;;
esac
