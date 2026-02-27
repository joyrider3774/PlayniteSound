using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PlayniteSounds.Common
{
    public class Dism
    {
        public delegate void ProgressCallback(int percent, string message);

        public static bool EnableFeature(string featureName, ProgressCallback callback)
        {
            string logFile = Path.GetTempFileName();
            var logMonitor = new LogMonitor(callback, logFile);

            try
            {
                // Start monitoring the log file in background
                var monitorThread = new Thread(logMonitor.Start);
                monitorThread.Start();

                // Run elevated DISM
                var exitCode = RunElevatedDism(featureName, logFile);

                // Signal monitoring to stop
                logMonitor.Stop();
                monitorThread.Join();

                return exitCode == 0;
            }
            finally
            {
                File.Delete(logFile);
            }
        }

        private static int RunElevatedDism(string featureName, string logFile)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C dism.exe /Online /Enable-Feature /FeatureName:{featureName} /All /NoRestart > \"{logFile}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(startInfo))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private class LogMonitor
        {
            private readonly ProgressCallback _callback;
            private readonly string _logFile;
            private bool _shouldStop;

            public LogMonitor(ProgressCallback callback, string logFile)
            {
                _callback = callback;
                _logFile = logFile;
            }

            public void Start()
            {
                using (var fs = new FileStream(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                {
                    int percent = 0;
                    string message = "";
                    while (!_shouldStop)
                    {
                        string line;
                        while (!_shouldStop && (line = reader.ReadLine()) != null)
                        {
                            if (line.Contains("%"))
                            {
                                percent = ParsePercent(line);
                            }
                            else if (line.Length > 0)
                            {
                                message = line;
                            }
                            _callback?.Invoke(percent, message);
                        }

                        if (!_shouldStop)
                        {
                            Thread.Sleep(500); // Wait for more output
                        }
                    }
                }
            }

            public void Stop() => _shouldStop = true;

            private int ParsePercent(string line)
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)[\.,]\d+\%");

                if (match.Success && int.TryParse(match.Groups[1].Value, out int percent))
                {
                    return percent;
                }
                return 0;
            }
        }
    }
}

// Usage example

