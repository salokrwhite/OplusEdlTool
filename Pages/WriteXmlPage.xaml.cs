using System.IO;
using System.Linq;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OplusEdlTool.Services;

namespace OplusEdlTool.Pages
{
    public partial class WriteXmlPage : UserControl
    {
        private readonly EdlService edl;
        private readonly System.Action<string> log;
        public WriteXmlPage(EdlService edl, System.Action<string> logger)
        {
            InitializeComponent(); this.edl = edl; this.log = logger;
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
            var xmls = XmlList.Items.Cast<string>().ToList(); var searchPath = xmls.Count > 0 ? Path.GetDirectoryName(xmls[0]) ?? "" : "";
            log("waiting for edl port (9008)..."); var port = await edl.WaitForEdlPortAsync();
            log("device connected: " + port);
            log("configure..."); var ok = await edl.ConfigureAsync(port); if (!ok) { log("failed"); return; }
            log("test rw mode..."); var mode = await edl.TestRwModeAsync(port); log("rw mode: " + mode.Item1 + (string.IsNullOrEmpty(mode.Item2) ? "" : (" " + mode.Item2)));
            log("write..."); ok = await edl.WriteByXmlAsync(port, xmls, mode.Item1, searchPath);
            log(ok ? "all done" : "failed");
        }
    }
}
