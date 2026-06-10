using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace OplusEdlTool.Services
{
    public class OfpDecryptor
    {
        private Action<string>? _log;

        private static readonly (string version, string mc, string userkey, string ivec)[] KeySets = new[]
        {
            ("V1.4.17/1.4.27", "27827963787265EF89D126B69A495A21", "82C50203285A2CE7D8C3E198383CE94C", "422DD5399181E223813CD8ECDF2E4D72"),
            ("V1.6.17", "E11AA7BB558A436A8375FD15DDD4651F", "77DDF6A0696841F6B74782C097835169", "A739742384A44E8BA45207AD5C3700EA"),
            ("V1.5.13", "67657963787565E837D226B69A495D21", "F6C50203515A2CE7D8C3E1F938B7E94C", "42F2D5399137E2B2813CD8ECDF2F4D72"),
            ("V1.6.6/1.6.9/1.6.17/1.6.24/1.6.26/1.7.6", "3C2D518D9BF2E4279DC758CD535147C3", "87C74A29709AC1BF2382276C4E8DF232", "598D92E967265E9BCABE2469FE4A915E"),
            ("V1.7.2", "8FB8FB261930260BE945B841AEFA9FD4", "E529E82B28F5A2F8831D860AE39E425D", "8A09DA60ED36F125D64709973372C1CF"),
            ("V2.0.3", "E8AE288C0192C54BF10C5707E9C4705B", "D64FC385DCD52A3C9B5FBA8650F92EDA", "79051FD8D8B6297E2E4559E997F63B7F"),
            ("MTK-1", "9E4F32639D21357D37D226B69A495D21", "A3D8D358E42F5A9E931DD3917D9A3218", "386935399137416B67416BECF22F519A"),
            ("MTK-2", "892D57E92A4D8A975E3C216B7C9DE189", "D26DF2D9913785B145D18C7219B89F26", "516989E4A1BFC78B365C6BC57D944391"),
            ("MTK-3", "3C4A618D9BF2E4279DC758CD535147C3", "87B13D29709AC1BF2382276C4E8DF232", "59B7A8E967265E9BCABE2469FE4A915E"),
            ("MTK-4", "1C3288822BF824259DC852C1733127D3", "E7918D22799181CF2312176C9E2DF298", "3247F889A7B6DECBCA3E28693E4AAAFE"),
            ("MTK-5", "1E4F32239D65A57D37D2266D9A775D43", "A332D3C3E42F5A3E931DD991729A321D", "3F2A35399A373377674155ECF28FD19A"),
            ("MTK-6", "122D57E92A518AFF5E3C786B7C34E189", "DD6DF2D9543785674522717219989FB0", "12698965A132C76136CC88C5DD94EE91"),
            ("V2.1.x", "D4D2CD61D4D2CD61D4D2CD61D4D2CD61", "D4D2CD61D4D2CD61D4D2CD61D4D2CD61", "D4D2CD61D4D2CD61D4D2CD61D4D2CD61"),
            ("V3.0.x", "2442CE821A4F352D44D2CE8D1A4F352D", "2442CE821A4F352D44D2CE8D1A4F352D", "2442CE821A4F352D44D2CE8D1A4F352D"),
        };

        public OfpDecryptor(Action<string>? log = null) { _log = log; }
        private void Log(string msg) => _log?.Invoke(msg);

        private static byte Swap(byte ch) => (byte)(((ch & 0xF) << 4) + ((ch & 0xF0) >> 4));
        
        private static byte[] Deobfuscate(byte[] data, byte[] mask)
        {
            byte[] ret = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                byte xored = (byte)(data[i] ^ mask[i]);
                ret[i] = Swap(xored);
            }
            return ret;
        }

        private static (byte[] key, byte[] iv) GenerateKey(byte[] mc, byte[] userkey, byte[] ivec)
        {
            byte[] deobfUserkey = Deobfuscate(userkey, mc);
            byte[] deobfIvec = Deobfuscate(ivec, mc);
            using var md5 = MD5.Create();
            string keyHex = BitConverter.ToString(md5.ComputeHash(deobfUserkey)).Replace("-", "").ToLower().Substring(0, 16);
            string ivHex = BitConverter.ToString(md5.ComputeHash(deobfIvec)).Replace("-", "").ToLower().Substring(0, 16);
            return (Encoding.UTF8.GetBytes(keyHex), Encoding.UTF8.GetBytes(ivHex));
        }

        private static byte[] AesCfbDecrypt(byte[] data, byte[] key, byte[] iv)
        {
            int paddedLength = data.Length;
            if (paddedLength % 16 != 0)
                paddedLength = ((paddedLength / 16) + 1) * 16;
            
            byte[] paddedData = new byte[paddedLength];
            Array.Copy(data, paddedData, data.Length);
            
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CFB;
            aes.Padding = PaddingMode.None;
            aes.FeedbackSize = 128;
            using var decryptor = aes.CreateDecryptor();
            byte[] result = decryptor.TransformFinalBlock(paddedData, 0, paddedData.Length);
            
            if (result.Length > data.Length)
            {
                byte[] trimmed = new byte[data.Length];
                Array.Copy(result, trimmed, data.Length);
                return trimmed;
            }
            return result;
        }

        private (int pagesize, byte[]? key, byte[]? iv, string? xml) ExtractXml(string filename)
        {
            long filesize = new FileInfo(filename).Length;
            using var rf = new FileStream(filename, FileMode.Open, FileAccess.Read);
            
            int pagesize = 0;
            int[] pagesizes = { 0x200, 0x1000 };
            int[] offsets = { 0x10, 0x14, 0x0 }; 
            
            foreach (int x in pagesizes)
            {
                foreach (int off in offsets)
                {
                    if (filesize < x) continue;
                    rf.Seek(filesize - x + off, SeekOrigin.Begin);
                    byte[] magicBytes = new byte[4];
                    rf.ReadExactly(magicBytes, 0, 4);
                    uint magic = BitConverter.ToUInt32(magicBytes, 0);
                    Log($"Checking pagesize 0x{x:X} offset 0x{off:X}: magic=0x{magic:X}");
                    if (magic == 0x7CEF)
                    {
                        pagesize = x;
                        Log($"Found pagesize: 0x{pagesize:X}");
                        break;
                    }
                }
                if (pagesize != 0) break;
            }
            
            if (pagesize == 0)
            {
                Log("Scanning file tail for magic 0x7CEF...");
                int scanSize = Math.Min(0x2000, (int)filesize);
                rf.Seek(filesize - scanSize, SeekOrigin.Begin);
                byte[] tailData = new byte[scanSize];
                rf.ReadExactly(tailData, 0, scanSize);
                
                for (int i = 0; i < scanSize - 4; i++)
                {
                    uint magic = BitConverter.ToUInt32(tailData, i);
                    if (magic == 0x7CEF)
                    {
                        int posFromEnd = scanSize - i;
                        Log($"Found magic at offset -{posFromEnd} (0x{posFromEnd:X}) from end");
                        if (posFromEnd <= 0x200) pagesize = 0x200;
                        else if (posFromEnd <= 0x1000) pagesize = 0x1000;
                        else pagesize = ((posFromEnd / 0x200) + 1) * 0x200;
                        Log($"Estimated pagesize: 0x{pagesize:X}");
                        break;
                    }
                }
            }
            
            if (pagesize == 0) 
            {
                Log("Unknown pagesize - magic 0x7CEF not found");
                return (0, null, null, null);
            }

            long xmloffset = filesize - pagesize;
            rf.Seek(xmloffset + 0x14, SeekOrigin.Begin);
            byte[] offsetBytes = new byte[4], lengthBytes = new byte[4];
            rf.ReadExactly(offsetBytes, 0, 4);
            rf.ReadExactly(lengthBytes, 0, 4);
            long offset = BitConverter.ToUInt32(offsetBytes, 0) * pagesize;
            int length = (int)BitConverter.ToUInt32(lengthBytes, 0);
            Log($"XML offset: 0x{offset:X}, length: {length}");
            if (length < 200) length = (int)(xmloffset - offset - 0x57);

            rf.Seek(offset, SeekOrigin.Begin);
            byte[] data = new byte[length];
            rf.ReadExactly(data, 0, length);
            
            Log($"Encrypted data first 16 bytes: {BitConverter.ToString(data, 0, Math.Min(16, data.Length))}");

            foreach (var keyset in KeySets)
            {
                byte[] mc = Convert.FromHexString(keyset.mc);
                byte[] userkey = Convert.FromHexString(keyset.userkey);
                byte[] ivec = Convert.FromHexString(keyset.ivec);
                var (key, iv) = GenerateKey(mc, userkey, ivec);
                
                Log($"Trying {keyset.version}: key={Encoding.UTF8.GetString(key)}, iv={Encoding.UTF8.GetString(iv)}");
                
                try
                {
                    byte[] dec = AesCfbDecrypt(data, key, iv);
                    string xml = Encoding.UTF8.GetString(dec);
                    if (dec.Length > 0)
                        Log($"  Decrypted preview: {xml.Substring(0, Math.Min(30, xml.Length))}");
                    
                    if (xml.Contains("<?xml"))
                    {
                        Log($"Found key: {keyset.version}");
                        int lastGt = xml.LastIndexOf('>');
                        if (lastGt > 0)
                            return (pagesize, key, iv, xml.Substring(0, lastGt + 1));
                        return (pagesize, key, iv, xml);
                    }
                }
                catch (Exception ex)
                {
                    Log($"  Decrypt error: {ex.Message}");
                }
            }
            
            Log("Trying generatekey1 method...");
            try
            {
                var (key1, iv1) = GenerateKey1();
                byte[] dec = AesCfbDecrypt(data, key1, iv1);
                string xml = Encoding.UTF8.GetString(dec);
                if (xml.Contains("<?xml"))
                {
                    Log("Found key using generatekey1");
                    int lastGt = xml.LastIndexOf('>');
                    if (lastGt > 0)
                        return (pagesize, key1, iv1, xml.Substring(0, lastGt + 1));
                    return (pagesize, key1, iv1, xml);
                }
            }
            catch { }
            
            Log("No matching key found for this OFP file");
            return (0, null, null, null);
        }
        
        private static (byte[] key, byte[] iv) GenerateKey1()
        {
            byte[] key1 = Convert.FromHexString("42F2D5399137E2B2813CD8ECDF2F4D72");
            byte[] key2 = Convert.FromHexString("F6C50203515A2CE7D8C3E1F938B7E94C");
            byte[] key3 = Convert.FromHexString("67657963787565E837D226B69A495D21");
            
            byte[] shuffledKey2 = KeyShuffle(key2, key3);
            byte[] shuffledKey1 = KeyShuffle(key1, key3);
            
            using var md5 = MD5.Create();
            string keyHex = BitConverter.ToString(md5.ComputeHash(shuffledKey2)).Replace("-", "").ToLower().Substring(0, 16);
            string ivHex = BitConverter.ToString(md5.ComputeHash(shuffledKey1)).Replace("-", "").ToLower().Substring(0, 16);
            return (Encoding.UTF8.GetBytes(keyHex), Encoding.UTF8.GetBytes(ivHex));
        }
        
        private static byte[] KeyShuffle(byte[] key, byte[] hkey)
        {
            byte[] result = (byte[])key.Clone();
            for (int i = 0; i < 0x10; i += 4)
            {
                result[i] = Swap((byte)(hkey[i] ^ result[i]));
                result[i + 1] = Swap((byte)(hkey[i + 1] ^ result[i + 1]));
                result[i + 2] = Swap((byte)(hkey[i + 2] ^ result[i + 2]));
                result[i + 3] = Swap((byte)(hkey[i + 3] ^ result[i + 3]));
            }
            return result;
        }

        public string? Decrypt(string ofpFilePath, string? outputDir = null)
        {
            if (!File.Exists(ofpFilePath)) { Log($"File not found: {ofpFilePath}"); return null; }

            using (var rf = new FileStream(ofpFilePath, FileMode.Open, FileAccess.Read))
            {
                byte[] header = new byte[2];
                rf.ReadExactly(header, 0, 2);
                if (header[0] == 'P' && header[1] == 'K')
                {
                    Log("ZIP file detected, extracting with password...");
                    return ExtractZip(ofpFilePath, outputDir);
                }
            }

            string basePath = Path.GetDirectoryName(ofpFilePath) ?? ".";
            string extractPath = outputDir ?? Path.Combine(basePath, "extract");
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);

            var (pagesize, key, iv, xml) = ExtractXml(ofpFilePath);
            if (pagesize == 0 || key == null || iv == null || xml == null)
            {
                Log("Unknown key. Aborting");
                return null;
            }

            File.WriteAllText(Path.Combine(extractPath, "ProFile.xml"), xml);
            Log("Saved ProFile.xml");

            var root = XElement.Parse(xml);
            foreach (var child in root.Elements())
            {
                foreach (var item in child.Elements())
                {
                    ProcessItem(item, child.Name.LocalName, pagesize, key, iv, ofpFilePath, extractPath);
                    foreach (var subitem in item.Elements())
                        ProcessItem(subitem, child.Name.LocalName, pagesize, key, iv, ofpFilePath, extractPath);
                }
            }

            Log($"Done. Extracted files to {extractPath}");
            return extractPath;
        }

        private void ProcessItem(XElement item, string parentTag, int pagesize, byte[] key, byte[] iv, string srcFile, string destPath)
        {
            string wfilename = item.Attribute("Path")?.Value ?? item.Attribute("filename")?.Value ?? "";
            if (string.IsNullOrEmpty(wfilename)) return;

            long start = -1;
            if (item.Attribute("FileOffsetInSrc") != null)
                start = long.Parse(item.Attribute("FileOffsetInSrc")!.Value) * pagesize;
            else if (item.Attribute("SizeInSectorInSrc") != null)
                start = long.Parse(item.Attribute("SizeInSectorInSrc")!.Value) * pagesize;
            if (start < 0) return;

            long rlength = long.Parse(item.Attribute("SizeInByteInSrc")?.Value ?? "0");
            long length = item.Attribute("SizeInSectorInSrc") != null 
                ? long.Parse(item.Attribute("SizeInSectorInSrc")!.Value) * pagesize 
                : rlength;

            string[] copyTags = { "DigestsToSign", "ChainedTableOfDigests", "Firmware" };
            if (Array.Exists(copyTags, t => t == parentTag))
                CopyFile(srcFile, destPath, wfilename, start, rlength);
            else
                DecryptFile(key, iv, srcFile, destPath, wfilename, start, rlength, parentTag == "Sahara" ? rlength : 0x40000);
        }

        private void CopyFile(string srcFile, string destPath, string wfilename, long start, long length)
        {
            Log($"Extracting {wfilename}");
            string destFile = Path.Combine(destPath, wfilename);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? destPath);
            using var rf = new FileStream(srcFile, FileMode.Open, FileAccess.Read);
            using var wf = new FileStream(destFile, FileMode.Create, FileAccess.Write);
            rf.Seek(start, SeekOrigin.Begin);
            byte[] buffer = new byte[0x100000];
            long remaining = length;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = rf.Read(buffer, 0, toRead);
                if (read == 0) break;
                wf.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private void DecryptFile(byte[] key, byte[] iv, string srcFile, string destPath, string wfilename, long start, long rlength, long decryptSize)
        {
            Log($"Decrypting {wfilename}");
            string destFile = Path.Combine(destPath, wfilename);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? destPath);

            using var rf = new FileStream(srcFile, FileMode.Open, FileAccess.Read);
            using var wf = new FileStream(destFile, FileMode.Create, FileAccess.Write);
            rf.Seek(start, SeekOrigin.Begin);

            long size = Math.Min(decryptSize, rlength);
            byte[] data = new byte[size + (4 - size % 4) % 4];
            rf.ReadExactly(data, 0, (int)size);
            byte[] outp = AesCfbDecrypt(data, key, iv);
            wf.Write(outp, 0, (int)size);

            if (rlength > decryptSize)
            {
                byte[] buffer = new byte[0x100000];
                long remaining = rlength - size;
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = rf.Read(buffer, 0, toRead);
                    if (read == 0) break;
                    wf.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
        }

        private string? ExtractZip(string zipPath, string? outputDir)
        {
            string basePath = Path.GetDirectoryName(zipPath) ?? ".";
            string extractPath = outputDir ?? Path.Combine(basePath, "extract");
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractPath);
                Log($"Extracted ZIP to {extractPath}");
                return extractPath;
            }
            catch
            {
                Log("ZIP extraction failed. Password-protected ZIPs require external library.");
                return null;
            }
        }
    }
}
