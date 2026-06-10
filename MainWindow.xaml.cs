using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using WinForms = System.Windows.Forms;
using OplusEdlTool.Services;

namespace OplusEdlTool
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
        public string NumSectors { get; set; } = string.Empty;
        public string SectorSize { get; set; } = "4096";
        public PartitionEntry ToEntry() => new PartitionEntry { Name = Name, Lun = Lun, FirstLBA = FirstLBA, LastLBA = LastLBA, SizeBytes = SizeBytes, TypeGuid = TypeGuid };
    }

    public class XmlFileItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsSelected 
        { 
            get => _isSelected; 
            set 
            { 
                _isSelected = value; 
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            } 
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        private readonly EdlService edl;
        private readonly RawProgramXmlProcessor _xmlProcessor;
        private List<PartitionRow> rows = new();
        private List<PartitionRow> filteredRows = new();
        private List<XmlFileItem> xmlFileItems = new();
        private string? currentPort;
        private string? currentRwMode;
        private string? romImagesPath;
        private string[]? rawProgramFiles;
        private CancellationTokenSource? _cts;
        private bool _isFromOfpExtract = false; 
        private System.Windows.Threading.DispatcherTimer? _portDetectionTimer;

        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OplusEdlTool");
        private static readonly string LogFilePath = Path.Combine(LogDir, "runtime.log");
        private StreamWriter? _logWriter;
        private readonly object _logLock = new object();
        private System.Windows.Threading.DispatcherTimer? _logRefreshTimer;
        private long _lastLogPosition = 0;

        public MainWindow()
        {
            InitializeComponent();
            InitializeLogFile();
            edl = new EdlService(AppendLog, UpdateProgress);
            _xmlProcessor = new RawProgramXmlProcessor(AppendLog);
            ApplyLanguage();
            RefreshPortStatus();
            InitializePortDetectionTimer();
        }
        
        private void InitializeLogFile()
        {
            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);
                
                if (File.Exists(LogFilePath))
                    File.Delete(LogFilePath);
                
                _logWriter = new StreamWriter(LogFilePath, false, System.Text.Encoding.UTF8)
                {
                    AutoFlush = true
                };
                
                _logRefreshTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _logRefreshTimer.Tick += LogRefreshTimer_Tick;
                _logRefreshTimer.Start();
                
                this.Closed += MainWindow_Closed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize log file: {ex.Message}");
            }
        }
        
        private void LogRefreshTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(LogFilePath)) return;
                
                using var fs = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length <= _lastLogPosition) return;
                
                fs.Seek(_lastLogPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
                var newContent = reader.ReadToEnd();
                _lastLogPosition = fs.Length;
                
                if (string.IsNullOrEmpty(newContent)) return;
                
                Log.AppendText(newContent);
                Log.ScrollToEnd();
            }
            catch
            {
                
            }
        }
        
        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _logRefreshTimer?.Stop();
                _logWriter?.Close();
                _logWriter?.Dispose();
                _logWriter = null;
                
                if (File.Exists(LogFilePath))
                    File.Delete(LogFilePath);
            }
            catch
            {
                
            }
        }
        
        private void InitializePortDetectionTimer()
        {
            _portDetectionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _portDetectionTimer.Tick += (s, e) => RefreshPortStatus();
            _portDetectionTimer.Start();
        }
        


        #region Language
        private void ApplyLanguage()
        {
            TitleText.Text = Lang.WindowTitle;
            BtnLanguage.Content = LanguageService.GetLanguageDisplayName();
            BtnAbout.Content = Lang.About;

            GrpFirehose.Header = Lang.EnterFirehoseMode;
            LblDevPrg.Text = Lang.DeviceProgrammer;
            LblDigest.Text = Lang.Digest;
            LblSig.Text = Lang.Sig;
            BtnEnterFirehose.Content = Lang.EnterFirehose;

            GrpFlashPackage.Header = Lang.FlashPackage;
            LblSelectRom.Text = Lang.SelectRomFolder;
            BtnLoad.Content = Lang.Load;
            BtnAll.Content = Lang.All;
            BtnNone.Content = Lang.None;

            GrpPartitions.Header = Lang.Partitions;
            LblSearch.Text = Lang.Search;
            SearchBox.ToolTip = Lang.SearchPartition;
            MenuCheckSelected.Header = Lang.CheckSelected;
            MenuUncheckSelected.Header = Lang.UncheckSelected;
            SelectAllCheckBox.ToolTip = Lang.SelectAll;
            BtnReadPartitions.Content = Lang.ReadPartitions;
            BtnReadSelected.Content = Lang.ReadSelected;
            BtnWriteSelected.Content = Lang.WriteSelected;
            BtnEraseSelected.Content = Lang.EraseSelected;
            BtnStopAll.Content = Lang.StopAll;
            AutoRebootCheckBox.Content = Lang.AutoReboot;
            AutoRebootCheckBox.ToolTip = Lang.AutoRebootTooltip;
            ExportXmlCheckBox.Content = Lang.ExportXml;
            ExportXmlCheckBox.ToolTip = Lang.ExportXmlTooltip;

            GrpLog.Header = Lang.Log;
            LblPort9008.Text = Lang.Port9008;
            BtnClear.Content = Lang.Clear;

            if (PortStatus.Text == "Not detected" || PortStatus.Text == "未检测到")
            {
                PortStatus.Text = Lang.NotDetected;
            }
        }

        private void SwitchLanguage_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                Lang.LanguageSwitchMessage,
                Lang.RestartRequired,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                bool success = LanguageService.ToggleLanguage();
                
                if (!success)
                {
                    AppendLog("Warning: Failed to save language settings");
                    System.Windows.MessageBox.Show(
                        "Failed to save language settings. Please check write permissions.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                AppendLog("Language settings saved, restarting...");

                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }
        #endregion

        #region Logging & Progress
        
        public void AppendLog(string s)
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logLine = $"{ts} {s}";
            
            lock (_logLock)
            {
                try
                {
                    _logWriter?.WriteLine(logLine);
                }
                catch
                {
                    
                }
            }
        }

        public void UpdateProgress(int percent)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateProgress(percent)); return; }
            if (percent < 0) percent = 0; if (percent > 100) percent = 100;
            Progress.Value = percent;
        }
        #endregion

        #region Port Detection
        private void RefreshPortStatus()
        {
            try
            {
                var port = Detect9008Port();
                if (!string.IsNullOrEmpty(port))
                {
                    PortStatus.Text = port;
                    PortStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)); 
                }
                else
                {
                    PortStatus.Text = Lang.NotDetected;
                    PortStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F)); 
                }
            }
            catch
            {
                PortStatus.Text = "Error";
                PortStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));
            }
        }

        private string? Detect9008Port()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%Qualcomm%9008%' OR Caption LIKE '%QDLoader%9008%'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var caption = obj["Caption"]?.ToString() ?? "";
                    var match = Regex.Match(caption, @"\(COM(\d+)\)");
                    if (match.Success)
                        return "COM" + match.Groups[1].Value;
                }
            }
            catch { }
            return null;
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            Log.Clear();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            try
            {
                edl.CleanupWorkDir();
            }
            catch { }
        }
        #endregion

        #region Search & Filter
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var searchText = SearchBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(searchText))
            {
                filteredRows = rows.ToList();
            }
            else
            {
                filteredRows = rows.Where(r => r.Name.ToLower().Contains(searchText)).ToList();
            }
            Grid.ItemsSource = null;
            Grid.ItemsSource = filteredRows;
        }
        #endregion

        #region Stop All
        private void StopAll_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
                AppendLog(Lang.StoppingTasks);
                AppendLog("All tasks have been stopped.");
            }
            else
            {
                AppendLog(Lang.NoTasksRunning);
            }

            try
            {
                foreach (var procName in new[] { "fh_loader", "QSaharaServer", "lsusb", "lpmake", "simg2img" })
                {
                    foreach (var proc in System.Diagnostics.Process.GetProcessesByName(procName))
                    {
                        try
                        {
                            proc.Kill();
                            AppendLog($"Killed process: {procName}");
                        }
                        catch { }
                    }
                }
            }
            catch { }
            
            BtnEnterFirehose.IsEnabled = true;
            BtnReadPartitions.IsEnabled = true;
            BtnReadSelected.IsEnabled = true;
            BtnWriteSelected.IsEnabled = true;
            BtnEraseSelected.IsEnabled = true;
            
            UpdateProgress(0);
            AppendLog("Ready for new tasks.");
        }
        #endregion

        #region File Pickers
        private void PickDevPrg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "All Files|*.*" };
            if (dlg.ShowDialog() == true) DevPrg.Text = dlg.FileName;
        }

        private void PickDigest_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "All Files|*.*" };
            if (dlg.ShowDialog() == true) Digest.Text = dlg.FileName;
        }

        private void PickSig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "All Files|*.*" };
            if (dlg.ShowDialog() == true) Sig.Text = dlg.FileName;
        }

        private void PickRomFolder_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                Lang.SelectFolderOrFile,
                Lang.SelectRomSource,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            if (result == MessageBoxResult.Yes)
            {
                SelectRomFolder();
            }
            else
            {
                SelectOfpOpsFile();
            }
        }

        private void SelectRomFolder()
        {
            var dlg = new WinForms.FolderBrowserDialog { ShowNewFolderButton = false };
            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;

            var selectedPath = dlg.SelectedPath;
            var imagesPath = FindImagesFolder(selectedPath);
            
            if (imagesPath == null)
            {
                AppendLog(Lang.ImagesFolderNotFound);
                System.Windows.MessageBox.Show(Lang.ImagesFolderNotFound, Lang.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isFromOfpExtract = false; 
            if (!ValidateAndLoadRawProgram(imagesPath, selectedPath)) return;
        }

        private async void SelectOfpOpsFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ROM Files|*.ofp;*.ops|OFP Files|*.ofp|OPS Files|*.ops|All Files|*.*",
                Title = "Select OFP or OPS ROM File"
            };

            if (dlg.ShowDialog() != true) return;

            var filePath = dlg.FileName;
            var ext = Path.GetExtension(filePath).ToLower();

            AppendLog($"Selected file: {Path.GetFileName(filePath)}");
            AppendLog("Starting extraction, please wait...");

            string? extractPath = null;
            try
            {
                if (ext == ".ops")
                {
                    var decryptor = new OpsDecryptor(AppendLog);
                    extractPath = await System.Threading.Tasks.Task.Run(() => decryptor.Decrypt(filePath));
                }
                else if (ext == ".ofp")
                {
                    var decryptor = new OfpDecryptor(AppendLog);
                    extractPath = await System.Threading.Tasks.Task.Run(() => decryptor.Decrypt(filePath));
                }
                else
                {
                    AppendLog("Unsupported file format");
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Extraction error: {ex.Message}");
                return;
            }

            if (string.IsNullOrEmpty(extractPath))
            {
                AppendLog("Extraction failed");
                return;
            }

            AppendLog($"Extraction completed: {extractPath}");

            await MergeSuperImages(extractPath);

            var imagesPath = FindImagesFolder(extractPath) ?? extractPath;
            _isFromOfpExtract = true; 
            if (!ValidateAndLoadRawProgram(imagesPath, extractPath)) return;
        }

        private async System.Threading.Tasks.Task MergeSuperImages(string extractPath)
        {
            try
            {
                var superFiles = Directory.GetFiles(extractPath, "super.*.img")
                    .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^super\.\d+\.[a-fA-F0-9]+\.img$"))
                    .OrderBy(f =>
                    {
                        var match = Regex.Match(Path.GetFileName(f), @"^super\.(\d+)\.");
                        return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
                    })
                    .ToList();

                if (superFiles.Count == 0)
                {
                    AppendLog("No super segment images found");
                    return;
                }

                AppendLog($"Found {superFiles.Count} super segment images");

                var renamedFiles = new List<string>();
                for (int i = 0; i < superFiles.Count; i++)
                {
                    var newName = Path.Combine(extractPath, $"super{i}.img");
                    if (File.Exists(newName) && newName != superFiles[i])
                        File.Delete(newName);
                    
                    if (superFiles[i] != newName)
                    {
                        File.Move(superFiles[i], newName);
                        AppendLog($"Renamed {Path.GetFileName(superFiles[i])} -> super{i}.img");
                    }
                    renamedFiles.Add(newName);
                }

                var simg2imgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "simg2img.exe");
                if (!File.Exists(simg2imgPath))
                {
                    AppendLog("simg2img.exe not found in Tools folder, skipping super merge");
                    return;
                }

                var superOutputPath = Path.Combine(extractPath, "super.img");
                if (File.Exists(superOutputPath))
                {
                    AppendLog("super.img already exists, skipping merge");
                    return;
                }

                var args = string.Join(" ", renamedFiles.Select(f => $"\"{f}\"")) + $" \"{superOutputPath}\"";
                AppendLog($"Merging super images with simg2img...");

                var result = await System.Threading.Tasks.Task.Run(() =>
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = simg2imgPath,
                        Arguments = args,
                        WorkingDirectory = extractPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(psi);
                    if (process == null) return (false, "Failed to start simg2img");
                    
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                        return (true, output);
                    else
                        return (false, error);
                });

                if (result.Item1)
                {
                    AppendLog("Super images merged successfully");
                    
                    foreach (var file in renamedFiles)
                    {
                        if (File.Exists(file))
                            File.Delete(file);
                    }
                }
                else
                {
                    AppendLog($"Failed to merge super images: {result.Item2}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error merging super images: {ex.Message}");
            }
        }

        private string? FindImagesFolder(string basePath)
        {
            var imagesPath = Path.Combine(basePath, "IMAGES");
            if (Directory.Exists(imagesPath)) return imagesPath;

            if (Path.GetFileName(basePath).Equals("IMAGES", StringComparison.OrdinalIgnoreCase))
                return basePath;

            if (Directory.GetFiles(basePath, "rawprogram*.xml").Length > 0)
                return basePath;

            return null;
        }

        private bool ValidateAndLoadRawProgram(string imagesPath, string displayPath)
        {
            var xmlFiles = new List<string>();
            for (int i = 0; i <= 5; i++)
            {
                var xmlFile = Path.Combine(imagesPath, $"rawprogram{i}.xml");
                if (File.Exists(xmlFile)) xmlFiles.Add(xmlFile);
            }

            if (xmlFiles.Count == 0)
            {
                xmlFiles = Directory.GetFiles(imagesPath, "rawprogram*.xml")
                    .OrderBy(f =>
                    {
                        var fileName = Path.GetFileNameWithoutExtension(f);
                        var match = Regex.Match(fileName, @"\d+");
                        return match.Success ? int.Parse(match.Value) : int.MaxValue;
                    })
                    .ToList();
            }

            if (xmlFiles.Count == 0)
            {
                AppendLog(Lang.NoRawprogramFound);
                System.Windows.MessageBox.Show(Lang.NoRawprogramFound, Lang.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            romImagesPath = imagesPath;
            rawProgramFiles = xmlFiles.ToArray();
            RomPath.Text = displayPath;

            xmlFileItems.Clear();
            foreach (var xmlFile in xmlFiles)
            {
                xmlFileItems.Add(new XmlFileItem
                {
                    Name = Path.GetFileName(xmlFile),
                    FullPath = xmlFile,
                    IsSelected = false
                });
            }
            XmlFileList.ItemsSource = null;
            XmlFileList.ItemsSource = xmlFileItems;
            
            rows.Clear();
            filteredRows.Clear();
            Grid.ItemsSource = null;
            AppendLog(string.Format(Lang.FoundXmlFiles, xmlFiles.Count));

            _ = CheckAndMergeSuperPartitionAsync(displayPath, imagesPath);

            return true;
        }

        private void SelectAllXml_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in xmlFileItems)
            {
                item.IsSelected = true;
            }
            XmlFileList.ItemsSource = null;
            XmlFileList.ItemsSource = xmlFileItems;
        }

        private void SelectNoneXml_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in xmlFileItems)
            {
                item.IsSelected = false;
            }
            XmlFileList.ItemsSource = null;
            XmlFileList.ItemsSource = xmlFileItems;
        }

        private void XmlFileList_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Any(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void XmlFileList_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            var xmlFiles = files.Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();

            if (xmlFiles.Count == 0)
            {
                AppendLog(Lang.NoXmlInDropped);
                return;
            }

            AppendLog(string.Format(Lang.DroppedXmlFiles, xmlFiles.Count));

            var firstXmlDir = Path.GetDirectoryName(xmlFiles[0]);
            if (string.IsNullOrEmpty(firstXmlDir))
            {
                AppendLog(Lang.CannotDetermineDir);
                return;
            }

            romImagesPath = firstXmlDir;
            RomPath.Text = firstXmlDir;

            _isFromOfpExtract = false;

            rawProgramFiles = xmlFiles.ToArray();

            xmlFileItems.Clear();
            foreach (var xmlFile in xmlFiles)
            {
                xmlFileItems.Add(new XmlFileItem
                {
                    Name = Path.GetFileName(xmlFile),
                    FullPath = xmlFile,
                    IsSelected = true 
                });
            }
            XmlFileList.ItemsSource = null;
            XmlFileList.ItemsSource = xmlFileItems;

            rows.Clear();
            filteredRows.Clear();
            Grid.ItemsSource = null;

            AppendLog(string.Format(Lang.XmlLoadedFrom, firstXmlDir));
            AppendLog(Lang.TipClickLoad);
        }

        private void LoadSelectedXml_Click(object sender, RoutedEventArgs e)
        {
            var selectedXmls = xmlFileItems.Where(x => x.IsSelected).ToList();
            
            if (selectedXmls.Count == 0)
            {
                AppendLog(Lang.NoXmlSelected);
                System.Windows.MessageBox.Show(Lang.PleaseSelectXml, Lang.NoSelection, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            rawProgramFiles = selectedXmls.Select(x => x.FullPath).ToArray();
            
            AppendLog(string.Format(Lang.LoadingXmlFiles, selectedXmls.Count, string.Join(", ", selectedXmls.Select(x => x.Name))));
            LoadPartitionsFromXml();
        }

        private async System.Threading.Tasks.Task CheckAndMergeSuperPartitionAsync(string romBaseDir, string imagesPath)
        {
            try
            {
                var superImgPath = Path.Combine(imagesPath, "super.img");
                if (File.Exists(superImgPath))
                {
                    AppendLog("super.img already exists");
                    return;
                }

                var superMergeService = new SuperMergeService(AppendLog, UpdateProgress);
                var jsonPath = superMergeService.FindSuperDefJson(romBaseDir);
                
                if (string.IsNullOrEmpty(jsonPath))
                {
                    return;
                }

                var config = superMergeService.ParseSuperDefJson(jsonPath);
                if (config == null)
                {
                    AppendLog("Failed to parse super_def.json");
                    return;
                }

                var partitionsWithPath = config.Partitions.Where(p => !string.IsNullOrEmpty(p.Path)).ToList();
                if (partitionsWithPath.Count == 0)
                {
                    AppendLog("No partitions to merge in super_def.json");
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    string.Format(Lang.MergeSuperMessage, partitionsWithPath.Count),
                    Lang.MergeSuperPartition,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    AppendLog("Super merge skipped by user");
                    return;
                }

                AppendLog("Starting Super partition merge...");
                
                var success = await superMergeService.MergeSuperAsync(imagesPath, config, romBaseDir);
                
                if (success)
                {
                    AppendLog("Super partition merge completed!");
                    if (rawProgramFiles != null && rawProgramFiles.Length > 0 && rows.Count > 0)
                    {
                        LoadPartitionsFromXml();
                    }
                    else
                    {
                        AppendLog("Please select XML files and click 'Load' to see the updated partition list.");
                    }
                }
                else
                {
                    AppendLog("Super partition merge failed");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error checking super partition: {ex.Message}");
            }
        }

        private void LoadPartitionsFromXml()
        {
            if (rawProgramFiles == null || rawProgramFiles.Length == 0) return;

            rows.Clear();
            int totalPartitions = 0;

            foreach (var file in rawProgramFiles)
            {
                try
                {
                    var xmlDoc = XDocument.Load(file);
                    var programs = xmlDoc.Descendants("program");

                    foreach (var program in programs)
                    {
                        var fileName = program.Attribute("filename")?.Value ?? "";
                        var label = program.Attribute("label")?.Value ?? "";
                        var sizeKB = program.Attribute("size_in_KB")?.Value ?? "0";
                        var startSector = program.Attribute("start_sector")?.Value ?? "0";
                        var numPartitionSectors = program.Attribute("num_partition_sectors")?.Value ?? "0";
                        var physicalPartitionNumber = program.Attribute("physical_partition_number")?.Value ?? "0";

                        double sizeInKB = double.TryParse(sizeKB, out var parsedSize) ? parsedSize : 0;
                        string sizeFormatted;
                        if (sizeInKB >= 1048576) 
                            sizeFormatted = $"{sizeInKB / 1048576:F2} GB";
                        else if (sizeInKB >= 1024) 
                            sizeFormatted = $"{sizeInKB / 1024:F2} MB";
                        else
                            sizeFormatted = $"{sizeInKB:F2} KB";

                        var filePath = string.IsNullOrEmpty(fileName) ? "" : Path.Combine(romImagesPath!, fileName);
                        var fileExists = !string.IsNullOrEmpty(filePath) && File.Exists(filePath);

                        string displayFileName = fileName;
                        if (!fileExists && !string.IsNullOrEmpty(fileName) && 
                            Regex.IsMatch(fileName, @"^super\.\d+\.[a-fA-F0-9]+\.img$"))
                        {
                            var superImgPath = Path.Combine(romImagesPath!, "super.img");
                            if (File.Exists(superImgPath))
                            {
                                filePath = superImgPath;
                                fileExists = true;
                                displayFileName = "super.img";
                            }
                        }

                        if (!string.IsNullOrEmpty(fileName))
                        {
                            ulong firstLba = ulong.TryParse(startSector, out var start) ? start : 0;
                            ulong numSectors = ulong.TryParse(numPartitionSectors, out var sectors) ? sectors : 0;
                            ulong lastLba = numSectors > 0 ? firstLba + numSectors - 1 : 0;
                            
                            ulong sizeBytes = (ulong)(sizeInKB * 1024);
                            string actualSizeFormatted = sizeFormatted;
                            if (sizeInKB == 0 && fileExists && File.Exists(filePath))
                            {
                                try
                                {
                                    var fileInfo = new FileInfo(filePath);
                                    sizeBytes = (ulong)fileInfo.Length;
                                    double fileSizeKB = sizeBytes / 1024.0;
                                    if (fileSizeKB >= 1048576) 
                                        actualSizeFormatted = $"{fileSizeKB / 1048576:F2} GB";
                                    else if (fileSizeKB >= 1024) 
                                        actualSizeFormatted = $"{fileSizeKB / 1024:F2} MB";
                                    else
                                        actualSizeFormatted = $"{fileSizeKB:F2} KB";
                                    
                                    if (numSectors == 0)
                                    {
                                        numSectors = sizeBytes / 4096;
                                        lastLba = numSectors > 0 ? firstLba + numSectors - 1 : 0;
                                    }
                                }
                                catch { }
                            }

                            rows.Add(new PartitionRow
                            {
                                IsSelected = fileExists, 
                                Name = label,
                                Lun = int.TryParse(physicalPartitionNumber, out var lun) ? lun : 0,
                                FirstLBA = firstLba,
                                LastLBA = lastLba,
                                SizeFormatted = actualSizeFormatted,
                                SizeBytes = sizeBytes,
                                FilePath = fileExists ? filePath : $"[NOT FOUND] {fileName}",
                                NumSectors = numSectors > 0 ? numSectors.ToString() : numPartitionSectors,
                                SectorSize = "4096"
                            });
                            totalPartitions++;
                        }
                    }

                    AppendLog($"Parsed: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    AppendLog($"Error parsing {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            filteredRows = rows.ToList();
            Grid.ItemsSource = null;
            Grid.ItemsSource = filteredRows;

            var existingFiles = rows.Count(r => !r.FilePath.StartsWith("[NOT FOUND]"));
            RomInfo.Text = $"Found {totalPartitions} partitions, {existingFiles} files available";
            AppendLog($"Loaded {totalPartitions} partitions from {rawProgramFiles.Length} XML files");
        }
        #endregion

        #region Enter Firehose
        private async void EnterFirehose_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DevPrg.Text))
            {
                AppendLog("Please select Device Programmer file");
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            
            BtnEnterFirehose.IsEnabled = false;
            try
            {
                AppendLog("Waiting for EDL port (9008)...");
                var port = await edl.WaitForEdlPortAsync(token);
                
                if (token.IsCancellationRequested)
                {
                    AppendLog("Enter Firehose cancelled.");
                    return;
                }
                
                AppendLog("Device connected: " + port);

                AppendLog("Sending device programmer...");
                var ok = await edl.SendProgrammerAsync(port, DevPrg.Text);
                if (token.IsCancellationRequested) { AppendLog("Enter Firehose cancelled."); return; }
                if (!ok) { AppendLog("Failed to send programmer"); return; }

                if (!string.IsNullOrWhiteSpace(Digest.Text))
                {
                    AppendLog("Sending digest...");
                    ok = await edl.SendDigestsAsync(port, Digest.Text);
                    if (token.IsCancellationRequested) { AppendLog("Enter Firehose cancelled."); return; }
                    if (!ok) { AppendLog("Failed to send digest"); return; }
                }

                AppendLog("Sending verify command...");
                ok = await edl.SendVerifyAsync(port);
                if (token.IsCancellationRequested) { AppendLog("Enter Firehose cancelled."); return; }
                if (!ok) { AppendLog("Failed to verify"); return; }

                if (!string.IsNullOrWhiteSpace(Sig.Text))
                {
                    AppendLog("Sending sig...");
                    ok = await edl.SendDigestsAsync(port, Sig.Text);
                    if (token.IsCancellationRequested) { AppendLog("Enter Firehose cancelled."); return; }
                    if (!ok) { AppendLog("Failed to send sig"); return; }
                }

                AppendLog("Sending sha256init command...");
                ok = await edl.SendSha256InitAsync(port);
                if (token.IsCancellationRequested) { AppendLog("Enter Firehose cancelled."); return; }
                if (!ok) { AppendLog("Failed sha256init"); return; }

                AppendLog("Configuring...");
                ok = await edl.ConfigureAsync(port);
                if (token.IsCancellationRequested) { AppendLog("Enter Firehose cancelled."); return; }
                if (!ok) { AppendLog("Failed to configure"); return; }

                currentPort = port;
                AppendLog("Firehose mode entered successfully!");
            }
            catch (OperationCanceledException)
            {
                AppendLog("Enter Firehose cancelled by user.");
            }
            catch (Exception ex)
            {
                AppendLog("Error: " + ex.Message);
            }
            finally
            {
                _cts = null;
                BtnEnterFirehose.IsEnabled = true;
            }
        }
        #endregion

        #region Partitions
        private static string FormatSize(ulong bytes)
        {
            const double KB = 1024.0; const double MB = KB * 1024.0; const double GB = MB * 1024.0;
            if (bytes >= (ulong)GB) return Math.Round(bytes / GB, 2) + " GB";
            if (bytes >= (ulong)MB) return Math.Round(bytes / MB, 2) + " MB";
            return Math.Round(bytes / KB, 2) + " KB";
        }

        private async void ReadPartitions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendLog("Waiting for EDL port (9008)...");
                var port = await edl.WaitForEdlPortAsync();
                AppendLog("Device connected: " + port);

                AppendLog("Configuring...");
                var ok = await edl.ConfigureAsync(port);
                if (!ok) { AppendLog("Failed to configure"); return; }

                AppendLog("Testing RW mode...");
                var mode = await edl.TestRwModeAsync(port);
                AppendLog("RW mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));

                currentPort = port;
                currentRwMode = mode.Item1;

                AppendLog("Reading partition table...");
                var parts = await edl.ReadPartitionTableAsync(port, mode.Item1);
                if (parts == null || parts.Count == 0) { AppendLog("No partitions found"); return; }

                rows = parts.Select(p => new PartitionRow
                {
                    Name = p.Name,
                    Lun = p.Lun,
                    FirstLBA = p.FirstLBA,
                    LastLBA = p.LastLBA,
                    SizeBytes = p.SizeBytes,
                    SizeFormatted = FormatSize(p.SizeBytes),
                    TypeGuid = p.TypeGuid
                }).ToList();
                Grid.ItemsSource = rows;
                AppendLog($"Found {parts.Count} partitions");
            }
            catch (Exception ex)
            {
                AppendLog("Error: " + ex.Message);
            }
        }

        private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = SelectAllCheckBox.IsChecked == true;
            foreach (var r in rows) r.IsSelected = isChecked;
            Grid.Items.Refresh();
        }

        private void CheckSelectedRows_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = Grid.SelectedItems.Cast<PartitionRow>().ToList();
            foreach (var row in selectedItems)
            {
                row.IsSelected = true;
            }
            Grid.Items.Refresh();
        }

        private void UncheckSelectedRows_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = Grid.SelectedItems.Cast<PartitionRow>().ToList();
            foreach (var row in selectedItems)
            {
                row.IsSelected = false;
            }
            Grid.Items.Refresh();
        }

        private void CheckAllRows_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in rows) r.IsSelected = true;
            SelectAllCheckBox.IsChecked = true;
            Grid.Items.Refresh();
        }

        private void UncheckAllRows_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in rows) r.IsSelected = false;
            SelectAllCheckBox.IsChecked = false;
            Grid.Items.Refresh();
        }

        private async void ReadSelected_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                Lang.SelectReadMethodMessage,
                Lang.SelectReadMethod,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            if (result == MessageBoxResult.Yes)
            {
                await ReadSelectedPartitionsAuto();
            }
            else
            {
                await ReadByCustomXml();
            }
        }

        private async System.Threading.Tasks.Task ReadSelectedPartitionsAuto()
        {
            var selected = rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0) { AppendLog(Lang.NoPartitionsSelected); return; }

            var sfd = new Microsoft.Win32.SaveFileDialog { FileName = selected.Count == 1 ? (selected[0].Name + ".img") : "selected_partitions.img" };
            if (sfd.ShowDialog() != true) return;
            var destBase = Path.GetDirectoryName(sfd.FileName) ?? "";

            var superPartitions = selected.Where(r => EdlService.IsSuperPartition(r.Name)).ToList();
            var otherPartitions = selected.Where(r => !EdlService.IsSuperPartition(r.Name))
                                          .OrderBy(r => r.SizeBytes) 
                                          .ToList();

            _cts = new CancellationTokenSource();
            try
            {
                AppendLog(Lang.WaitingForEdl);
                var port = await edl.WaitForEdlPortAsync();
                AppendLog(Lang.DeviceConnected + port);

                AppendLog(Lang.Configuring);
                var ok = await edl.ConfigureAsync(port);
                if (!ok) { AppendLog(Lang.ConfigureFailed); return; }

                AppendLog(Lang.TestingRwMode);
                var mode = await edl.TestRwModeAsync(port);
                AppendLog("RW mode: " + mode.Item1);

                if (otherPartitions.Count > 0)
                {
                    AppendLog($"Backing up {otherPartitions.Count} partition(s) first...");
                    foreach (var r in otherPartitions)
                    {
                        if (_cts.Token.IsCancellationRequested) { AppendLog("Task cancelled"); break; }
                        AppendLog($"Reading partition: {r.Name} ({FormatSize(r.SizeBytes)})...");
                        var outPath = await edl.BackupPartitionAsync(port, mode.Item1, r.ToEntry());
                        if (outPath == null) { AppendLog("Failed: " + r.Name); continue; }
                        var dest = Path.Combine(destBase, r.Name + ".img");
                        try { File.Copy(outPath, dest, true); AppendLog("Saved: " + dest); }
                        catch { AppendLog("Copy failed: " + r.Name); }
                    }
                }

                if (superPartitions.Count > 0 && !_cts.Token.IsCancellationRequested)
                {
                    AppendLog("Now backing up super partition (this may take a while)...");
                    foreach (var r in superPartitions)
                    {
                        if (_cts.Token.IsCancellationRequested) { AppendLog("Task cancelled"); break; }
                        AppendLog($"Reading super partition ({FormatSize(r.SizeBytes)})...");
                        var outPath = await edl.BackupSuperPartitionAsync(port, mode.Item1, destBase);
                        if (outPath == null) { AppendLog("Failed: " + r.Name); continue; }
                        var dest = Path.Combine(destBase, "super.img");
                        if (outPath != dest)
                        {
                            try 
                            { 
                                if (File.Exists(dest)) File.Delete(dest);
                                File.Move(outPath, dest); 
                                AppendLog("Saved: " + dest); 
                            }
                            catch { AppendLog("Rename failed, file at: " + outPath); }
                        }
                        else
                        {
                            AppendLog("Saved: " + dest);
                        }
                    }
                }

                AppendLog("Read selected partitions completed");

                if (ExportXmlCheckBox.IsChecked == true)
                {
                    ExportPartitionsToXml(selected, destBase);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Error: " + ex.Message);
            }
            finally
            {
                _cts = null;
            }
        }

        private async System.Threading.Tasks.Task ReadByCustomXml()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog 
            { 
                Filter = "XML Files|*.xml|All Files|*.*",
                Multiselect = true,
                Title = "Select XML files for reading partitions"
            };
            
            if (dlg.ShowDialog() != true) return;
            
            var xmlFiles = dlg.FileNames.ToList();
            if (xmlFiles.Count == 0)
            {
                AppendLog("No XML files selected");
                return;
            }

            _cts = new CancellationTokenSource();
            try
            {
                AppendLog(Lang.WaitingForEdl);
                var port = await edl.WaitForEdlPortAsync();
                AppendLog(Lang.DeviceConnected + port);

                AppendLog(Lang.Configuring);
                var ok = await edl.ConfigureAsync(port);
                if (!ok) { AppendLog(Lang.ConfigureFailed); return; }

                AppendLog(Lang.TestingRwMode);
                var mode = await edl.TestRwModeAsync(port);
                AppendLog("RW mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));

                AppendLog($"Reading by custom XML ({xmlFiles.Count} file(s))...");
                var outDir = await edl.ReadByXmlAsync(port, xmlFiles, mode.Item1);
                
                if (outDir == null)
                {
                    AppendLog("Failed to read by XML");
                }
                else
                {
                    AppendLog($"Read by XML completed. Output: {outDir}");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Error: " + ex.Message);
            }
            finally
            {
                _cts = null;
            }
        }

        private void ExportPartitionsToXml(List<PartitionRow> partitions, string outputDir)
        {
            try
            {
                var groupedByLun = partitions.GroupBy(r => r.Lun).OrderBy(g => g.Key);

                foreach (var lunGroup in groupedByLun)
                {
                    var lun = lunGroup.Key;
                    var xmlPath = Path.Combine(outputDir, $"rawprogram_{lun}_backup.xml");

                    var doc = new XDocument(
                        new XDeclaration("1.0", "utf-8", null),
                        new XElement("data")
                    );
                    var dataElement = doc.Element("data")!;

                    dataElement.Add(new XComment(" Generated by OPLUS EDL Tool "));
                    dataElement.Add(new XComment($" Export time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} "));
                    dataElement.Add(new XComment($" LUN {lun} - {lunGroup.Count()} partitions "));

                    foreach (var partition in lunGroup.OrderBy(p => p.FirstLBA))
                    {
                        var sectorSize = 4096;
                        if (!string.IsNullOrEmpty(partition.SectorSize) && int.TryParse(partition.SectorSize, out var parsedSize))
                            sectorSize = parsedSize;

                        var numSectors = partition.LastLBA - partition.FirstLBA + 1;
                        if (!string.IsNullOrEmpty(partition.NumSectors) && ulong.TryParse(partition.NumSectors, out var parsedNumSectors))
                            numSectors = parsedNumSectors;

                        var startByteHex = $"0x{partition.FirstLBA * (ulong)sectorSize:x}";
                        var sizeInKB = (numSectors * (ulong)sectorSize) / 1024.0;

                        var fileName = partition.Name + ".img";

                        var programElement = new XElement("program",
                            new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString()),
                            new XAttribute("file_sector_offset", "0"),
                            new XAttribute("filename", fileName),
                            new XAttribute("label", partition.Name),
                            new XAttribute("num_partition_sectors", numSectors.ToString()),
                            new XAttribute("partofsingleimage", "false"),
                            new XAttribute("physical_partition_number", lun.ToString()),
                            new XAttribute("readbackverify", "false"),
                            new XAttribute("size_in_KB", sizeInKB.ToString("F1")),
                            new XAttribute("sparse", "false"),
                            new XAttribute("start_byte_hex", startByteHex),
                            new XAttribute("start_sector", partition.FirstLBA.ToString())
                        );

                        dataElement.Add(programElement);
                    }

                    doc.Save(xmlPath);
                    AppendLog(string.Format(Lang.ExportedXml, xmlPath));
                }
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(Lang.ExportXmlFailed, ex.Message));
            }
        }

        private async void WriteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (rawProgramFiles == null || rawProgramFiles.Length == 0 || string.IsNullOrEmpty(romImagesPath))
            {
                AppendLog(Lang.NoRomLoaded);
                return;
            }

            var selected = rows.Where(r => r.IsSelected && !string.IsNullOrEmpty(r.FilePath) && !r.FilePath.StartsWith("[NOT FOUND]")).ToList();
            if (selected.Count == 0) { AppendLog(Lang.NoValidPartitions); return; }

            var persistPartitions = selected.Where(r => r.Name.Equals("persist", StringComparison.OrdinalIgnoreCase)).ToList();
            if (persistPartitions.Count > 0)
            {
                var persistResult = System.Windows.MessageBox.Show(
                    Lang.PersistPartitionWarningMessage,
                    Lang.PersistPartitionWarning,
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);
                
                if (persistResult == MessageBoxResult.Cancel)
                {
                    AppendLog("用户取消了刷机操作");
                    return;
                }
                else if (persistResult == MessageBoxResult.No)
                {
                    // 用户选择跳过persist分区
                    selected = selected.Where(r => !r.Name.Equals("persist", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (selected.Count == 0)
                    {
                        AppendLog("已跳过persist分区，没有其他分区需要刷写");
                        return;
                    }
                    AppendLog("已跳过persist分区，继续刷写其他分区");
                }
            }

            var confirmMsg = string.Format(Lang.ConfirmFlashMessage, selected.Count) +
                string.Join(", ", selected.Take(10).Select(r => r.Name)) + 
                (selected.Count > 10 ? string.Format(Lang.AndMore, selected.Count - 10) : "");
            var result = System.Windows.MessageBox.Show(
                confirmMsg,
                Lang.ConfirmFlash,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _cts = new CancellationTokenSource();
            try
            {
                AppendLog(Lang.WaitingForEdl);
                var port = await edl.WaitForEdlPortAsync();
                AppendLog(Lang.DeviceConnected + port);

                AppendLog(Lang.Configuring);
                var ok = await edl.ConfigureAsync(port);
                if (!ok) { AppendLog(Lang.ConfigureFailed); return; }

                var selectedLabels = new HashSet<string>(selected.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
                
                var workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OplusEdlTool");
                Directory.CreateDirectory(workDir);

                var searchPath = romImagesPath;
                if (searchPath.EndsWith("\\")) searchPath = searchPath.TrimEnd('\\');
                if (searchPath.EndsWith(":")) searchPath += ".";

                var (specialXmls, normalXmls) = RawProgramXmlProcessor.SeparateXmlFiles(rawProgramFiles);
                
                if (_isFromOfpExtract)
                {
                    AppendLog("ROM from OFP/OPS extraction, using standard flash mode");
                    normalXmls.AddRange(specialXmls);
                    specialXmls.Clear();
                }

                var tempFiles = new List<string>();

                if (specialXmls.Count > 0)
                {
                    var (processedSpecialXmls, failedXmls) = _xmlProcessor.PrepareXmlFilesForFlash(specialXmls, romImagesPath, workDir);
                    tempFiles.AddRange(processedSpecialXmls);
                    
                    normalXmls.AddRange(failedXmls);

                    if (processedSpecialXmls.Count > 0)
                    {
                        var filteredSpecialXmls = new List<string>();
                        foreach (var xmlFile in processedSpecialXmls)
                        {
                            var filteredXml = GenerateFilteredXml(xmlFile, selectedLabels, workDir, "special_filtered_");
                            if (filteredXml != null)
                            {
                                filteredSpecialXmls.Add(filteredXml);
                                tempFiles.Add(filteredXml);
                            }
                        }

                        if (filteredSpecialXmls.Count > 0)
                        {
                            ok = await edl.WriteByXmlWithOplusRwModeAsync(port, filteredSpecialXmls, searchPath);
                            
                            if (!ok)
                            {
                                AppendLog(Lang.FlashFailed);
                                foreach (var f in tempFiles) { try { File.Delete(f); } catch { } }
                                return;
                            }
                        }
                    }
                }

                if (normalXmls.Count > 0)
                {
                    var mode = await edl.TestRwModeAsync(port);
                    var rwmode = mode.Item1;

                    var sparseLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                    { 
                        "super", "userdata", "oplusreserve2" 
                    };
                    
                    var hasSparse = selected.Any(r => sparseLabels.Contains(r.Name));
                    var hasNonSparse = selected.Any(r => !sparseLabels.Contains(r.Name));

                    if (hasNonSparse)
                    {
                        var nonSparseLabels = new HashSet<string>(
                            selected.Where(r => !sparseLabels.Contains(r.Name)).Select(r => r.Name), 
                            StringComparer.OrdinalIgnoreCase);
                        
                        var nonSparseXmlFiles = new List<string>();
                        foreach (var xmlFile in normalXmls)
                        {
                            var filteredXml = GenerateFilteredXml(xmlFile, nonSparseLabels, workDir, "nonsparse_");
                            if (filteredXml != null)
                            {
                                nonSparseXmlFiles.Add(filteredXml);
                                tempFiles.Add(filteredXml);
                            }
                        }

                        if (nonSparseXmlFiles.Count > 0)
                        {
                            ok = await edl.WriteByXmlAsync(port, nonSparseXmlFiles, rwmode, searchPath);

                            if (!ok)
                            {
                                AppendLog(Lang.FlashFailed);
                                foreach (var f in tempFiles) { try { File.Delete(f); } catch { } }
                                return;
                            }
                        }
                    }

                    if (hasSparse)
                    {
                        var sparseSelectedLabels = new HashSet<string>(
                            selected.Where(r => sparseLabels.Contains(r.Name)).Select(r => r.Name), 
                            StringComparer.OrdinalIgnoreCase);
                        
                        var sparseXmlFiles = new List<string>();
                        foreach (var xmlFile in normalXmls)
                        {
                            var filteredXml = GenerateFilteredXml(xmlFile, sparseSelectedLabels, workDir, "sparse_");
                            if (filteredXml != null)
                            {
                                sparseXmlFiles.Add(filteredXml);
                                tempFiles.Add(filteredXml);
                            }
                        }

                        if (sparseXmlFiles.Count > 0)
                        {
                            ok = await edl.WriteByXmlAsync(port, sparseXmlFiles, "", searchPath);

                            if (!ok)
                            {
                                AppendLog(Lang.FlashFailed);
                                foreach (var f in tempFiles) { try { File.Delete(f); } catch { } }
                                return;
                            }
                        }
                    }
                }

                AppendLog(Lang.FlashCompleted);

                if (rawProgramFiles != null && rawProgramFiles.Length > 0)
                {
                    AppendLog("Applying patch files...");
                    
                    var patchMode = await edl.TestRwModeAsync(port);
                    var patchRwMode = patchMode.Item1;
                    
                    var patchCount = await edl.WritePatchXmlsAsync(port, rawProgramFiles, romImagesPath, patchRwMode);
                    
                    if (patchCount > 0)
                    {
                        AppendLog($"Patch files applied: {patchCount} file(s)");
                    }
                    else
                    {
                        AppendLog("No patch files were applied (files may not exist)");
                    }
                }

                if (AutoRebootCheckBox.IsChecked == true)
                {
                    AppendLog("Auto reboot is enabled, rebooting device...");
                    var rebootOk = await edl.RebootToEdlAsync(port);
                    if (rebootOk)
                    {
                        AppendLog("Device reboot command sent successfully!");
                    }
                    else
                    {
                        AppendLog("Warning: Failed to send reboot command");
                    }
                }

                AppendLog(Lang.AllOperationsCompleted);

                foreach (var f in tempFiles)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Error: " + ex.Message);
            }
            finally
            {
                _cts = null;
            }
        }

        private string? GenerateFilteredXml(string originalXmlPath, HashSet<string> selectedLabels, string workDir, string prefix = "filtered_")
        {
            try
            {
                var doc = XDocument.Load(originalXmlPath);
                var dataElement = doc.Element("data");
                if (dataElement == null) return null;
                
                var programs = dataElement.Elements("program").ToList();
                bool hasSelectedPartition = false;
                var gptElements = new List<XElement>();
                var toRemove = new List<XElement>();

                foreach (var program in programs)
                {
                    var label = program.Attribute("label")?.Value ?? "";
                    var filename = program.Attribute("filename")?.Value ?? "";
                    
                    if (label == "PrimaryGPT" || label == "BackupGPT")
                    {
                        if (!string.IsNullOrEmpty(filename) && !prefix.StartsWith("sparse"))
                        {
                            gptElements.Add(program);
                            hasSelectedPartition = true;
                        }
                        toRemove.Add(program);
                        continue;
                    }
                    
                    if (!string.IsNullOrEmpty(filename))
                    {
                        if (selectedLabels.Contains(label))
                        {
                            hasSelectedPartition = true;
                        }
                        else
                        {
                            toRemove.Add(program);
                        }
                    }
                }

                foreach (var elem in toRemove)
                {
                    elem.Remove();
                }

                foreach (var gpt in gptElements.OrderBy(e => e.Attribute("label")?.Value == "BackupGPT" ? 1 : 0))
                {
                    dataElement.Add(gpt);
                }

                if (!hasSelectedPartition)
                {
                    return null;
                }

                var filteredPath = Path.Combine(workDir, prefix + Path.GetFileName(originalXmlPath));
                doc.Save(filteredPath);
                return filteredPath;
            }
            catch (Exception ex)
            {
                AppendLog($"Error filtering XML {Path.GetFileName(originalXmlPath)}: {ex.Message}");
                return null;
            }
        }

        private async void EraseSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0) { AppendLog(Lang.NoPartitionsSelected); return; }

            var result = System.Windows.MessageBox.Show(string.Format(Lang.ConfirmEraseMessage, selected.Count), Lang.ConfirmErase, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _cts = new CancellationTokenSource();
            try
            {
                AppendLog(Lang.WaitingForEdl);
                var port = await edl.WaitForEdlPortAsync();
                AppendLog(Lang.DeviceConnected + port);

                AppendLog(Lang.Configuring);
                var ok = await edl.ConfigureAsync(port);
                if (!ok) { AppendLog(Lang.ConfigureFailed); return; }

                AppendLog(Lang.TestingRwMode);
                var mode = await edl.TestRwModeAsync(port);
                AppendLog("RW mode: " + mode.Item1);

                foreach (var r in selected)
                {
                    if (_cts.Token.IsCancellationRequested) { AppendLog("Task cancelled"); break; }
                    AppendLog($"Erasing partition: {r.Name}...");
                    ok = await edl.ErasePartitionAsync(port, mode.Item1, r.ToEntry());
                    AppendLog(ok ? ("Erased: " + r.Name) : ("Failed: " + r.Name));
                }
                AppendLog("Erase selected partitions completed");
            }
            catch (Exception ex)
            {
                AppendLog("Error: " + ex.Message);
            }
            finally
            {
                _cts = null;
            }
        }

        private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is DataGridCell))
            {
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }
            
            if (dep is DataGridCell cell)
            {
                var columnIndex = cell.Column.DisplayIndex;
                if (columnIndex == 0)
                {
                    var row = cell.DataContext as PartitionRow;
                    if (row != null)
                    {
                        row.IsSelected = !row.IsSelected;
                        e.Handled = true;
                        
                        Grid.Items.Refresh();
                    }
                }
            }
        }

        private void Grid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is DataGridCell))
            {
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }
            
            if (dep is DataGridCell cell && cell.Column?.Header?.ToString() == "File Path")
            {
                e.Handled = true;
                var row = cell.DataContext as PartitionRow;
                if (row == null) return;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var ofd = new Microsoft.Win32.OpenFileDialog 
                    { 
                        Filter = "Binary|*.bin;*.img|All|*.*", 
                        FileName = row.FilePath?.StartsWith("[NOT FOUND]") == true ? "" : row.FilePath 
                    };
                    if (ofd.ShowDialog() == true)
                    {
                        row.FilePath = ofd.FileName ?? string.Empty;
                        var source = Grid.ItemsSource;
                        Grid.ItemsSource = null;
                        Grid.ItemsSource = source;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column.Header.ToString() == "File Path")
            {
                e.Cancel = true;
                var row = (PartitionRow)e.Row.Item;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var ofd = new Microsoft.Win32.OpenFileDialog 
                    { 
                        Filter = "Binary|*.bin;*.img|All|*.*", 
                        FileName = row.FilePath?.StartsWith("[NOT FOUND]") == true ? "" : row.FilePath 
                    };
                    if (ofd.ShowDialog() == true)
                    {
                        row.FilePath = ofd.FileName ?? string.Empty;
                        var source = Grid.ItemsSource;
                        Grid.ItemsSource = null;
                        Grid.ItemsSource = source;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        #endregion
    }
}
