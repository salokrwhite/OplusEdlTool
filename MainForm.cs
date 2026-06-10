using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OplusEdlTool
{
    public partial class MainForm : Form
    {
        private readonly Services.EdlService edl;
        private List<Services.PartitionEntry>? currentParts;
        public MainForm()
        {
            InitializeComponent();
            edl = new Services.EdlService();
        }
        private void AppendLog(string s)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AppendLog(s)));
                return;
            }
            txtLog.AppendText(s + Environment.NewLine);
        }
        private static string FormatBytes(ulong bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;
            if (bytes >= (ulong)GB) return Math.Round(bytes / GB, 2).ToString() + " GB";
            if (bytes >= (ulong)MB) return Math.Round(bytes / MB, 2).ToString() + " MB";
            return Math.Round(bytes / KB, 2).ToString() + " KB";
        }
        private void btnBrowseDevPrg_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Programmer|*.*";
            if (dlg.ShowDialog() == DialogResult.OK) txtDevPrg.Text = dlg.FileName;
        }
        private void btnBrowseDigest_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Digest|*.*";
            if (dlg.ShowDialog() == DialogResult.OK) txtDigest.Text = dlg.FileName;
        }
        private void btnBrowseSig_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Sig|*.*";
            if (dlg.ShowDialog() == DialogResult.OK) txtSig.Text = dlg.FileName;
        }
        private async void btnRunFirehose_Click(object sender, EventArgs e)
        {
            AppendLog("waiting for edl port (9008)...");
            var port = await edl.WaitForEdlPortAsync();
            AppendLog("device connected: " + port);
            AppendLog("send device programmer...");
            var ok = await edl.SendProgrammerAsync(port, txtDevPrg.Text);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("send digest...");
            ok = await edl.SendDigestsAsync(port, txtDigest.Text);
            AppendLog("send verify command...");
            ok = await edl.SendVerifyAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("send sig...");
            ok = await edl.SendDigestsAsync(port, txtSig.Text);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("send sha256init command...");
            ok = await edl.SendSha256InitAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("configure...");
            ok = await edl.ConfigureAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("all done");
        }
        private void btnAddXmlRead_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "XML|*.xml";
            dlg.Multiselect = true;
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                foreach (var f in dlg.FileNames) lstXmlRead.Items.Add(f);
            }
        }
        private async void btnRunRead_Click(object sender, EventArgs e)
        {
            AppendLog("waiting for edl port (9008)...");
            var port = await edl.WaitForEdlPortAsync();
            AppendLog("device connected: " + port);
            AppendLog("configure...");
            var ok = await edl.ConfigureAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("test rw mode...");
            var mode = await edl.TestRwModeAsync(port);
            AppendLog("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            AppendLog("readback...");
            var xmls = lstXmlRead.Items.Cast<string>().ToList();
            var outDir = await edl.ReadByXmlAsync(port, xmls, mode.Item1);
            if (outDir == null) { AppendLog("failed"); return; }
            AppendLog("output: " + outDir);
            AppendLog("all done");
        }
        private void btnAddXmlWrite_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "XML|*.xml";
            dlg.Multiselect = true;
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                foreach (var f in dlg.FileNames) lstXmlWrite.Items.Add(f);
            }
        }
        private async void btnRunWrite_Click(object sender, EventArgs e)
        {
            AppendLog("waiting for edl port (9008)...");
            var port = await edl.WaitForEdlPortAsync();
            AppendLog("device connected: " + port);
            AppendLog("configure...");
            var ok = await edl.ConfigureAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("test rw mode...");
            var mode = await edl.TestRwModeAsync(port);
            AppendLog("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            AppendLog("write...");
            var xmls = lstXmlWrite.Items.Cast<string>().ToList();
            var searchPath = xmls.Count > 0 ? Path.GetDirectoryName(xmls[0]) ?? "" : "";
            ok = await edl.WriteByXmlAsync(port, xmls, mode.Item1, searchPath);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("all done");
        }
        private async void btnCleanup_Click(object sender, EventArgs e)
        {
            var ok = await edl.CleanupAsync();
            AppendLog(ok ? "cleaned" : "cleanup failed");
        }
        private async void btnReadPartitions_Click(object sender, EventArgs e)
        {
            AppendLog("waiting for edl port (9008)...");
            var port = await edl.WaitForEdlPortAsync();
            AppendLog("device connected: " + port);
            AppendLog("configure...");
            var ok = await edl.ConfigureAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("test rw mode...");
            var mode = await edl.TestRwModeAsync(port);
            AppendLog("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            AppendLog("read partition table...");
            var parts = await edl.ReadPartitionTableAsync(port, mode.Item1);
            if (parts == null) { AppendLog("failed"); return; }
            currentParts = parts;
            lvPartitions.Items.Clear();
            foreach (var p in parts)
            {
                var item = new ListViewItem(new[] { p.Name, p.Lun.ToString(), p.FirstLBA.ToString(), p.LastLBA.ToString(), FormatBytes(p.SizeBytes), p.TypeGuid });
                lvPartitions.Items.Add(item);
            }
            AppendLog("all done");
        }
        private async void btnBackupSelected_Click(object sender, EventArgs e)
        {
            if (lvPartitions.SelectedItems.Count == 0 || currentParts == null || currentParts.Count == 0) return;
            var item = lvPartitions.SelectedItems[0];
            var name = item.SubItems[0].Text;
            var part = currentParts.FirstOrDefault(x => x.Name == name);
            if (part == null) return;
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() != DialogResult.OK) return;
            var dest = Path.Combine(dlg.SelectedPath, (string.IsNullOrWhiteSpace(part.Name) ? "partition" : part.Name).Replace('\\', '_').Replace('/', '_') + ".bin");
            AppendLog("waiting for edl port (9008)...");
            var port = await edl.WaitForEdlPortAsync();
            AppendLog("device connected: " + port);
            AppendLog("configure...");
            var ok = await edl.ConfigureAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("test rw mode...");
            var mode = await edl.TestRwModeAsync(port);
            AppendLog("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            AppendLog("backup partition: " + part.Name);
            var outPath = await edl.BackupPartitionAsync(port, mode.Item1, part);
            if (outPath == null) { AppendLog("failed"); return; }
            try { File.Copy(outPath, dest, true); } catch { AppendLog("copy failed"); return; }
            AppendLog("saved: " + dest);
        }
        private async void btnWriteSelected_Click(object sender, EventArgs e)
        {
            if (lvPartitions.SelectedItems.Count == 0 || currentParts == null || currentParts.Count == 0) return;
            var item = lvPartitions.SelectedItems[0];
            var name = item.SubItems[0].Text;
            var part = currentParts.FirstOrDefault(x => x.Name == name);
            if (part == null) return;
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Binary|*.bin;*.img|All|*.*";
            if (dlg.ShowDialog() != DialogResult.OK) return;
            var file = dlg.FileName;
            var len = new FileInfo(file).Length;
            if (len == 0) { AppendLog("file empty"); return; }
            var sectorSize = edl.StorageType == "emmc" ? 512 : 4096;
            var maxBytes = part.SizeBytes;
            if ((ulong)len > maxBytes) { AppendLog("file too big"); return; }
            AppendLog("waiting for edl port (9008)...");
            var port = await edl.WaitForEdlPortAsync();
            AppendLog("device connected: " + port);
            AppendLog("configure...");
            var ok = await edl.ConfigureAsync(port);
            if (!ok) { AppendLog("failed"); return; }
            AppendLog("test rw mode...");
            var mode = await edl.TestRwModeAsync(port);
            AppendLog("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            AppendLog("write partition: " + part.Name);
            ok = await edl.WritePartitionAsync(port, mode.Item1, part, file);
            AppendLog(ok ? "all done" : "failed");
        }
    }
}
