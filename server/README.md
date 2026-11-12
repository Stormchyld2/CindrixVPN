# CindrixVPN exit server (Ubuntu + WireGuard)

CindrixVPN needs no account system or backend. The only server piece is a small WireGuard
machine that your traffic exits through. This guide uses a free Oracle Cloud Always Free VM,
but any Ubuntu server with a public IP works.

## 1. Create the VM (Oracle Cloud)

1. Create an Always Free account. Do not click Upgrade.
2. Create a compute instance using an **Always Free-eligible** shape (the AMD micro) and the
   **Ubuntu** image. Add your SSH public key.
3. Note the instance's **public IP address**.

## 2. Open the WireGuard port in Oracle (UDP 51820)

In the Oracle console, open the instance's subnet, then its security list, and add an
**ingress rule**: source `0.0.0.0/0`, protocol **UDP**, destination port **51820**.

## 3. Install WireGuard on the VM

```bash
ssh ubuntu@<public-ip>
git clone https://github.com/Stormchyld2/CindrixVPN.git
cd CindrixVPN/server
sudo ./cindrixvpn-server.sh setup
```

The script prints the endpoint and the server public key.

## 4. Add a device

Quick way (the script makes the keys and a ready-to-import config):

```bash
sudo ./cindrixvpn-server.sh add-client my-laptop
```

Copy `my-laptop.conf` to the device, then delete it from the server.

Better way (the device keeps its own private key): have the app generate a key pair, then run

```bash
sudo ./cindrixvpn-server.sh add-peer my-laptop <device-public-key>
```

and enter the address, endpoint and server public key it prints into the app.

## Notes

- Check connected devices with `sudo ./cindrixvpn-server.sh list`.
- IPv6 is not routed by this server. The client config sends IPv6 into the tunnel, where it is
  dropped, so it can't leak around the VPN.
- If the VM's public IP changes, edit `/etc/wireguard/endpoint` and re-issue configs.
