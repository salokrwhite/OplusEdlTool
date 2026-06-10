using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OplusEdlTool.Services
{
    public class SuperMergeService
    {
        private readonly Action<string>? _onLog;
        private readonly Action<int>? _onProgress;
        private bool _isCancelled;

        public SuperMergeService(Action<string>? onLog = null, Action<int>? onProgress = null)
        {
            _onLog = onLog;
            _onProgress = onProgress;
        }

        public class SuperConfig
        {
            public SuperMeta? SuperMeta { get; set; }
            public List<BlockDevice> BlockDevices { get; set; } = new();
            public List<Group> Groups { get; set; } = new();
            public List<Partition> Partitions { get; set; } = new();
        }

        public class SuperMeta
        {
            public string Path { get; set; } = "";
            public string Size { get; set; } = "65536";
        }

        public class BlockDevice
        {
            public string Name { get; set; } = "super";
            public string Size { get; set; } = "";
            public string BlockSize { get; set; } = "4096";
            public string Alignment { get; set; } = "1048576";
        }

        public class Group
        {
            public string Name { get; set; } = "";
            public string? MaximumSize { get; set; }
        }

        public class Partition
        {
            public string Name { get; set; } = "";
            public string? Path { get; set; }
            public string? Size { get; set; }
            public string GroupName { get; set; } = "";
            public bool IsDynamic { get; set; } = true;
        }

        public void Cancel()
        {
            _isCancelled = true;
        }

        public SuperConfig? ParseSuperDefJson(string jsonPath)
        {
            try
            {
                if (!File.Exists(jsonPath))
                {
                    _onLog?.Invoke($"Error: JSON file not found - {jsonPath}");
                    return null;
                }

                var jsonText = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                var config = new SuperConfig();

                if (root.TryGetProperty("super_meta", out var superMeta))
                {
                    config.SuperMeta = new SuperMeta
                    {
                        Path = superMeta.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                        Size = superMeta.TryGetProperty("size", out var s) ? s.GetString() ?? "65536" : "65536"
                    };
                }

                if (root.TryGetProperty("block_devices", out var blockDevices))
                {
                    foreach (var dev in blockDevices.EnumerateArray())
                    {
                        config.BlockDevices.Add(new BlockDevice
                        {
                            Name = dev.TryGetProperty("name", out var n) ? n.GetString() ?? "super" : "super",
                            Size = dev.TryGetProperty("size", out var sz) ? sz.GetString() ?? "" : "",
                            BlockSize = dev.TryGetProperty("block_size", out var bs) ? bs.GetString() ?? "4096" : "4096",
                            Alignment = dev.TryGetProperty("alignment", out var al) ? al.GetString() ?? "1048576" : "1048576"
                        });
                    }
                }

                if (root.TryGetProperty("groups", out var groups))
                {
                    foreach (var grp in groups.EnumerateArray())
                    {
                        config.Groups.Add(new Group
                        {
                            Name = grp.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            MaximumSize = grp.TryGetProperty("maximum_size", out var ms) ? ms.GetString() : null
                        });
                    }
                }

                if (root.TryGetProperty("partitions", out var partitions))
                {
                    foreach (var part in partitions.EnumerateArray())
                    {
                        config.Partitions.Add(new Partition
                        {
                            Name = part.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Path = part.TryGetProperty("path", out var p) ? p.GetString() : null,
                            Size = part.TryGetProperty("size", out var s) ? s.GetString() : null,
                            GroupName = part.TryGetProperty("group_name", out var g) ? g.GetString() ?? "" : "",
                            IsDynamic = part.TryGetProperty("is_dynamic", out var d) && d.GetBoolean()
                        });
                    }
                }

                _onLog?.Invoke($"Parsed config: {config.Partitions.Count} partitions");
                return config;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Error parsing JSON: {ex.Message}");
                return null;
            }
        }

        public string? FindSuperDefJson(string romBaseDir)
        {
            var metaDir = Path.Combine(romBaseDir, "META");
            if (!Directory.Exists(metaDir))
            {
                _onLog?.Invoke("META folder not found");
                return null;
            }

            var jsonFiles = Directory.GetFiles(metaDir, "super_def.*.json");
            if (jsonFiles.Length == 0)
            {
                _onLog?.Invoke("super_def.*.json not found in META folder");
                return null;
            }

            _onLog?.Invoke($"Found config: {Path.GetFileName(jsonFiles[0])}");
            return jsonFiles[0];
        }

        public async Task<bool> MergeSuperAsync(string imagesDir, SuperConfig config, string? romBaseDir = null)
        {
            _isCancelled = false;
            
            romBaseDir ??= Directory.GetParent(imagesDir)?.FullName ?? imagesDir;
            
            var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            var simg2imgPath = Path.Combine(toolsDir, "simg2img.exe");
            var lpmakePath = Path.Combine(toolsDir, "lpmake.exe");

            if (!File.Exists(simg2imgPath))
            {
                _onLog?.Invoke("simg2img.exe not found in Tools folder");
                return false;
            }
            if (!File.Exists(lpmakePath))
            {
                _onLog?.Invoke("lpmake.exe not found in Tools folder");
                return false;
            }

            var localSimg2img = Path.Combine(imagesDir, "simg2img.exe");
            var localLpmake = Path.Combine(imagesDir, "lpmake.exe");
            
            try
            {
                File.Copy(simg2imgPath, localSimg2img, true);
                File.Copy(lpmakePath, localLpmake, true);
                _onLog?.Invoke("Tools copied to IMAGES folder");
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Failed to copy tools: {ex.Message}");
                return false;
            }

            var rawFiles = new List<string>();
            var partitionsWithPath = config.Partitions.Where(p => !string.IsNullOrEmpty(p.Path)).ToList();
            int totalPartitions = partitionsWithPath.Count;
            int currentIndex = 0;

            try
            {
                _onLog?.Invoke("Stage 1: Converting img to raw...");
                
                foreach (var partition in partitionsWithPath)
                {
                    if (_isCancelled)
                    {
                        _onLog?.Invoke("Operation cancelled");
                        return false;
                    }

                    var imgPath = Path.Combine(romBaseDir, partition.Path!.Replace("/", "\\"));
                    
                    if (!File.Exists(imgPath))
                    {
                        _onLog?.Invoke($"Warning: {partition.Path} not found, skipping");
                        continue;
                    }

                    var imgFileName = Path.GetFileName(imgPath);
                    var rawFileName = Path.GetFileNameWithoutExtension(imgFileName) + ".raw";
                    var rawPath = Path.Combine(imagesDir, rawFileName);
                    _onLog?.Invoke($"Converting {imgFileName}...");
                    _onProgress?.Invoke((currentIndex * 50) / totalPartitions);
                    var success = await RunProcessAsync(localSimg2img, $"\"{imgPath}\" \"{rawPath}\"", imagesDir);
                    
                    if (success && File.Exists(rawPath))
                    {
                        rawFiles.Add(rawPath);
                        _onLog?.Invoke($"Converted: {imgFileName} -> {rawFileName}");
                    }
                    else
                    {
                        _onLog?.Invoke($"Failed to convert: {imgFileName}");
                    }

                    currentIndex++;
                }

                if (rawFiles.Count == 0)
                {
                    _onLog?.Invoke("No raw files generated, aborting merge");
                    return false;
                }

                _onLog?.Invoke("Stage 2: Merging raw files to super.img...");
                _onProgress?.Invoke(50);

                var outputPath = Path.Combine(imagesDir, "super.img");
                var lpmakeArgs = BuildLpmakeArgs(config, rawFiles, imagesDir, outputPath);

                _onLog?.Invoke($"Running lpmake with {rawFiles.Count} partitions...");
                var mergeSuccess = await RunProcessAsync(localLpmake, lpmakeArgs, imagesDir);

                if (mergeSuccess && File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    var sizeGB = fileInfo.Length / (1024.0 * 1024.0 * 1024.0);
                    _onLog?.Invoke($"Super merge completed! Size: {sizeGB:F2} GB");
                    _onLog?.Invoke($"Output: {outputPath}");
                    _onProgress?.Invoke(100);

                    _onLog?.Invoke("Cleaning up temporary raw files...");
                    foreach (var rawFile in rawFiles)
                    {
                        try
                        {
                            if (File.Exists(rawFile))
                                File.Delete(rawFile);
                        }
                        catch { }
                    }

                    return true;
                }
                else
                {
                    _onLog?.Invoke("lpmake failed to create super.img");
                    return false;
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(localSimg2img)) File.Delete(localSimg2img);
                    if (File.Exists(localLpmake)) File.Delete(localLpmake);
                    _onLog?.Invoke("Tools cleaned up");
                }
                catch { }
            }
        }

        private string BuildLpmakeArgs(SuperConfig config, List<string> rawFiles, string imagesDir, string outputPath)
        {
            var args = new List<string>();

            if (config.SuperMeta != null)
            {
                args.Add($"--metadata-size {config.SuperMeta.Size}");
            }

            args.Add("--metadata-slots 2");

            foreach (var dev in config.BlockDevices)
            {
                args.Add($"--device {dev.Name}:{dev.Size}");
            }

            foreach (var grp in config.Groups)
            {
                if (!string.IsNullOrEmpty(grp.MaximumSize))
                {
                    args.Add($"--group {grp.Name}:{grp.MaximumSize}");
                }
            }

            var partitionsWithPath = config.Partitions.Where(p => !string.IsNullOrEmpty(p.Path)).ToList();
            int rawIndex = 0;

            foreach (var part in partitionsWithPath)
            {
                if (rawIndex >= rawFiles.Count) break;
                var rawFile = rawFiles[rawIndex];
                args.Add($"--partition {part.Name}:readonly:{part.Size}:{part.GroupName}");
                args.Add($"--image {part.Name}=\"{rawFile}\"");
                
                rawIndex++;
            }

            args.Add($"--output \"{outputPath}\"");

            return string.Join(" ", args);
        }

        private async Task<bool> RunProcessAsync(string fileName, string arguments, string workingDir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                
                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data) && ProcessRunner.VerboseLogging)
                    {
                        _onLog?.Invoke($"[tool] {e.Data}");
                    }
                };
                
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data) && ProcessRunner.VerboseLogging)
                    {
                        _onLog?.Invoke($"[tool ERR] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit());

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Process error: {ex.Message}");
                return false;
            }
        }
    }
}
