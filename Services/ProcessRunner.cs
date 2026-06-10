using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OplusEdlTool.Services
{
    public static class ProcessRunner
    {
        public static bool VerboseLogging { get; set; } = false;

        private static string? _lastFlashedPartition = null;

        private static string GetWorkDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OplusEdlTool");
        }

        public static async Task<(int, string)> RunAsync(string fileName, string args, string workingDir, System.Action<string>? onLine = null, System.Action<int>? onPercent = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var p = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            
            var cts = new CancellationTokenSource();
            var traceMonitorTask = MonitorPortTraceAsync(onLine, cts.Token);
            
            p.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    sb.AppendLine(e.Data);
                    try
                    {
                        var line = e.Data;
                        
                        if (VerboseLogging && onLine != null && !string.IsNullOrWhiteSpace(line))
                        {
                            onLine.Invoke("[fh_loader] " + line);
                        }
                        
                        ParseAndLogPartition(line, onLine);
                        
                        var m1 = Regex.Match(line, @"(?i)percentage\s*complete\D*(\d{1,3})");
                        var m2 = Regex.Match(line, @"(\d{1,3})\s*%");
                        if (m1.Success || m2.Success)
                        {
                            var txt = m1.Success ? m1.Groups[1].Value : m2.Groups[1].Value;
                            if (int.TryParse(txt, out var per)) onPercent?.Invoke(Math.Max(0, Math.Min(100, per)));
                        }
                    }
                    catch { }
                }
            };
            p.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    sb.AppendLine(e.Data);
                    if (VerboseLogging && onLine != null && !string.IsNullOrWhiteSpace(e.Data))
                    {
                        onLine.Invoke("[fh_loader ERR] " + e.Data);
                    }
                }
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await Task.Run(() => p.WaitForExit());
            
            cts.Cancel();
            try { await traceMonitorTask; } catch { }
            
            try { if (p.ExitCode == 0) onPercent?.Invoke(100); } catch { }
            return (p.ExitCode, sb.ToString());
        }

        private static void ParseAndLogPartition(string line, Action<string>? onLine)
        {
            if (onLine == null || string.IsNullOrWhiteSpace(line)) return;
            
            var skipLabels = new[] { "PrimaryGPT", "BackupGPT", "test", "read", "write", "ping" };
            
            string? partition = null;
            
            var m1 = Regex.Match(line, @"label\s*[=:]\s*[""']?([a-zA-Z0-9_]+)[""']?", RegexOptions.IgnoreCase);
            if (m1.Success) partition = m1.Groups[1].Value;
            
            if (partition == null)
            {
                var m2 = Regex.Match(line, @"for\s+['""]([a-zA-Z0-9_]+)['""]", RegexOptions.IgnoreCase);
                if (m2.Success) partition = m2.Groups[1].Value;
            }
            
            if (partition == null)
            {
                var m3 = Regex.Match(line, @"partition\s+['""]([a-zA-Z0-9_]+)['""]", RegexOptions.IgnoreCase);
                if (m3.Success) partition = m3.Groups[1].Value;
            }
            
            if (partition == null)
            {
                var m4 = Regex.Match(line, @"(?:Writing|Flashing|Programming)\s+['""]?([a-zA-Z0-9_]+)['""]?", RegexOptions.IgnoreCase);
                if (m4.Success) partition = m4.Groups[1].Value;
            }
            
            if (partition != null)
            {
                foreach (var skip in skipLabels)
                {
                    if (partition.Equals(skip, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                
                if (partition != _lastFlashedPartition)
                {
                    _lastFlashedPartition = partition;
                    onLine.Invoke($"Flashing: {partition}");
                }
            }
        }

        private static async Task MonitorPortTraceAsync(Action<string>? onLine, CancellationToken ct)
        {
            if (onLine == null) return;
            
            var traceFile = Path.Combine(GetWorkDir(), "port_trace.txt");
            long lastPosition = 0;
            
            if (File.Exists(traceFile))
            {
                try { lastPosition = new FileInfo(traceFile).Length; } catch { }
            }
            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(100, ct);
                    
                    if (!File.Exists(traceFile)) continue;
                    
                    var fileInfo = new FileInfo(traceFile);
                    if (fileInfo.Length <= lastPosition) continue;
                    
                    using var fs = new FileStream(traceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fs.Seek(lastPosition, SeekOrigin.Begin);
                    using var reader = new StreamReader(fs);
                    var newContent = await reader.ReadToEndAsync();
                    lastPosition = fileInfo.Length;
                    
                    var lines = newContent.Split('\n');
                    foreach (var line in lines)
                    {
                        ParseAndLogPartition(line, onLine);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }
            }
        }
    }
}
