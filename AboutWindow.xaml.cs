using System.Diagnostics;
using System.Windows;

namespace OplusEdlTool
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

    }
}
