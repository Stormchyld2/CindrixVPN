using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace CindrixVPN.Client;

/// <summary>
/// Start-at-sign-in via a scheduled task. A plain Run registry entry cannot launch an app that
/// requires administrator rights without a UAC prompt, but a "highest privileges" task can.
/// </summary>
internal static class StartupManager
{
    private const string TaskName = "CindrixVPN";

    public static bool IsEnabled() => RunSchtasks("/Query", "/TN", TaskName).Code == 0;

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (IsEnabled())
            {
                (int code, string output) = RunSchtasks("/Delete", "/TN", TaskName, "/F");
                if (code != 0)
                {
                    throw new InvalidOperationException("Could not remove the startup task: " + output.Trim());
                }
            }

            return;
        }

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the app path.");
        string xml = BuildTaskXml(exe, WindowsIdentity.GetCurrent().Name);
        string temp = Path.Combine(Path.GetTempPath(), "cindrixvpn-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(temp, xml, Encoding.Unicode);
            (int code, string output) = RunSchtasks("/Create", "/TN", TaskName, "/XML", temp, "/F");
            if (code != 0)
            {
                throw new InvalidOperationException("Could not create the startup task: " + output.Trim());
            }
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    internal static string BuildTaskXml(string exePath, string userId)
    {
        string user = SecurityElement.Escape(userId);
        string command = SecurityElement.Escape(exePath);
        string workDir = SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? string.Empty);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Start CindrixVPN when you sign in</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{command}</Command><Arguments>--minimized</Arguments><WorkingDirectory>{workDir}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) RunSchtasks(params string[] args)
    {
        ProcessStartInfo info = new("schtasks.exe")
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
