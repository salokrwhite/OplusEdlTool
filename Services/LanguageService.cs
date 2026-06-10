using System;
using System.IO;
using System.Text.Json;

namespace OplusEdlTool.Services
{
    public static class LanguageService
    {
        private static string _currentLanguage = "en";
        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OplusEdlTool", "language.json");

        public static string CurrentLanguage => _currentLanguage;

        public static bool IsChinese => _currentLanguage == "zh";

        public static void Initialize()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Language settings file: {SettingsFile}");
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    System.Diagnostics.Debug.WriteLine($"Language settings content: {json}");
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var settings = JsonSerializer.Deserialize<LanguageSettings>(json, options);
                    if (settings != null && !string.IsNullOrEmpty(settings.Language))
                    {
                        _currentLanguage = settings.Language;
                        System.Diagnostics.Debug.WriteLine($"Language loaded: {_currentLanguage}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Language settings file not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load language settings: {ex.Message}");
                _currentLanguage = "en";
            }
        }

        public static bool SaveLanguage(string language)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var settings = new LanguageSettings { Language = language };
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFile, json);
                _currentLanguage = language;
                
                return File.Exists(SettingsFile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save language settings: {ex.Message}");
                return false;
            }
        }

        public static bool ToggleLanguage()
        {
            var newLang = _currentLanguage == "en" ? "zh" : "en";
            return SaveLanguage(newLang);
        }

        public static string GetLanguageDisplayName()
        {
            return _currentLanguage == "zh" ? "Translate to English" : "切换为中文";
        }

        private class LanguageSettings
        {
            public string Language { get; set; } = "en";
        }
    }

    public static class Lang
    {
        public static string WindowTitle => LanguageService.IsChinese ? "OPLUS EDL 工具" : "OPLUS EDL Tool";
        
        public static string About => LanguageService.IsChinese ? "关于" : "About";
        
        public static string EnterFirehoseMode => LanguageService.IsChinese ? "进入 Firehose 模式" : "Enter Firehose Mode";
        public static string DeviceProgrammer => LanguageService.IsChinese ? "引导文件 (devprg*.mbn)" : "Device Programmer (devprg*.mbn)";
        public static string Digest => LanguageService.IsChinese ? "Digest文件 (*.bin)" : "Digest (*.bin)";
        public static string Sig => LanguageService.IsChinese ? "签名文件 (*.bin)" : "Sig (*.bin)";
        public static string EnterFirehose => LanguageService.IsChinese ? "进入 Firehose" : "Enter Firehose";
        
        public static string FlashPackage => LanguageService.IsChinese ? "刷机包 (ROM)" : "Flash Package (ROM)";
        public static string SelectRomFolder => LanguageService.IsChinese ? "选择 ROM 文件夹或 OFP/OPS 文件" : "Select ROM folder or OFP/OPS file";
        public static string Load => LanguageService.IsChinese ? "加载" : "Load";
        public static string All => LanguageService.IsChinese ? "全选" : "All";
        public static string None => LanguageService.IsChinese ? "取消" : "None";
        
        public static string Partitions => LanguageService.IsChinese ? "分区" : "Partitions";
        public static string Search => LanguageService.IsChinese ? "搜索:" : "Search:";
        public static string SearchPartition => LanguageService.IsChinese ? "按名称搜索分区" : "Search partition by name";
        public static string Name => LanguageService.IsChinese ? "名称" : "Name";
        public static string Start => LanguageService.IsChinese ? "起始" : "Start";
        public static string End => LanguageService.IsChinese ? "结束" : "End";
        public static string Size => LanguageService.IsChinese ? "大小" : "Size";
        public static string FilePath => LanguageService.IsChinese ? "文件路径" : "File Path";
        public static string SelectAll => LanguageService.IsChinese ? "全选" : "Select All";
        public static string CheckSelected => LanguageService.IsChinese ? "勾选所选" : "Check Selected";
        public static string UncheckSelected => LanguageService.IsChinese ? "取消勾选所选" : "Uncheck Selected";
        public static string ReadPartitions => LanguageService.IsChinese ? "读取分区表" : "Read Partitions";
        public static string ReadSelected => LanguageService.IsChinese ? "读取选中" : "Read Selected";
        public static string WriteSelected => LanguageService.IsChinese ? "写入选中" : "Write Selected";
        public static string EraseSelected => LanguageService.IsChinese ? "擦除选中" : "Erase Selected";
        public static string StopAll => LanguageService.IsChinese ? "停止全部" : "Stop All";
        public static string AutoReboot => LanguageService.IsChinese ? "刷机后自动重启" : "Auto reboot after flash";
        public static string AutoRebootTooltip => LanguageService.IsChinese ? "刷机完成后自动重启设备" : "Automatically reboot device after flashing is complete";
        public static string ExportXml => LanguageService.IsChinese ? "导出 XML" : "Export XML";
        public static string ExportXmlTooltip => LanguageService.IsChinese ? "备份分区时同时导出 rawprogram XML 文件" : "Export rawprogram XML when backing up partitions";
        
        public static string Log => LanguageService.IsChinese ? "日志" : "Log";
        public static string Port9008 => LanguageService.IsChinese ? "9008 端口:" : "9008 Port:";
        public static string NotDetected => LanguageService.IsChinese ? "未检测到" : "Not detected";
        public static string Verbose => LanguageService.IsChinese ? "详细" : "Verbose";
        public static string VerboseTooltip => LanguageService.IsChinese ? "显示详细的 fh_loader 输出用于调试" : "Show detailed fh_loader output for debugging";
        public static string Clear => LanguageService.IsChinese ? "清除" : "Clear";
        public static string Refresh => LanguageService.IsChinese ? "刷新" : "Refresh";
        
        public static string SelectRomSource => LanguageService.IsChinese ? "选择 ROM 来源" : "Select ROM Source";
        public static string SelectFolderOrFile => LanguageService.IsChinese ? "是 = 选择文件夹\n否 = 选择 OFP/OPS 文件" : "Yes = Select Folder\nNo = Select OFP/OPS File";
        public static string ConfirmFlash => LanguageService.IsChinese ? "确认刷机" : "Confirm Flash";
        public static string ConfirmFlashMessage => LanguageService.IsChinese 
            ? "确定要刷写 {0} 个分区吗？\n\n这将向您的设备写入数据。请确保选择了正确的分区。\n\n已选分区:\n" 
            : "Are you sure you want to flash {0} partition(s)?\n\nThis will write data to your device. Make sure you have selected the correct partitions.\n\nSelected partitions:\n";
        public static string AndMore => LanguageService.IsChinese ? "... 以及另外 {0} 个" : "... and {0} more";
        public static string ConfirmErase => LanguageService.IsChinese ? "确认擦除" : "Confirm Erase";
        public static string ConfirmEraseMessage => LanguageService.IsChinese ? "确定要擦除 {0} 个分区吗？" : "Are you sure you want to erase {0} partition(s)?";
        public static string MergeSuperPartition => LanguageService.IsChinese ? "合并 Super 分区" : "Merge Super Partition";
        public static string MergeSuperMessage => LanguageService.IsChinese 
            ? "找到 super_def.json，包含 {0} 个分区。\n\n是否要将它们合并为 super.img？\n\n这可能需要几分钟时间。" 
            : "Found super_def.json with {0} partitions.\n\nDo you want to merge them into super.img?\n\nThis may take several minutes.";
        public static string NoSelection => LanguageService.IsChinese ? "未选择" : "No Selection";
        public static string PleaseSelectXml => LanguageService.IsChinese ? "请至少选择一个 XML 文件来加载。" : "Please select at least one XML file to load.";
        public static string Error => LanguageService.IsChinese ? "错误" : "Error";
        
        public static string LanguageSwitch => LanguageService.IsChinese ? "切换语言" : "Switch Language";
        public static string LanguageSwitchMessage => LanguageService.IsChinese 
            ? "语言将切换为英语。\n需要重启应用程序才能生效。\n\n是否立即重启？" 
            : "Language will be switched to Chinese.\nApplication restart is required.\n\nRestart now?";
        public static string RestartRequired => LanguageService.IsChinese ? "需要重启" : "Restart Required";
        
        public static string PortStatusRefreshed => LanguageService.IsChinese ? "端口状态已刷新" : "Port status refreshed";
        public static string VerboseLoggingEnabled => LanguageService.IsChinese ? "详细日志已启用" : "Verbose logging enabled";
        public static string VerboseLoggingDisabled => LanguageService.IsChinese ? "详细日志已禁用" : "Verbose logging disabled";
        public static string NoTasksRunning => LanguageService.IsChinese ? "没有正在运行的任务" : "No tasks running";
        public static string StoppingTasks => LanguageService.IsChinese ? "正在停止所有任务..." : "Stopping all tasks...";
        public static string ImagesFolderNotFound => LanguageService.IsChinese ? "在选定目录中未找到 IMAGES 文件夹" : "IMAGES folder not found in selected directory";
        public static string NoRawprogramFound => LanguageService.IsChinese ? "未找到 rawprogram*.xml 文件" : "No rawprogram*.xml files found";
        public static string FoundXmlFiles => LanguageService.IsChinese ? "找到 {0} 个 rawprogram XML 文件。请选择要加载的文件。" : "Found {0} rawprogram XML files. Select which ones to load.";
        public static string NoXmlSelected => LanguageService.IsChinese ? "未选择 XML 文件。请勾选要加载的 XML 文件。" : "No XML files selected. Please check the XML files you want to load.";
        public static string LoadingXmlFiles => LanguageService.IsChinese ? "正在加载 {0} 个 XML 文件: {1}" : "Loading {0} XML file(s): {1}";
        public static string NoPartitionsSelected => LanguageService.IsChinese ? "未选择分区" : "No partitions selected";
        public static string NoValidPartitions => LanguageService.IsChinese ? "没有选中带有有效文件路径的分区" : "No partition selected with valid file path";
        public static string NoRomLoaded => LanguageService.IsChinese ? "未加载 ROM 包。请先选择 ROM 文件夹。" : "No ROM package loaded. Please select a ROM folder first.";
        public static string WaitingForEdl => LanguageService.IsChinese ? "等待 EDL 端口 (9008)..." : "Waiting for EDL port (9008)...";
        public static string DeviceConnected => LanguageService.IsChinese ? "设备已连接: " : "Device connected: ";
        public static string Configuring => LanguageService.IsChinese ? "正在配置..." : "Configuring...";
        public static string ConfigureFailed => LanguageService.IsChinese ? "配置失败" : "Failed to configure";
        public static string TestingRwMode => LanguageService.IsChinese ? "测试读写模式..." : "Testing RW mode...";
        public static string FlashCompleted => LanguageService.IsChinese ? "刷机成功完成！" : "Flash completed successfully!";
        public static string FlashFailed => LanguageService.IsChinese ? "刷机失败！请查看日志了解详情。" : "Flash failed! Check the logs for details.";
        public static string AllOperationsCompleted => LanguageService.IsChinese ? "所有操作已成功完成！" : "All operations completed successfully!";
        public static string DroppedXmlFiles => LanguageService.IsChinese ? "拖入了 {0} 个 XML 文件" : "Dropped {0} XML file(s)";
        public static string NoXmlInDropped => LanguageService.IsChinese ? "拖入的项目中没有 XML 文件" : "No XML files found in dropped items";
        public static string XmlLoadedFrom => LanguageService.IsChinese ? "XML 文件已从以下位置加载: {0}" : "XML files loaded from: {0}";
        public static string TipClickLoad => LanguageService.IsChinese ? "提示: 点击 '加载' 解析分区，然后选择要刷写的分区。" : "Tip: Click 'Load' to parse partitions, then select partitions to flash.";
        public static string CannotDetermineDir => LanguageService.IsChinese ? "无法从拖入的 XML 文件确定目录" : "Cannot determine directory from dropped XML file";
        
        public static string NoPartitionsForExport => LanguageService.IsChinese ? "没有选中分区用于导出" : "No partitions selected for export";
        public static string ExportedXml => LanguageService.IsChinese ? "已导出 XML: {0}" : "Exported XML: {0}";
        public static string ExportXmlFailed => LanguageService.IsChinese ? "导出 XML 失败: {0}" : "Export XML failed: {0}";
        
        public static string SelectReadMethod => LanguageService.IsChinese ? "选择读取方式" : "Select Read Method";
        public static string SelectReadMethodMessage => LanguageService.IsChinese 
            ? "选择读取方式:\n\n是 - 自动读取 (读取分区表中选中的分区)\n否 - 自定义 XML 读取 (读取 XML 文件中指定的分区)\n取消 - 取消操作" 
            : "Choose read method:\n\nYES - Auto Read (Read selected partitions from partition table)\nNO - Custom XML Read (Read partitions specified in XML files)\nCANCEL - Cancel operation";
        public static string PersistPartitionWarning => LanguageService.IsChinese ? "Persist分区警告" : "Persist Partition Warning";
        public static string PersistPartitionWarningMessage => LanguageService.IsChinese 
            ? "您选择了刷写persist分区。\n\n警告：刷写persist分区可能会导致传感器或指纹功能丢失！\n\n是否要继续刷写persist分区？\n\n选择\"是\"继续刷写，选择\"否\"跳过persist分区。" 
            : "You have selected to flash the persist partition.\n\nWarning: Flashing the persist partition may cause sensor or fingerprint functionality loss!\n\nDo you want to continue flashing the persist partition?\n\nSelect 'Yes' to continue flashing, select 'No' to skip the persist partition.";
    }
}
