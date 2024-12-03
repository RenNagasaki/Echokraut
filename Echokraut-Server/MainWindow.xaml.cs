using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Echokraut_Server
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        bool loaded = false;

        public MainWindow()
        {
            InitializeComponent();
            loadConfig();
            loaded = true;
        }

        private void loadConfig()
        {
            slider_InstanceCount.Value = Properties.Settings.Default.instanceCount;
            lbl_InstanceCount.Content = slider_InstanceCount.Value;
            chBox_ShutDownAfter1Hour.IsChecked = Properties.Settings.Default.shutdownIdle;
        }

        private void saveConfig()
        {
            Properties.Settings.Default.instanceCount = slider_InstanceCount.Value;
            Properties.Settings.Default.shutdownIdle = chBox_ShutDownAfter1Hour.IsChecked.Value;
            Properties.Settings.Default.Save();
        }

        private void slider_InstanceCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (loaded)
            {
                var value = slider_InstanceCount.Value;

                value = Math.Round(value);
                slider_InstanceCount.Value = value;
                lbl_InstanceCount.Content = value;

                saveConfig();
            }
        }

        private void chBox_ShutDownAfter1Hour_Click(object sender, RoutedEventArgs e)
        {
            saveConfig();
        }

        private void btn_PrepareInstances_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btn_Start_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            saveConfig();
        }
    }
}
