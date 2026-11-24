using System;
using System.Diagnostics;
using System.IO;
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
        public static void LaunchGui()
        {
            GameMasterConsoleWindow.Open();
        }

        private static string PrepareClientDirectory()
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

            foreach (string file in new[] { "gamemaster_client.py", "requirements.txt", "run_client.bat", "run_client.sh" })
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

        private static string QuoteArgument(string argument)
        {
            return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
