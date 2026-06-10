using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OplusEdlTool.Services
{
    public class RawProgramXmlProcessor
    {
        private readonly Action<string>? _onLog;
        private readonly GptParser _gptParser;

        public RawProgramXmlProcessor(Action<string>? onLog = null)
        {
            _onLog = onLog;
            _gptParser = new GptParser(onLog);
        }

        private void Log(string message)
        {
            _onLog?.Invoke(message);
        }

        public static bool IsSpecialRawProgram(string xmlPath)
        {
            var fileName = Path.GetFileName(xmlPath).ToLower();
            return fileName == "rawprogram0.xml" || fileName == "rawprogram5.xml";
        }

        public static int GetPhysicalPartitionNumber(string xmlPath)
        {
            var fileName = Path.GetFileName(xmlPath).ToLower();
            if (fileName == "rawprogram0.xml") return 0;
            if (fileName == "rawprogram5.xml") return 5;
            return -1;
        }

        public bool ProcessRawProgramXml(string xmlPath, string imagesPath, string outputPath)
        {
            try
            {
                var fileName = Path.GetFileName(xmlPath).ToLower();
                int partitionNumber = GetPhysicalPartitionNumber(xmlPath);
                
                if (partitionNumber < 0)
                {
                    Log($"Not a special rawprogram file: {fileName}");
                    return false;
                }

                var gptFileName = $"gpt_main{partitionNumber}.bin";
                var gptPath = Path.Combine(imagesPath, gptFileName);
                
                bool hasGptFile = File.Exists(gptPath);
                Dictionary<string, GptPartitionInfo> partitionDict = new(StringComparer.OrdinalIgnoreCase);
                int sectorSize = 4096; 

                if (hasGptFile)
                {
                    var (gptPartitions, parsedSectorSize) = _gptParser.ParseGpt(gptPath);
                    sectorSize = parsedSectorSize;
                    
                    if (gptPartitions.Count > 0)
                    {
                        partitionDict = gptPartitions.ToDictionary(
                            p => p.Label, 
                            p => p, 
                            StringComparer.OrdinalIgnoreCase);
                    }
                }

                var doc = XDocument.Load(xmlPath);
                var dataElement = doc.Element("data");
                if (dataElement == null)
                {
                    Log("Invalid XML: no <data> element");
                    return false;
                }

                var programs = dataElement.Elements("program").ToList();
                var gptMainProgram = (XElement?)null;
                var gptBackupProgram = (XElement?)null;
                var otherPrograms = new List<XElement>();

                foreach (var program in programs)
                {
                    var label = program.Attribute("label")?.Value ?? "";
                    var programFilename = program.Attribute("filename")?.Value ?? "";

                    if (label == "PrimaryGPT" || programFilename == $"gpt_main{partitionNumber}.bin")
                    {
                        gptMainProgram = program;
                        continue;
                    }
                    if (label == "BackupGPT" || programFilename == $"gpt_backup{partitionNumber}.bin")
                    {
                        gptBackupProgram = program;
                        continue;
                    }

                    if (partitionDict.TryGetValue(label, out var gptInfo))
                    {
                        program.SetAttributeValue("start_sector", gptInfo.StartSector.ToString());
                        program.SetAttributeValue("num_partition_sectors", gptInfo.NumPartitionSectors.ToString());
                        program.SetAttributeValue("start_byte_hex", gptInfo.StartByteHex);
                        program.SetAttributeValue("size_in_KB", gptInfo.SizeInKB.ToString("F1"));
                        program.SetAttributeValue("SECTOR_SIZE_IN_BYTES", sectorSize.ToString());
                    }

                    bool needSetSparseToFalse = 
                        (partitionNumber == 0 && (label.Equals("super", StringComparison.OrdinalIgnoreCase) || 
                                                   label.Equals("userdata", StringComparison.OrdinalIgnoreCase))) ||
                        (partitionNumber == 5 && label.Equals("oplusreserve2", StringComparison.OrdinalIgnoreCase));
                    
                    if (needSetSparseToFalse)
                    {
                        var currentSparse = program.Attribute("sparse")?.Value ?? "false";
                        if (currentSparse.ToLower() == "true")
                        {
                            program.SetAttributeValue("sparse", "false");
                        }
                    }

                    otherPrograms.Add(program);
                }

                if (gptMainProgram == null)
                {
                    gptMainProgram = new XElement("program",
                        new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString()),
                        new XAttribute("file_sector_offset", "0"),
                        new XAttribute("filename", $"gpt_main{partitionNumber}.bin"),
                        new XAttribute("label", "PrimaryGPT"),
                        new XAttribute("num_partition_sectors", "6"),
                        new XAttribute("partofsingleimage", "true"),
                        new XAttribute("physical_partition_number", partitionNumber.ToString()),
                        new XAttribute("readbackverify", "false"),
                        new XAttribute("size_in_KB", (6.0 * sectorSize / 1024).ToString("F1")),
                        new XAttribute("sparse", "false"),
                        new XAttribute("start_byte_hex", "0x0"),
                        new XAttribute("start_sector", "0")
                    );
                }
                else
                {
                    gptMainProgram.SetAttributeValue("start_sector", "0");
                    gptMainProgram.SetAttributeValue("start_byte_hex", "0x0");
                    gptMainProgram.SetAttributeValue("num_partition_sectors", "6");
                    gptMainProgram.SetAttributeValue("size_in_KB", (6.0 * sectorSize / 1024).ToString("F1"));
                }

                dataElement.RemoveNodes();

                dataElement.Add(new XComment("NOTE: This is an ** Autogenerated file **"));
                dataElement.Add(new XComment($"NOTE: Sector size is {sectorSize}bytes"));
                dataElement.Add(new XComment($"NOTE: Modified by OPLUS EDL Tool for rawprogram{partitionNumber}.xml"));

                dataElement.Add(gptMainProgram);

                foreach (var program in otherPrograms)
                {
                    dataElement.Add(program);
                }

                if (gptBackupProgram != null)
                {
                    dataElement.Add(gptBackupProgram);
                }

                doc.Save(outputPath);

                return true;
            }
            catch (Exception ex)
            {
                Log($"Error processing rawprogram XML: {ex.Message}");
                return false;
            }
        }

        public (List<string> specialXmlFiles, List<string> normalXmlFiles) PrepareXmlFilesForFlash(
            IEnumerable<string> xmlFiles, 
            string imagesPath, 
            string workDir)
        {
            var specialXmlFiles = new List<string>();
            var normalXmlFiles = new List<string>();

            foreach (var xmlPath in xmlFiles)
            {
                if (IsSpecialRawProgram(xmlPath))
                {
                    var outputFileName = "modified_" + Path.GetFileName(xmlPath);
                    var outputPath = Path.Combine(workDir, outputFileName);

                    if (ProcessRawProgramXml(xmlPath, imagesPath, outputPath))
                    {
                        specialXmlFiles.Add(outputPath);
                    }
                    else
                    {
                        Log($"Failed to process {Path.GetFileName(xmlPath)}, using original");
                        normalXmlFiles.Add(xmlPath);
                    }
                }
                else
                {
                    normalXmlFiles.Add(xmlPath);
                }
            }

            return (specialXmlFiles, normalXmlFiles);
        }

        public static bool NeedsOplusRwMode(IEnumerable<string> xmlFiles)
        {
            return xmlFiles.Any(f => IsSpecialRawProgram(f));
        }

        public static (List<string> special, List<string> normal) SeparateXmlFiles(IEnumerable<string> xmlFiles)
        {
            var special = new List<string>();
            var normal = new List<string>();

            foreach (var xmlPath in xmlFiles)
            {
                if (IsSpecialRawProgram(xmlPath))
                    special.Add(xmlPath);
                else
                    normal.Add(xmlPath);
            }

            return (special, normal);
        }
    }
}
