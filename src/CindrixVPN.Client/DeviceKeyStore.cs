using System.IO;
using CindrixVPN.Core;

namespace CindrixVPN.Client;

internal static class DeviceKeyStore
{
    public static string LoadOrCreatePrivateKey()
    {
        string path = AppPaths.DeviceKeyFile;
        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path).Trim();
            if (WireGuardKeys.IsValidKey(existing))
            {
                return existing;
            }
        }

        string created = WireGuardKeys.GeneratePrivateKey();
        File.WriteAllText(path, created);
        return created;
    }
}
