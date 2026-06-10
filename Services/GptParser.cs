using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OplusEdlTool.Services
{
    public class GptPartitionInfo
    {
        public string Label { get; set; } = string.Empty;
        public ulong StartSector { get; set; }
        public ulong NumPartitionSectors { get; set; }
        public string StartByteHex { get; set; } = string.Empty;
        public double SizeInKB { get; set; }
        public int SectorSize { get; set; }
    }

    public class GptParser
    {
        private readonly Action<string>? _onLog;

        public GptParser(Action<string>? onLog = null)
        {
            _onLog = onLog;
        }

        private void Log(string message)
        {
            _onLog?.Invoke(message);
        }

        public (List<GptPartitionInfo> partitions, int sectorSize) ParseGpt(string gptFilePath, int defaultSectorSize = 4096)
        {
            var partitions = new List<GptPartitionInfo>();
            int sectorSize = defaultSectorSize;

            try
            {
                var data = File.ReadAllBytes(gptFilePath);

                int headerOffset = sectorSize;

                if (data.Length < headerOffset + 92)
                {
                    Log($"GPT file too small: {data.Length} bytes");
                    return (partitions, sectorSize);
                }

                string signature = Encoding.ASCII.GetString(data, headerOffset, 8);
                if (signature != "EFI PART")
                {
                    Log("GPT signature not found at default offset, trying offset 512...");
                    headerOffset = 512;
                    if (data.Length < headerOffset + 92)
                    {
                        Log("GPT file too small for eMMC sector size");
                        return (partitions, sectorSize);
                    }
                    signature = Encoding.ASCII.GetString(data, headerOffset, 8);
                    if (signature != "EFI PART")
                    {
                        Log($"Invalid GPT signature: {signature}");
                        return (partitions, sectorSize);
                    }
                    sectorSize = 512;
                }

                ulong partitionEntryLba = BitConverter.ToUInt64(data, headerOffset + 72);
                uint numEntries = BitConverter.ToUInt32(data, headerOffset + 80);
                uint entrySize = BitConverter.ToUInt32(data, headerOffset + 84);

                int entryOffset = (int)(partitionEntryLba * (ulong)sectorSize);

                for (int i = 0; i < numEntries; i++)
                {
                    int offset = entryOffset + i * (int)entrySize;
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

                    ulong startLba = BitConverter.ToUInt64(data, offset + 32);
                    ulong endLba = BitConverter.ToUInt64(data, offset + 40);
                    
                    byte[] nameBytes = new byte[72];
                    Array.Copy(data, offset + 56, nameBytes, 0, 72);
                    string name = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');

                    ulong numSectors = endLba - startLba + 1;
                    ulong startByteHex = startLba * (ulong)sectorSize;
                    double sizeKb = numSectors * (ulong)sectorSize / 1024.0;

                    partitions.Add(new GptPartitionInfo
                    {
                        Label = name,
                        StartSector = startLba,
                        NumPartitionSectors = numSectors,
                        StartByteHex = $"0x{startByteHex:x}",
                        SizeInKB = sizeKb,
                        SectorSize = sectorSize
                    });
                }

            }
            catch (Exception ex)
            {
                Log($"Error parsing GPT: {ex.Message}");
            }

            return (partitions, sectorSize);
        }

        public GptPartitionInfo? FindPartition(List<GptPartitionInfo> partitions, string label)
        {
            foreach (var p in partitions)
            {
                if (p.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }
    }
}
