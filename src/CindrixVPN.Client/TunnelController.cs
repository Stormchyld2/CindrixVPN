using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CindrixVPN.Core;

namespace CindrixVPN.Client;

internal sealed class TunnelController
{
    private const string PipePath = @"ProtectedPrefix\Administrators\WireGuard\" + AppPaths.TunnelName;

    public async Task ConnectAsync(string privateKey, ServerEntry server)
    {
        string conf = WireGuardConfigBuilder.Build(privateKey, server);

        await Task.Run(() =>
        {
            StopAndRemoveService();
            WriteProtectedConfig(conf);

            string binPath = $"\"{AppPaths.TunnelServiceExe}\" \"{AppPaths.TunnelConfigFile}\"";
            RunSc("create", AppPaths.ServiceName, "binPath=", binPath, "start=", "demand", "depend=", "Nsi/TcpIp",
                "DisplayName=", "CindrixVPN Tunnel");
            RunSc("sidtype", AppPaths.ServiceName, "unrestricted");
            RunSc("start", AppPaths.ServiceName);
        }).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            StopAndRemoveService();
            TryDeleteConfig();
        }).ConfigureAwait(false);
    }

    public Task<bool> IsServiceRunningAsync() => Task.Run(() =>
    {
        (int _, string output) = Sc("query", AppPaths.ServiceName);
        return output.Contains("RUNNING", StringComparison.Ordinal);
    });

    public async Task<TunnelStats?> GetStatsAsync()
    {
        try
        {
            using NamedPipeClientStream pipe = new(".", PipePath, PipeDirection.InOut);
            await pipe.ConnectAsync(1000).ConfigureAwait(false);
            byte[] request = Encoding.ASCII.GetBytes("get=1\n\n");
            await pipe.WriteAsync(request).ConfigureAwait(false);
            await pipe.FlushAsync().ConfigureAwait(false);

            using StreamReader reader = new(pipe, Encoding.ASCII);
            string text = await reader.ReadToEndAsync().ConfigureAwait(false);
            return UapiParser.Parse(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void StopAndRemoveService()
    {
        (int queryCode, string state) = Sc("query", AppPaths.ServiceName);
        if (queryCode != 0)
        {
            return; // service does not exist
        }

        if (!state.Contains("STOPPED", StringComparison.Ordinal))
        {
            Sc("stop", AppPaths.ServiceName);
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(200);
                (_, string s) = Sc("query", AppPaths.ServiceName);
                if (s.Contains("STOPPED", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        Sc("delete", AppPaths.ServiceName);
    }

    private static void WriteProtectedConfig(string conf)
    {
        DirectoryInfo dir = Directory.CreateDirectory(AppPaths.RuntimeDir);
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (WellKnownSidType sidType in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        dir.SetAccessControl(security);
        File.WriteAllText(AppPaths.TunnelConfigFile, conf);
    }

    private static void TryDeleteConfig()
    {
        try
        {
            if (File.Exists(AppPaths.TunnelConfigFile))
            {
                File.Delete(AppPaths.TunnelConfigFile);
            }
        }
        catch (IOException)
        {
        }
    }

    private static void RunSc(params string[] args)
    {
        (int code, string output) = Sc(args);
        if (code != 0)
        {
            throw new InvalidOperationException($"sc {args[0]} failed ({code}): {output.Trim()}");
        }
    }

    private static (int Code, string Output) Sc(params string[] args)
    {
        ProcessStartInfo info = new("sc.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
