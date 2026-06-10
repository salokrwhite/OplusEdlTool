using System;
using OplusEdlTool.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace OplusEdlTool
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);
            
            LanguageService.Initialize();
            
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }


    }
}
