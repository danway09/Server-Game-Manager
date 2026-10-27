using System.Configuration;
using System.Data;
using System.Windows;
using MCServerManager.Managers;

namespace MCServerManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Тему применяем ДО base.OnStartup: иначе главное окно успеет
            // создаться со светлой палитрой и останется светлым.
            ThemeManager.ApplySaved();

            base.OnStartup(e);
        }
    }

}
