using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using OplusEdlTool.Services;

namespace OplusEdlTool.Pages
{
    public partial class CleanupPage : UserControl
    {
        private readonly EdlService edl;
        private readonly System.Action<string> log;
        public CleanupPage(EdlService edl, System.Action<string> logger)
        { InitializeComponent(); this.edl = edl; this.log = logger; }
        private async void Run_Click(object sender, System.Windows.RoutedEventArgs e)
        { var ok = await edl.CleanupAsync(); log(ok ? "cleaned" : "cleanup failed"); }
    }
}
