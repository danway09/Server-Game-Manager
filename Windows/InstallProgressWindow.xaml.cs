using System.Windows;

namespace MCServerManager
{
    public partial class InstallProgressWindow : Window
    {

        public InstallProgressWindow(string title)
        {
            InitializeComponent();


            TitleText.Text = title;

        }




        public void AppendLine(string line)
        {

            if (!Dispatcher.CheckAccess())
            {

                Dispatcher.Invoke(() => AppendLine(line));


                return;

            }



            LogBox.Text += line + "\n";


            LogBox.ScrollToEnd();

        }

    }
}
