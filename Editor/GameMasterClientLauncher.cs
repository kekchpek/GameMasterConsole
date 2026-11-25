using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace GameMasterConsole.Editor
{
    public static class GameMasterClientLauncher
    {
        [MenuItem("Window/Game Master/Launch Terminal", false, 100)]
        public static void LaunchClient()
        {
            try
            {
                string clientDirectory = PrepareClientDirectory();
                ProcessStartInfo startInfo = GetLaunchInfo(clientDirectory);
                using var process = Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to launch GameMaster Console Client: {exception.Message}");
            }
        }

        [MenuItem("Window/Game Master/Launch GUI", false, 101)]
        public static async void LaunchGui()
        {
            try
            {
                string clientDirectory = PrepareClientDirectory(gui: true);
                using var process = Process.Start(GetGuiLaunchInfo(clientDirectory));
                if (process == null)
                    throw new InvalidOperationException("The Python launcher could not be started.");

                string errors = await process.StandardError.ReadToEndAsync();
                await Task.Run(() => process.WaitForExit());
                if (process.ExitCode != 0)
                {
                    string details = errors.Trim();
                    Debug.LogError(string.IsNullOrEmpty(details)
                        ? $"GameMaster Console desktop launcher exited with code {process.ExitCode}."
                        : $"GameMaster Console desktop launcher failed.\n{details}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to launch GameMaster Console desktop app: {exception.Message}");
            }
        }

        private static string PrepareClientDirectory(bool gui = false)
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(GameMasterClientLauncher).Assembly);
            if (package == null)
            {
                throw new InvalidOperationException("The GameMaster Console Unity package could not be located.");
            }

            string source = Path.Combine(package.resolvedPath, "Tools~", "Client");
            string destination = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Library", "GameMasterConsole", "Client"));
            Directory.CreateDirectory(destination);

            string[] files = gui
                ? new[] { "gamemaster_client.py", "gamemaster_gui.py", "gamemaster_logs_view.py", "requirements.txt", "run_gui.bat", "run_gui.sh" }
                : new[] { "gamemaster_client.py", "requirements.txt", "run_client.bat", "run_client.sh" };
            foreach (string file in files)
            {
                string sourceFile = Path.Combine(source, file);
                string destinationFile = Path.Combine(destination, file);
                File.WriteAllBytes(destinationFile, File.ReadAllBytes(sourceFile));
            }

            return destination;
        }

        private static ProcessStartInfo GetLaunchInfo(string clientDirectory)
        {
            string unixScript = Path.Combine(clientDirectory, "run_client.sh");
            var startInfo = new ProcessStartInfo { WorkingDirectory = clientDirectory };

            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    startInfo.FileName = Path.Combine(clientDirectory, "run_client.bat");
                    startInfo.UseShellExecute = true;
                    break;

                case RuntimePlatform.OSXEditor:
                    string shellCommand = "/bin/bash '" + unixScript.Replace("'", "'\\''") + "'";
                    string appleScript = "tell application \"Terminal\" to do script " + QuoteArgument(shellCommand);
                    startInfo.FileName = "/usr/bin/osascript";
                    startInfo.Arguments = "-e " + QuoteArgument(appleScript);
                    startInfo.UseShellExecute = false;
                    break;

                case RuntimePlatform.LinuxEditor:
                    startInfo.FileName = "x-terminal-emulator";
                    startInfo.Arguments = "-e /bin/bash " + QuoteArgument(unixScript);
                    startInfo.UseShellExecute = false;
                    break;

                default:
                    throw new PlatformNotSupportedException($"Unsupported platform: {Application.platform}");
            }

            return startInfo;
        }

        private static ProcessStartInfo GetGuiLaunchInfo(string clientDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = clientDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    startInfo.Arguments = "/d /c run_gui.bat";
                    break;
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.LinuxEditor:
                    startInfo.FileName = "/bin/bash";
                    startInfo.Arguments = "run_gui.sh";
                    break;
                default:
                    throw new PlatformNotSupportedException($"Unsupported platform: {Application.platform}");
            }

            return startInfo;
        }

        private static string QuoteArgument(string argument)
        {
            return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
