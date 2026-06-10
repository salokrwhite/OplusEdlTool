using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OplusEdlTool.Services;

namespace OplusEdlTool.Pages
{
    public class PartitionRow
    {
        public bool IsSelected { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Lun { get; set; }
        public ulong FirstLBA { get; set; }
        public ulong LastLBA { get; set; }
        public string SizeFormatted { get; set; } = string.Empty;
        public ulong SizeBytes { get; set; }
        public string TypeGuid { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public PartitionEntry ToEntry() => new PartitionEntry { Name = Name, Lun = Lun, FirstLBA = FirstLBA, LastLBA = LastLBA, SizeBytes = SizeBytes, TypeGuid = TypeGuid };
    }
    public partial class PartitionsPage : UserControl
    {
        private readonly EdlService edl;
        private readonly System.Action<string> log;
        private List<PartitionRow> rows = new();
        public PartitionsPage(EdlService edl, System.Action<string> logger)
        { InitializeComponent(); this.edl = edl; this.log = logger; }
        private static string Fmt(ulong bytes)
        {
            const double KB = 1024.0; const double MB = KB * 1024.0; const double GB = MB * 1024.0;
            if (bytes >= (ulong)GB) return System.Math.Round(bytes / GB, 2) + " GB";
            if (bytes >= (ulong)MB) return System.Math.Round(bytes / MB, 2) + " MB";
            return System.Math.Round(bytes / KB, 2) + " KB";
        }
        private async void Read_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            log("waiting for edl port (9008)..."); var port = await edl.WaitForEdlPortAsync();
            log("device connected: " + port);
            log("configure..."); var ok = await edl.ConfigureAsync(port); if (!ok) { log("failed"); return; }
            log("test rw mode..."); var mode = await edl.TestRwModeAsync(port); log("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            log("read partition table..."); var parts = await edl.ReadPartitionTableAsync(port, mode.Item1);
            if (parts == null || parts.Count == 0) { log("no partitions"); return; }
            rows = parts.Select(p => new PartitionRow { Name = p.Name, Lun = p.Lun, FirstLBA = p.FirstLBA, LastLBA = p.LastLBA, SizeBytes = p.SizeBytes, SizeFormatted = Fmt(p.SizeBytes), TypeGuid = p.TypeGuid }).ToList();
            Grid.ItemsSource = rows;
            log("all done");
        }
        private void SelectAll_Click(object sender, System.Windows.RoutedEventArgs e)
        { foreach (var r in rows) r.IsSelected = true; Grid.Items.Refresh(); }
        private async void ReadSelected_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var selected = rows.Where(r => r.IsSelected).ToList(); if (selected.Count == 0) return;
            var sfd = new SaveFileDialog { FileName = selected.Count == 1 ? (selected[0].Name + ".bin") : "selected_partitions.bin" };
            if (sfd.ShowDialog() != true) return;
            var destBase = System.IO.Path.GetDirectoryName(sfd.FileName) ?? "";

            var superPartitions = selected.Where(r => EdlService.IsSuperPartition(r.Name)).ToList();
            var otherPartitions = selected.Where(r => !EdlService.IsSuperPartition(r.Name))
                                          .OrderBy(r => r.SizeBytes)
                                          .ToList();

            log("waiting for edl port (9008)..."); var port = await edl.WaitForEdlPortAsync();
            log("device connected: " + port);
            log("configure..."); var ok = await edl.ConfigureAsync(port); if (!ok) { log("failed"); return; }
            log("test rw mode..."); var mode = await edl.TestRwModeAsync(port); log("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));

            if (otherPartitions.Count > 0)
            {
                log($"backing up {otherPartitions.Count} partition(s) first...");
                foreach (var r in otherPartitions)
                {
                    log($"reading partition: {r.Name} ({Fmt(r.SizeBytes)})...");
                    var outPath = await edl.BackupPartitionAsync(port, mode.Item1, r.ToEntry());
                    if (outPath == null) { log("failed: " + r.Name); continue; }
                    var dest = System.IO.Path.Combine(destBase, r.Name + ".bin");
                    try { System.IO.File.Copy(outPath, dest, true); log("saved: " + dest); } catch { log("copy failed: " + r.Name); }
                }
            }

            if (superPartitions.Count > 0)
            {
                log("now backing up super partition (this may take a while)...");
                foreach (var r in superPartitions)
                {
                    log($"reading super partition ({Fmt(r.SizeBytes)})...");
                    var outPath = await edl.BackupSuperPartitionAsync(port, mode.Item1, destBase);
                    if (outPath == null) { log("failed: " + r.Name); continue; }
                    var dest = System.IO.Path.Combine(destBase, "super.img");
                    if (outPath != dest)
                    {
                        try 
                        { 
                            if (System.IO.File.Exists(dest)) System.IO.File.Delete(dest);
                            System.IO.File.Move(outPath, dest); 
                            log("saved: " + dest); 
                        }
                        catch { log("rename failed, file at: " + outPath); }
                    }
                    else
                    {
                        log("saved: " + dest);
                    }
                }
            }

            log("all done");
        }
        private void Grid_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dataGridCell = e.OriginalSource as System.Windows.Controls.DataGridCell;
            if (dataGridCell != null && dataGridCell.Column.Header.ToString() == "File Path")
            {
                var row = (PartitionRow)dataGridCell.DataContext;
                var ofd = new OpenFileDialog { Filter = "Binary|*.bin;*.img|All|*.*", FileName = row.FilePath };
                if (ofd.ShowDialog() == true)
                {
                    row.FilePath = ofd.FileName;
                    Grid.Items.Refresh();
                }
                e.Handled = true;
            }
        }

        private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column.Header.ToString() == "File Path")
            {
                e.Cancel = true;
                var row = (PartitionRow)e.Row.Item;
                var ofd = new OpenFileDialog { Filter = "Binary|*.bin;*.img|All|*.*", FileName = row.FilePath };
                if (ofd.ShowDialog() == true)
                {
                    row.FilePath = ofd.FileName;
                    Grid.Items.Refresh();
                }
            }
        }
        private async void WriteSelected_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var selected = rows.Where(r => r.IsSelected && !string.IsNullOrEmpty(r.FilePath)).ToList(); 
            if (selected.Count == 0) { log("no partition selected with valid file path"); return; }
            log("waiting for edl port (9008)..."); var port = await edl.WaitForEdlPortAsync();
            log("device connected: " + port);
            log("configure..."); var ok = await edl.ConfigureAsync(port); if (!ok) { log("failed"); return; }
            log("test rw mode..."); var mode = await edl.TestRwModeAsync(port); log("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            foreach (var r in selected)
            {
                ok = await edl.WritePartitionAsync(port, mode.Item1, r.ToEntry(), r.FilePath);
                log(ok ? ("written: " + r.Name) : ("failed: " + r.Name));
            }
            log("all done");
        }
        private async void EraseSelected_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var selected = rows.Where(r => r.IsSelected).ToList(); if (selected.Count == 0) return;
            log("waiting for edl port (9008)..."); var port = await edl.WaitForEdlPortAsync();
            log("device connected: " + port);
            log("configure..."); var ok = await edl.ConfigureAsync(port); if (!ok) { log("failed"); return; }
            log("test rw mode..."); var mode = await edl.TestRwModeAsync(port); log("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            foreach (var r in selected)
            {
                ok = await edl.ErasePartitionAsync(port, mode.Item1, r.ToEntry());
                log(ok ? ("erased: " + r.Name) : ("failed: " + r.Name));
            }
            log("all done");
        }
    }
}
