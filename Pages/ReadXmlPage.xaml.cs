using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OplusEdlTool.Services;

namespace OplusEdlTool.Pages
{
    public partial class ReadXmlPage : UserControl
    {
        private readonly EdlService edl;
        private readonly System.Action<string> log;
        public ReadXmlPage(EdlService edl, System.Action<string> logger)
        {
            InitializeComponent(); edl.StorageType = edl.StorageType; this.edl = edl; this.log = logger;
        }
        private void AddXml_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "XML|*.xml", Multiselect = true };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames) XmlList.Items.Add(f);
            }
        }
        private async void Run_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            log("waiting for edl port (9008)..."); 
            var port = await edl.WaitForEdlPortAsync();
            log("device connected: " + port);
            
            log("configure..."); 
            var ok = await edl.ConfigureAsync(port); 
            if (!ok) { log("failed"); return; }
            
            log("test rw mode..."); 
            var mode = await edl.TestRwModeAsync(port); 
            log("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            
            string? outDir = null;
            
            if (ReadModeAuto.IsChecked == true)
            {
                log("readback (auto mode - GPT + all partitions)...");
                outDir = await edl.ReadByAutoAsync(port, mode.Item1);
            }
            else if (ReadModeCustomXml.IsChecked == true)
            {
                var xmls = XmlList.Items.Cast<string>().ToList();
                if (xmls.Count == 0)
                {
                    log("error: no XML files selected");
                    return;
                }
                log($"readback (custom XML mode - {xmls.Count} file(s))...");
                outDir = await edl.ReadByXmlAsync(port, xmls, mode.Item1);
            }
            
            log(outDir == null ? "failed" : ("output: " + outDir));
        }
    }
}
