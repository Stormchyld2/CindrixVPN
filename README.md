# CindrixVPN

Standalone Windows VPN client with WireGuard built in. Routes all traffic through the CindrixOps exit server.
No account, no backend: the device key is generated locally and the server list is a local JSON file (`servers.json`).

- Run `CindrixVPN.exe` (asks for administrator rights to manage the tunnel service).
- Copy the device public key shown in the app and authorize it on the server:
  `sudo ./cindrixvpn-server.sh add-peer <name> <public-key>`
- Server setup: see `server/README.md` in the main branch.

Build: GitHub Actions (`build` workflow) produces the `CindrixVPN-win-x64` artifact.

Licensed GPLv3. Bundles WireGuard native components (`native/x64`); see NOTICE.md.
