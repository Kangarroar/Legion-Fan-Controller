using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace Lenovo_Fan_Controller
{
    public static class StartupManager
    {
        private const string TaskName = "LegionFanController";

        /// <summary>
        /// Checks if the application is running with administrator privileges
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Checks if startup task exists
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Enables startup on Windows boot
        /// </summary>
        public static bool EnableStartup() // Minimized
        {
            try
            {
                if (!IsRunningAsAdmin())
                {
                    return false;
                }

                // Get the executable path
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return false;
                }

                DisableStartup();

                // XML 
                var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Legion Fan Controller - Automatic startup</Description>
    <Author>{Environment.UserName}</Author>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT10S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exePath}</Command>
      <Arguments>/minimized</Arguments>
    </Exec>
  </Actions>
</Task>";

                // Save XML 
                var tempXmlPath = Path.Combine(Path.GetTempPath(), "legion_fan_task.xml");
                File.WriteAllText(tempXmlPath, xml);

                try
                {
                    // SchedTSK
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Create /TN \"{TaskName}\" /XML \"{tempXmlPath}\" /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(startInfo);
                    process?.WaitForExit();

                    return process?.ExitCode == 0;
                }
                finally
                {
                    if (File.Exists(tempXmlPath))
                    {
                        try { File.Delete(tempXmlPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enable startup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disables startup on Windows boot
        /// </summary>
        public static bool DisableStartup()
        {
            try
            {
                if (!IsRunningAsAdmin())
                {
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{TaskName}\" /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit();
                return process?.ExitCode == 0 || process?.ExitCode == 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to disable startup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Toggles startup
        /// </summary>
        public static bool ToggleStartup()
        {
            if (IsStartupEnabled())
            {
                return DisableStartup();
            }
            else
            {
                return EnableStartup();
            }
        }
    }
}

