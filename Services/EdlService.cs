using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OplusEdlTool.Services
{
    public class EdlService
    {
        private readonly System.Action<string>? onLine;
        private readonly System.Action<int>? onPercent;
        public string StorageType { get; set; } = "ufs";
        private string? ToolsDirCache;
        private string? WorkDirCache;
        public EdlService(System.Action<string>? onLine = null, System.Action<int>? onPercent = null)
        {
            this.onLine = onLine;
            this.onPercent = onPercent;
        }
        private string FindToolsDir()
        {
            if (ToolsDirCache != null) return ToolsDirCache;
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, "Tools");
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "fh_loader.exe")))
                {
                    ToolsDirCache = full;
                    return full;
                }
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new DirectoryNotFoundException("Tools folder not found. Please ensure fh_loader.exe, QSaharaServer.exe, lsusb.exe are in the Tools folder.");
        }
        private string GetWorkDir()
        {
            if (WorkDirCache != null) return WorkDirCache;
            var workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OplusEdlTool");
            if (!Directory.Exists(workDir)) Directory.CreateDirectory(workDir);
            WorkDirCache = workDir;
            return workDir;
        }
        private string BuildDevicePath(string com) => "\\\\.\\" + com;
        
        public async Task<string> WaitForEdlPortAsync(CancellationToken cancellationToken = default)
        {
            var bin = FindToolsDir();
            while (!cancellationToken.IsCancellationRequested)
            {
                var res = await ProcessRunner.RunAsync(Path.Combine(bin, "lsusb.exe"), "", bin, null, onPercent);
                var m = Regex.Match(res.Item2, @"Qualcomm HS-USB QDLoader 9008 \(COM(?<n>\d+)\)");
                if (m.Success) return "COM" + m.Groups["n"].Value;
                
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            
            cancellationToken.ThrowIfCancellationRequested();
            return string.Empty;
        }
        public async Task<bool> SendProgrammerAsync(string port, string devprgPath)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var device = BuildDevicePath(port);
            var args = $"-p {device} -s 13:\"{devprgPath}\"";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "QSaharaServer.exe"), args, workDir, onLine, onPercent);
            return res.Item1 == 0;
        }
        public async Task<bool> SendDigestsAsync(string port, string digestPath)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var device = BuildDevicePath(port);
            var args = $"--port={device} --signeddigests=\"{digestPath}\" --testvipimpact --noprompt --skip_configure --mainoutputdir=\"{workDir}\"";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            return true;
        }
        public async Task<bool> SendVerifyAsync(string port)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var xml = Path.Combine(workDir, "cmd.xml");
            await File.WriteAllTextAsync(xml, "<?xml version=\"1.0\"?><data><verify value=\"ping\" EnableVip=\"1\"/></data>");
            var device = BuildDevicePath(port);
            var args = $"--port={device} --sendxml=\"{xml}\" --noprompt --skip_configure --mainoutputdir=\"{workDir}\"";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            return res.Item1 == 0;
        }
        public async Task<bool> SendSha256InitAsync(string port)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var xml = Path.Combine(workDir, "cmd.xml");
            await File.WriteAllTextAsync(xml, "<?xml version=\"1.0\"?><data><sha256init Verbose=\"1\"/></data>");
            var device = BuildDevicePath(port);
            var args = $"--port={device} --sendxml=\"{xml}\" --noprompt --skip_configure --mainoutputdir=\"{workDir}\"";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            return res.Item1 == 0;
        }
        public async Task<bool> ConfigureAsync(string port)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var xml = Path.Combine(workDir, "cmd.xml");
            var content = "<?xml version=\"1.0\" ?><data><configure MemoryName=\"" + StorageType + "\" Verbose=\"0\" AlwaysValidate=\"0\" MaxDigestTableSizeInBytes=\"8192\" MaxPayloadSizeToTargetInBytes=\"1048576\" ZlpAwareHost=\"1\" SkipStorageInit=\"0\" /></data>";
            await File.WriteAllTextAsync(xml, content);
            var device = BuildDevicePath(port);
            var args = $"--port={device} --memoryname={StorageType} --configure=\"{xml}\" --search_path=\"{workDir}\" --mainoutputdir=\"{workDir}\" --noprompt";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            return res.Item1 == 0;
        }
        
        public async Task<(string rwmode, string gptmainMode)> TestRwModeAsync(string port)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var xml = Path.Combine(workDir, "cmd.xml");
            var sectorSize = StorageType == "emmc" ? 512 : 4096;
            var device = BuildDevicePath(port);
            var fhLoader = Path.Combine(tools, "fh_loader.exe");

            var content = $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{sectorSize}\" filename=\"tmp.bin\" physical_partition_number=\"0\" label=\"5-35\" start_sector=\"5\" num_partition_sectors=\"31\"/></data>";
            await File.WriteAllTextAsync(xml, content);
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xml}\" --convertprogram2read --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode=oplus_gptbackup --noprompt";
            var res = await ProcessRunner.RunAsync(fhLoader, args, workDir, onLine, onPercent);
            if (res.Item1 == 0)
            {
                return ("oplus_gptbackup", "");
            }

            content = $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{sectorSize}\" filename=\"tmp.bin\" physical_partition_number=\"0\" label=\"33-35\" start_sector=\"33\" num_partition_sectors=\"3\"/></data>";
            await File.WriteAllTextAsync(xml, content);
            args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xml}\" --convertprogram2read --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode=oplus_gptmain --noprompt";
            res = await ProcessRunner.RunAsync(fhLoader, args, workDir, onLine, onPercent);
            if (res.Item1 == 0)
            {
                return ("oplus_gptmain", "1");
            }

            return ("oplus_gptmain", "2");
        }

        private async Task<string?> ReadSectorsAsync(string port, int lun, ulong startSector, ulong numSectors, int sectorSize, string fileName, string rwmode)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var xml = Path.Combine(workDir, "cmd.xml");
            var content = "<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"" + sectorSize + "\" filename=\"" + fileName + "\" physical_partition_number=\"" + lun + "\" label=\"read\" start_sector=\"" + startSector + "\" num_partition_sectors=\"" + numSectors + "\"/></data>";
            await File.WriteAllTextAsync(xml, content);
            var device = BuildDevicePath(port);
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xml}\" --convertprogram2read --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --noprompt";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            if (res.Item1 != 0) return null;
            var outPath = Path.Combine(workDir, fileName);
            return File.Exists(outPath) ? outPath : null;
        }
        private static string GuidToString(byte[] b)
        {
            var a = BitConverter.ToInt32(b, 0);
            var c = BitConverter.ToInt16(b, 4);
            var d = BitConverter.ToInt16(b, 6);
            return new Guid(a, c, d, b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]).ToString();
        }
        public async Task<List<PartitionEntry>?> ReadPartitionTableAsync(string port, string rwmode)
        {
            var sectorSize = StorageType == "emmc" ? 512 : 4096;
            var list = new List<PartitionEntry>();
            int maxLun = StorageType == "ufs" ? 12 : 0;
            for (int lun = 0; lun <= maxLun; lun++)
            {
                var hdrPath = await ReadSectorsAsync(port, lun, 1, 1UL, sectorSize, $"gpt_hdr_lun{lun}.bin", rwmode);
                if (hdrPath == null) continue;
                var hdr = await File.ReadAllBytesAsync(hdrPath);
                if (hdr.Length < sectorSize) continue;
                if (System.Text.Encoding.ASCII.GetString(hdr, 0, 8) != "EFI PART") continue;
                var peLba = BitConverter.ToUInt64(hdr, 72);
                var peCount = BitConverter.ToUInt32(hdr, 80);
                var peSize = BitConverter.ToUInt32(hdr, 84);
                var bytesToRead = (ulong)peCount * (ulong)peSize;
                var sectors = (bytesToRead + (ulong)sectorSize - 1) / (ulong)sectorSize;
                var entPath = await ReadSectorsAsync(port, lun, peLba, sectors, sectorSize, $"gpt_ent_lun{lun}.bin", rwmode);
                if (entPath == null) continue;
                var ent = await File.ReadAllBytesAsync(entPath);
                for (int i = 0; i < peCount; i++)
                {
                    var off = i * (int)peSize;
                    if (off + peSize > ent.Length) break;
                    var typeGuidBytes = ent.Skip(off).Take(16).ToArray();
                    var firstLba = BitConverter.ToUInt64(ent, off + 32);
                    var lastLba = BitConverter.ToUInt64(ent, off + 40);
                    var nameBytes = ent.Skip(off + 56).Take(72).ToArray();
                    var name = System.Text.Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                    if (firstLba == 0 && lastLba == 0) continue;
                    var item = new PartitionEntry
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? ($"part{lun}_{i}") : name,
                        Lun = lun,
                        FirstLBA = firstLba,
                        LastLBA = lastLba,
                        SizeBytes = (lastLba - firstLba + 1) * (ulong)sectorSize,
                        TypeGuid = GuidToString(typeGuidBytes)
                    };
                    list.Add(item);
                }
            }
            return list.Count > 0 ? list : null;
        }
        public async Task<string?> BackupPartitionAsync(string port, string rwmode, PartitionEntry part)
        {
            var sectorSize = StorageType == "emmc" ? 512 : 4096;
            var numSectors = (part.SizeBytes / (ulong)sectorSize);
            var fileName = "backup_" + (string.IsNullOrWhiteSpace(part.Name) ? "partition" : part.Name).Replace('\\', '_').Replace('/', '_') + ".bin";
            return await ReadSectorsAsync(port, part.Lun, part.FirstLBA, numSectors, sectorSize, fileName, rwmode);
        }
        public async Task<bool> WritePartitionAsync(string port, string rwmode, PartitionEntry part, string filePath)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var sectorSize = StorageType == "emmc" ? 512 : 4096;
            var len = new FileInfo(filePath).Length;
            var numSectors = (ulong)len / (ulong)sectorSize;
            var xml = Path.Combine(workDir, "cmd.xml");
            var fileName = Path.GetFileName(filePath);
            var content = "<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"" + sectorSize + "\" filename=\"" + fileName + "\" physical_partition_number=\"" + part.Lun + "\" label=\"write\" start_sector=\"" + part.FirstLBA + "\" num_partition_sectors=\"" + numSectors + "\"/></data>";
            await File.WriteAllTextAsync(xml, content);
            var device = BuildDevicePath(port);
            var searchPath = Path.GetDirectoryName(filePath) ?? "";
            if (searchPath.EndsWith("\\")) searchPath = searchPath.TrimEnd('\\');
            if (searchPath.EndsWith(":")) searchPath += ".";
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xml}\" --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --search_path=\"{searchPath}\" --noprompt";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            return res.Item1 == 0;
        }
        
        public async Task<bool> WriteGptAsync(string port, int lun, string filePath)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var sectorSize = StorageType == "emmc" ? 512 : 4096;
            var len = new FileInfo(filePath).Length;
            var numSectors = (ulong)len / (ulong)sectorSize;
            var xml = Path.Combine(workDir, "cmd.xml");
            
            var gptBackupName = $"gpt_backup{lun}.bin";
            var gptBackupPath = Path.Combine(workDir, gptBackupName);
            File.Copy(filePath, gptBackupPath, overwrite: true);
            
            var content = $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{sectorSize}\" filename=\"{gptBackupName}\" physical_partition_number=\"{lun}\" label=\"BackupGPT\" start_sector=\"0\" num_partition_sectors=\"{numSectors}\"/></data>";
            await File.WriteAllTextAsync(xml, content);
            var device = BuildDevicePath(port);
            
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xml}\" --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode=oplus_gptbackup --search_path=\"{workDir}\" --noprompt";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            
            try { File.Delete(gptBackupPath); } catch { }
            
            return res.Item1 == 0;
        }
        public async Task<string?> ReadByXmlAsync(string port, List<string> xmls, string rwmode)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var dirname = "readback_xml_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var outDir = Path.Combine(workDir, dirname);
            Directory.CreateDirectory(outDir);
            var device = BuildDevicePath(port);
            
            var xmlArg = string.Join(",", xmls);
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xmlArg}\" --convertprogram2read --mainoutputdir=\"{outDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --noprompt";
            
            if (ProcessRunner.VerboseLogging) onLine?.Invoke($"[DEBUG] fh_loader args: {args}");
            
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            return res.Item1 == 0 ? outDir : null;
        }

        public async Task<string?> ReadByAutoAsync(string port, string rwmode)
        {
            var workDir = GetWorkDir();
            var dirname = "readback_auto_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var outDir = Path.Combine(workDir, dirname);
            Directory.CreateDirectory(outDir);

            onLine?.Invoke("Reading partition table...");
            var partitions = await ReadPartitionTableAsync(port, rwmode);
            if (partitions == null || partitions.Count == 0)
            {
                onLine?.Invoke("Failed to read partition table");
                return null;
            }
            onLine?.Invoke($"Found {partitions.Count} partitions");

            var sectorSize = StorageType == "emmc" ? 512 : 4096;
            foreach (var part in partitions)
            {
                onLine?.Invoke($"Reading partition: {part.Name} (LUN {part.Lun})");
                var numSectors = (part.SizeBytes / (ulong)sectorSize);
                var fileName = $"{part.Name}.bin";
                var xml = Path.Combine(workDir, $"read_{part.Name}.xml");
                var content = $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{sectorSize}\" filename=\"{fileName}\" physical_partition_number=\"{part.Lun}\" label=\"{part.Name}\" start_sector=\"{part.FirstLBA}\" num_partition_sectors=\"{numSectors}\"/></data>";
                await File.WriteAllTextAsync(xml, content);
                var xmlList = new List<string> { xml };
                var partOutDir = await ReadByXmlAsync(port, xmlList, rwmode);
                try { File.Delete(xml); } catch { }
                if (partOutDir == null)
                {
                    onLine?.Invoke($"Failed to read partition: {part.Name}");
                    continue;
                }
                
                try
                {
                    var sourceFile = Path.Combine(partOutDir, fileName);
                    var destFile = Path.Combine(outDir, fileName);
                    if (File.Exists(sourceFile))
                    {
                        File.Move(sourceFile, destFile, overwrite: true);
                    }
                    Directory.Delete(partOutDir, recursive: true);
                }
                catch (Exception ex)
                {
                    onLine?.Invoke($"Warning: Failed to move file for {part.Name}: {ex.Message}");
                }
            }
            onLine?.Invoke($"Auto read completed. Output: {outDir}");
            return outDir;
        }

        public async Task<bool> WriteByXmlAsync(string port, List<string> xmls, string rwmode, string searchPath)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var device = BuildDevicePath(port);
            if (searchPath.EndsWith("\\")) searchPath = searchPath.TrimEnd('\\');
            if (searchPath.EndsWith(":")) searchPath += ".";
            var rwmodeArg = string.IsNullOrEmpty(rwmode) ? "" : $"--special_rw_mode={rwmode}";
            
            foreach (var xmlPath in xmls)
            {
                var partitionInfos = ParsePartitionInfoFromXml(xmlPath);
                foreach (var info in partitionInfos)
                {
                    onLine?.Invoke($"Flashing: {info.label}");
                    
                    var singleXml = GenerateSinglePartitionXml(xmlPath, info.label, workDir);
                    if (singleXml == null) continue;
                    var args = $"--port={device} --memoryname={StorageType} --search_path=\"{searchPath}\" --sendxml=\"{singleXml}\" --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete {rwmodeArg} --noprompt";
                    
                    if (ProcessRunner.VerboseLogging) onLine?.Invoke($"[DEBUG] fh_loader args: {args}");
                    
                    var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
                    
                    try { File.Delete(singleXml); } catch { }
                    
                    if (res.Item1 != 0)
                    {
                        onLine?.Invoke($"Failed to flash: {info.label}");
                        return false;
                    }
                }
            }
            
            return true;
        }

        public async Task<bool> WriteByXmlWithOplusRwModeAsync(string port, List<string> xmls, string searchPath)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var device = BuildDevicePath(port);
            
            var (rwmode, gptmainMode) = await TestRwModeAsync(port);
            
            if (searchPath.EndsWith("\\")) searchPath = searchPath.TrimEnd('\\');
            if (searchPath.EndsWith(":")) searchPath += ".";
            
            foreach (var xmlPath in xmls)
            {
                var partitionInfos = ParsePartitionInfoFromXml(xmlPath);
                foreach (var info in partitionInfos)
                {
                    onLine?.Invoke($"Flashing: {info.label}");
                    
                    var singleXml = GenerateSinglePartitionXml(xmlPath, info.label, workDir);
                    if (singleXml == null) continue;
                    
                    var args = $"--port={device} --memoryname={StorageType} --search_path=\"{searchPath}\" --sendxml=\"{singleXml}\" --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --noprompt";
                    
                    if (ProcessRunner.VerboseLogging) onLine?.Invoke($"[DEBUG] fh_loader args: {args}");
                    
                    var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
                    
                    try { File.Delete(singleXml); } catch { }
                    
                    if (res.Item1 != 0)
                    {
                        onLine?.Invoke($"Failed to flash: {info.label}");
                        return false;
                    }
                }
            }
            
            return true;
        }

        public async Task<bool> WriteByXmlWithNewOplusRwModeAsync(string port, List<string> xmls, string searchPath)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var device = BuildDevicePath(port);
            var fhLoader = Path.Combine(tools, "fh_loader.exe");

            var originalVerbose = ProcessRunner.VerboseLogging;
            ProcessRunner.VerboseLogging = true;
            onLine?.Invoke("[New Oplus RW Mode] Verbose logging enabled");

            onLine?.Invoke("Testing RW mode...");
            var (rwmode, gptmainMode) = await TestRwModeAsync(port);
            onLine?.Invoke($"RW mode detected: {rwmode}" + (string.IsNullOrEmpty(gptmainMode) ? "" : $" mode={gptmainMode}"));

            if (searchPath.EndsWith("\\")) searchPath = searchPath.TrimEnd('\\');
            if (searchPath.EndsWith(":")) searchPath += ".";

            onLine?.Invoke($"[DEBUG] Total XML files to process: {xmls.Count}");
            foreach (var xmlPath in xmls)
            {
                onLine?.Invoke($"[DEBUG] Processing XML: {Path.GetFileName(xmlPath)}");
                
                var allPartitions = ParseFullPartitionInfoFromXml(xmlPath);
                onLine?.Invoke($"[DEBUG] Found {allPartitions.Count} partitions in {Path.GetFileName(xmlPath)}");
                
                if (allPartitions.Count == 0)
                {
                    onLine?.Invoke($"No partitions found in {Path.GetFileName(xmlPath)}");
                    continue;
                }

                var partitionsByLun = allPartitions.GroupBy(p => p.physicalPartitionNumber).ToDictionary(g => g.Key, g => g.ToList());
                onLine?.Invoke($"[DEBUG] Grouped into {partitionsByLun.Count} LUNs");

                foreach (var lunGroup in partitionsByLun)
                {
                    var lun = lunGroup.Key;
                    var partitions = lunGroup.Value;
                    onLine?.Invoke($"[DEBUG] LUN {lun}: {partitions.Count} partitions");

                    var firstPartition = partitions.FirstOrDefault();
                    if (firstPartition != null)
                    {
                        onLine?.Invoke($"[DEBUG] First partition of LUN {lun}: {firstPartition.label} (filename={firstPartition.filename})");
                    }

                    foreach (var partition in partitions)
                    {
                        onLine?.Invoke($"Flashing: {partition.label} (LUN {lun}, sector {partition.startSector}-{partition.EndSector - 1}, size={partition.numPartitionSectors})");

                        var modifiedXml = GenerateModifiedPartitionXml(
                            xmlPath, partition, firstPartition, rwmode, gptmainMode, workDir);
                        
                        if (modifiedXml == null)
                        {
                            onLine?.Invoke($"Failed to generate XML for: {partition.label}");
                            return false;
                        }

                        var args = $"--port={device} --memoryname={StorageType} --search_path=\"{searchPath}\" --sendxml=\"{modifiedXml}\" --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --noprompt";

                        onLine?.Invoke($"[DEBUG] fh_loader command: {fhLoader}");
                        onLine?.Invoke($"[DEBUG] fh_loader args: {args}");

                        var res = await ProcessRunner.RunAsync(fhLoader, args, workDir, onLine, onPercent);
                        onLine?.Invoke($"[DEBUG] fh_loader exit code: {res.Item1}");

                        onLine?.Invoke($"[DEBUG] Modified XML saved: {modifiedXml}");

                        if (res.Item1 != 0)
                        {
                            onLine?.Invoke($"[ERROR] Failed to flash: {partition.label}");
                            ProcessRunner.VerboseLogging = originalVerbose;
                            return false;
                        }
                        else
                        {
                            onLine?.Invoke($"[SUCCESS] {partition.label} flashed successfully");
                        }
                    }
                }
            }

            ProcessRunner.VerboseLogging = originalVerbose;
            onLine?.Invoke($"[New Oplus RW Mode] All partitions flashed successfully");
            return true;
        }

        private List<PartitionXmlInfo> ParseFullPartitionInfoFromXml(string xmlPath)
        {
            var partitions = new List<PartitionXmlInfo>();
            var skipLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "PrimaryGPT", "BackupGPT"
            };

            try
            {
                if (!File.Exists(xmlPath)) return partitions;
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                var programs = doc.Descendants("program");
                foreach (var program in programs)
                {
                    var label = program.Attribute("label")?.Value ?? "";
                    var filename = program.Attribute("filename")?.Value ?? "";
                    var physicalPartitionNumber = int.TryParse(program.Attribute("physical_partition_number")?.Value, out var ppn) ? ppn : 0;
                    var startSector = ulong.TryParse(program.Attribute("start_sector")?.Value, out var ss) ? ss : 0;
                    var numPartitionSectors = ulong.TryParse(program.Attribute("num_partition_sectors")?.Value, out var nps) ? nps : 0;

                    if (!skipLabels.Contains(label))
                    {
                        partitions.Add(new PartitionXmlInfo
                        {
                            label = label,
                            filename = filename,
                            physicalPartitionNumber = physicalPartitionNumber,
                            startSector = startSector,
                            numPartitionSectors = numPartitionSectors
                        });
                    }
                }
            }
            catch { }
            return partitions;
        }

        private class PartitionXmlInfo
        {
            public string label { get; set; } = "";
            public string filename { get; set; } = "";
            public int physicalPartitionNumber { get; set; }
            public ulong startSector { get; set; }
            public ulong numPartitionSectors { get; set; }

            public ulong EndSector => startSector + numPartitionSectors;
        }

        private string? GenerateModifiedPartitionXml(
            string sourceXmlPath,
            PartitionXmlInfo partition,
            PartitionXmlInfo? firstPartition,
            string rwmode,
            string gptmainMode,
            string workDir)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(sourceXmlPath);
                var dataElement = doc.Element("data");
                if (dataElement == null) return null;

                var programs = dataElement.Elements("program").ToList();

                System.Xml.Linq.XElement? targetProgram = null;
                foreach (var program in programs)
                {
                    var label = program.Attribute("label")?.Value ?? "";
                    if (label.Equals(partition.label, StringComparison.OrdinalIgnoreCase))
                    {
                        targetProgram = program;
                        break;
                    }
                }

                if (targetProgram == null) return null;

                foreach (var program in programs.ToList())
                {
                    if (program != targetProgram)
                    {
                        program.Remove();
                    }
                }

                ModifyProgramElement(targetProgram, partition, firstPartition, rwmode, gptmainMode);

                var outputPath = Path.Combine(workDir, $"modified_{partition.label}_{Guid.NewGuid():N}.xml");
                doc.Save(outputPath);
                return outputPath;
            }
            catch
            {
                return null;
            }
        }

        private void ModifyProgramElement(
            System.Xml.Linq.XElement program,
            PartitionXmlInfo partition,
            PartitionXmlInfo? firstPartition,
            string rwmode,
            string gptmainMode)
        {
            var startSector = partition.startSector;
            var endSector = partition.EndSector; 

            if (rwmode == "oplus_gptbackup")
            {
                program.SetAttributeValue("filename", "gpt_backup0.bin");
                program.SetAttributeValue("label", "BackupGPT");
            }
            else if (rwmode == "oplus_gptmain")
            {
                if (gptmainMode == "1")
                {
                    bool coversSpecialSector = (startSector <= 6 && endSector > 6);

                    if (coversSpecialSector && firstPartition != null)
                    {
                        program.SetAttributeValue("filename", firstPartition.filename);
                        program.SetAttributeValue("label", firstPartition.label);
                        program.SetAttributeValue("physical_partition_number", firstPartition.physicalPartitionNumber.ToString());
                    }
                    else
                    {
                        program.SetAttributeValue("filename", "gpt_main0.bin");
                        program.SetAttributeValue("label", "PrimaryGPT");
                    }
                }
                else if (gptmainMode == "2")
                {
                    bool coversSpecialSector = (startSector <= 34 && endSector > 34);

                    if (coversSpecialSector && firstPartition != null)
                    {
                        program.SetAttributeValue("filename", firstPartition.filename);
                        program.SetAttributeValue("label", firstPartition.label);
                        program.SetAttributeValue("physical_partition_number", firstPartition.physicalPartitionNumber.ToString());
                    }
                    else
                    {
                        program.SetAttributeValue("filename", "gpt_main0.bin");
                        program.SetAttributeValue("label", "PrimaryGPT");
                    }
                }
            }
        }
        
        private List<(string label, string filename)> ParsePartitionInfoFromXml(string xmlPath)
        {
            var partitions = new List<(string label, string filename)>();
            var skipLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "PrimaryGPT", "BackupGPT"
            };
            
            try
            {
                if (!File.Exists(xmlPath)) return partitions;
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                var programs = doc.Descendants("program");
                foreach (var program in programs)
                {
                    var label = program.Attribute("label")?.Value;
                    var filename = program.Attribute("filename")?.Value;
                    if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(filename) && !skipLabels.Contains(label))
                    {
                        partitions.Add((label, filename));
                    }
                }
            }
            catch { }
            return partitions;
        }
        
        private string? GenerateSinglePartitionXml(string sourceXmlPath, string targetLabel, string workDir)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(sourceXmlPath);
                var dataElement = doc.Element("data");
                if (dataElement == null) return null;
                
                var programs = dataElement.Elements("program").ToList();
                var toRemove = new List<System.Xml.Linq.XElement>();
                
                foreach (var program in programs)
                {
                    var label = program.Attribute("label")?.Value ?? "";
                    if (!label.Equals(targetLabel, StringComparison.OrdinalIgnoreCase) &&
                        !label.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase) &&
                        !label.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase))
                    {
                        toRemove.Add(program);
                    }
                }
                
                foreach (var elem in toRemove)
                {
                    elem.Remove();
                }
                
                var outputPath = Path.Combine(workDir, $"single_{targetLabel}_{Guid.NewGuid():N}.xml");
                doc.Save(outputPath);
                return outputPath;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> WritePatchXmlAsync(string port, string patchXmlPath, string rwmode)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var device = BuildDevicePath(port);
            
            if (!File.Exists(patchXmlPath))
            {
                onLine?.Invoke($"Patch file not found: {patchXmlPath}");
                return false;
            }

            var patchFileName = Path.GetFileName(patchXmlPath);
            onLine?.Invoke($"Writing patch: {patchFileName}...");
            
            var rwmodeArg = string.IsNullOrEmpty(rwmode) ? "" : $"--special_rw_mode={rwmode}";
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{patchXmlPath}\" --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete {rwmodeArg} --noprompt";
            
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            
            if (res.Item1 == 0)
            {
                onLine?.Invoke($"Patch {patchFileName} applied successfully");
                return true;
            }
            else
            {
                onLine?.Invoke($"Failed to apply patch {patchFileName}");
                return false;
            }
        }

        public async Task<int> WritePatchXmlsAsync(string port, IEnumerable<string> selectedRawProgramXmls, string imagesPath, string rwmode)
        {
            int successCount = 0;
            var patchNumbers = new List<int>();

            foreach (var xmlPath in selectedRawProgramXmls)
            {
                var fileName = Path.GetFileName(xmlPath).ToLower();
                if (fileName.StartsWith("rawprogram") && fileName.EndsWith(".xml"))
                {
                    var numStr = fileName.Replace("rawprogram", "").Replace(".xml", "");
                    if (int.TryParse(numStr, out int num))
                    {
                        if (!patchNumbers.Contains(num))
                            patchNumbers.Add(num);
                    }
                }
            }

            patchNumbers.Sort();

            if (patchNumbers.Count == 0)
            {
                onLine?.Invoke("No patch files to apply");
                return 0;
            }

            onLine?.Invoke($"Applying {patchNumbers.Count} patch file(s): patch{string.Join(", patch", patchNumbers)}.xml");

            foreach (var num in patchNumbers)
            {
                var patchFileName = $"patch{num}.xml";
                var patchPath = Path.Combine(imagesPath, patchFileName);

                if (!File.Exists(patchPath))
                {
                    onLine?.Invoke($"Patch file not found, skipping: {patchFileName}");
                    continue;
                }

                var ok = await WritePatchXmlAsync(port, patchPath, rwmode);
                if (ok)
                {
                    successCount++;
                }
                else
                {
                    onLine?.Invoke($"Warning: Failed to apply {patchFileName}, continuing with next patch...");
                }
            }

            onLine?.Invoke($"Patch application completed: {successCount}/{patchNumbers.Count} successful");
            return successCount;
        }

        public async Task<bool> ErasePartitionAsync(string port, string rwmode, PartitionEntry part)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var sectorSize = StorageType == "emmc" ? 512 : 4096;
            var xml = Path.Combine(workDir, "cmd.xml");
            var label = string.IsNullOrWhiteSpace(part.Name) ? "partition" : part.Name;
            var content = $"<?xml version=\"1.0\" ?><data><erase SECTOR_SIZE_IN_BYTES=\"{sectorSize}\" label=\"{label}\" physical_partition_number=\"{part.Lun}\" start_sector=\"{part.FirstLBA}\" num_partition_sectors=\"{part.LastLBA - part.FirstLBA + 1}\" /></data>";
            await File.WriteAllTextAsync(xml, content);
            var device = BuildDevicePath(port);
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xml}\" --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --noprompt";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            return res.Item1 == 0;
        }
        public async Task<bool> CleanupAsync()
        {
            var workDir = GetWorkDir();
            var ok = true;
            var f1 = Path.Combine(workDir, "cmd.xml");
            var f2 = Path.Combine(workDir, "tmp.bin");
            var f3 = Path.Combine(workDir, "port_trace.txt");
            await Task.Run(() => { try { if (File.Exists(f1)) File.Delete(f1); } catch { ok = false; } });
            await Task.Run(() => { try { if (File.Exists(f2)) File.Delete(f2); } catch { ok = false; } });
            await Task.Run(() => { try { if (File.Exists(f3)) File.Delete(f3); } catch { ok = false; } });
            return ok;
        }

        public async Task<bool> RebootToEdlAsync(string port)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var device = BuildDevicePath(port);
            
            var xml = Path.Combine(workDir, "reboot_cmd.xml");
            var content = "<?xml version=\"1.0\" ?><data><power value=\"reset\" /></data>";
            await File.WriteAllTextAsync(xml, content);
            
            onLine?.Invoke("Sending reboot command...");
            
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xml}\" --mainoutputdir=\"{workDir}\" --skip_configure --noprompt";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            
            try { File.Delete(xml); } catch { }
            
            if (res.Item1 == 0)
            {
                onLine?.Invoke("Device is rebooting...");
                return true;
            }
            else
            {
                onLine?.Invoke("Failed to send reboot command");
                return false;
            }
        }

        public void CleanupWorkDir()
        {
            try
            {
                var workDir = GetWorkDir();
                if (Directory.Exists(workDir))
                {
                    var preserveFiles = new List<string> { "language.json" };

                    foreach (var file in Directory.GetFiles(workDir))
                    {
                        var fileName = Path.GetFileName(file);
                        if (!preserveFiles.Contains(fileName))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                    foreach (var dir in Directory.GetDirectories(workDir))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }
        }

        public async Task<(ulong startSector, ulong numSectors)?> ReadSuperPartitionInfoAsync(string port, string rwmode)
        {
            var tools = FindToolsDir();
            var workDir = GetWorkDir();
            var sectorSize = StorageType == "emmc" ? 512 : 4096;

            var gptXml = Path.Combine(tools, "ufs_gpt.xml");
            if (!File.Exists(gptXml))
            {
                onLine?.Invoke("ufs_gpt.xml not found in Tools folder");
                return null;
            }

            var device = BuildDevicePath(port);
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{gptXml}\" --convertprogram2read --mainoutputdir=\"{workDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --noprompt";
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, workDir, onLine, onPercent);
            if (res.Item1 != 0)
            {
                onLine?.Invoke("Failed to read GPT from device");
                return null;
            }

            var gptPath = Path.Combine(workDir, "gpt_main0.bin");
            if (!File.Exists(gptPath))
            {
                onLine?.Invoke("gpt_main0.bin not found");
                return null;
            }

            return ParseSuperFromGpt(gptPath, sectorSize);
        }

        private (ulong startSector, ulong numSectors)? ParseSuperFromGpt(string gptPath, int sectorSize)
        {
            try
            {
                var data = File.ReadAllBytes(gptPath);

                int headerOffset = sectorSize;
                if (data.Length < headerOffset + 8)
                {
                    onLine?.Invoke("GPT file too small");
                    return null;
                }

                var signature = System.Text.Encoding.ASCII.GetString(data, headerOffset, 8);
                if (signature != "EFI PART")
                {
                    onLine?.Invoke($"Invalid GPT signature: {signature}");
                    return null;
                }

                int entryOffset = 2 * sectorSize;
                int entrySize = 128; 

                for (int i = 0; i < 128; i++) 
                {
                    int offset = entryOffset + i * entrySize;
                    if (offset + entrySize > data.Length)
                        break;

                    bool isEmpty = true;
                    for (int j = 0; j < 16; j++)
                    {
                        if (data[offset + j] != 0)
                        {
                            isEmpty = false;
                            break;
                        }
                    }
                    if (isEmpty) continue;

                    ulong firstLba = BitConverter.ToUInt64(data, offset + 32);
                    ulong lastLba = BitConverter.ToUInt64(data, offset + 40);

                    var nameBytes = new byte[72];
                    Array.Copy(data, offset + 56, nameBytes, 0, 72);
                    var name = System.Text.Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');

                    if (name.Equals("super", StringComparison.OrdinalIgnoreCase))
                    {
                        ulong numSectors = lastLba - firstLba + 1;
                        onLine?.Invoke($"Found super partition: start={firstLba}, sectors={numSectors}, size={numSectors * (ulong)sectorSize / 1024 / 1024 / 1024} GB");
                        return (firstLba, numSectors);
                    }
                }

                onLine?.Invoke("super partition not found in GPT");
                return null;
            }
            catch (Exception ex)
            {
                onLine?.Invoke($"Error parsing GPT: {ex.Message}");
                return null;
            }
        }

        private string CreateSuperPartitionXml(ulong startSector, ulong numSectors, int sectorSize, string outputFileName)
        {
            var workDir = GetWorkDir();
            var xmlPath = Path.Combine(workDir, "super_read.xml");
            var content = "<?xml version=\"1.0\" ?>\n" +
                          "<data>\n" +
                          $"  <program SECTOR_SIZE_IN_BYTES=\"{sectorSize}\" filename=\"{outputFileName}\" physical_partition_number=\"0\" label=\"super\" start_sector=\"{startSector}\" num_partition_sectors=\"{numSectors}\"/>\n" +
                          "</data>";
            File.WriteAllText(xmlPath, content);
            return xmlPath;
        }

        public async Task<string?> BackupSuperPartitionAsync(string port, string rwmode, string outputDir)
        {
            var sectorSize = StorageType == "emmc" ? 512 : 4096;

            onLine?.Invoke("Reading GPT to get super partition info...");
            var superInfo = await ReadSuperPartitionInfoAsync(port, rwmode);
            if (superInfo == null)
            {
                onLine?.Invoke("Failed to get super partition info");
                return null;
            }

            var (startSector, numSectors) = superInfo.Value;

            var outputFileName = "super.bin";
            var xmlPath = CreateSuperPartitionXml(startSector, numSectors, sectorSize, outputFileName);
            onLine?.Invoke($"Created super partition XML: start_sector={startSector}, num_sectors={numSectors}");

            var tools = FindToolsDir();
            var device = BuildDevicePath(port);
            var args = $"--port={device} --memoryname={StorageType} --sendxml=\"{xmlPath}\" --convertprogram2read --mainoutputdir=\"{outputDir}\" --skip_configure --showpercentagecomplete --special_rw_mode={rwmode} --noprompt";
            
            onLine?.Invoke("Starting super partition backup (this may take a while)...");
            var res = await ProcessRunner.RunAsync(Path.Combine(tools, "fh_loader.exe"), args, tools, onLine, onPercent);
            
            if (res.Item1 != 0)
            {
                onLine?.Invoke("Failed to backup super partition");
                return null;
            }

            var outputPath = Path.Combine(outputDir, outputFileName);
            
            var portTraceFile = Path.Combine(outputDir, "port_trace.txt");
            if (File.Exists(portTraceFile))
            {
                try { File.Delete(portTraceFile); } catch { }
            }
            
            if (File.Exists(outputPath))
            {
                onLine?.Invoke($"Super partition backed up successfully: {outputPath}");
                return outputPath;
            }

            onLine?.Invoke("Super partition backup file not found");
            return null;
        }

        public static bool IsSuperPartition(PartitionEntry part)
        {
            return part.Name.Equals("super", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSuperPartition(string partitionName)
        {
            return partitionName.Equals("super", StringComparison.OrdinalIgnoreCase);
        }
    }
}
