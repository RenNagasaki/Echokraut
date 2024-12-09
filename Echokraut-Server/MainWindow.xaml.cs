using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Echokraut_Server
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        bool loaded = false;
        bool prepareDone = false;
        internal static bool closing = false;
        static bool preparing = false;
        static string progressText = "";
        static int progressValue = 0;
        static double progressMaximum = 0;
        string localPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        Thread workerThread;

        List<AlltalkInstance> instances = new List<AlltalkInstance>();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            loaded = true;
        }

        private void LoadConfig()
        {
            slider_InstanceCount.Value = Properties.Settings.Default.instanceCount;
            tBox_PortRange.Text = "7851 - " + (7852 + (2 * slider_InstanceCount.Value) - 2);
            lbl_InstanceCount.Content = slider_InstanceCount.Value;
            chBox_ShutDownAfter1Hour.IsChecked = Properties.Settings.Default.shutdownIdle;
            tBox_ModelData.Text = Properties.Settings.Default.modelData;
            if (string.IsNullOrWhiteSpace(tBox_ModelData.Text))
                tBox_ModelData.Text = localPath + @"\FFXIV - trained model";
            tBox_ModelName.Text = Properties.Settings.Default.modelName;
            prepareDone = Properties.Settings.Default.prepareDone;

            if (prepareDone)
            {
                for (int i = 1; i <= slider_InstanceCount.Value; i++)
                {
                    var instancePath = $@"{localPath}\alltalk_tts_{i}";
                    instancePath += @"\alltalk_tts";
                    var intanceIndex = i;
                    var port = 7851 + (2 * intanceIndex) - 2;
                    CreateAlltalkInstance(instancePath, intanceIndex, port);
                }
                EnableStep3();
            }
        }

        private void SaveConfig()
        {
            Properties.Settings.Default.instanceCount = slider_InstanceCount.Value;
            Properties.Settings.Default.shutdownIdle = chBox_ShutDownAfter1Hour.IsChecked.Value;
            Properties.Settings.Default.modelData = tBox_ModelData.Text;
            Properties.Settings.Default.modelName = tBox_ModelName.Text;
            Properties.Settings.Default.prepareDone = prepareDone;
            Properties.Settings.Default.Save();
        }

        private void EnableStep2()
        {
            btn_IsCudaInstalled.IsEnabled = false;
            img_CudaCross.Visibility = Visibility.Hidden;
            img_CudaCheckmark.Visibility = Visibility.Visible;
            tBlock_DownloadCuda.Visibility = Visibility.Hidden;
            lbl_DownloadCuda.Visibility = Visibility.Hidden;

            btn_PrepareInstances.IsEnabled = true;
            slider_InstanceCount.IsEnabled = true;

            btn_Start.IsEnabled = false;
            btn_Stop.IsEnabled = false;

            tBox_ModelData.IsEnabled = true;
            tBox_ModelName.IsEnabled = true;
            btn_ResetModel.IsEnabled = true;
            btn_SelectModelData.IsEnabled = true;
        }

        private void EnableStep3()
        {
            btn_IsCudaInstalled.IsEnabled = false;
            img_CudaCross.Visibility = Visibility.Hidden;
            img_CudaCheckmark.Visibility = Visibility.Visible;
            tBlock_DownloadCuda.Visibility = Visibility.Hidden;
            lbl_DownloadCuda.Visibility = Visibility.Hidden;

            btn_PrepareInstances.IsEnabled = false;
            slider_InstanceCount.IsEnabled = true;

            btn_Start.IsEnabled = true;
            btn_Stop.IsEnabled = false;

            tBox_ModelData.IsEnabled = true;
            tBox_ModelName.IsEnabled = true;
            btn_ResetModel.IsEnabled = true;
            btn_SelectModelData.IsEnabled = true;
        }

        private void StartInstances()
        {
            try
            {
                btn_IsCudaInstalled.IsEnabled = false;
                img_CudaCross.Visibility = Visibility.Hidden;
                img_CudaCheckmark.Visibility = Visibility.Visible;
                tBlock_DownloadCuda.Visibility = Visibility.Hidden;
                lbl_DownloadCuda.Visibility = Visibility.Hidden;

                btn_PrepareInstances.IsEnabled = false;
                slider_InstanceCount.IsEnabled = false;

                btn_Start.IsEnabled = false;
                btn_Stop.IsEnabled = true;

                tBox_ModelData.IsEnabled = false;
                tBox_ModelName.IsEnabled = false;
                btn_ResetModel.IsEnabled = false;
                btn_SelectModelData.IsEnabled = false;

                for (int i = 1; i <= slider_InstanceCount.Value; i++)
                {
                    var instancePath = $@"{localPath}\alltalk_tts_{i}\alltalk_tts";
                    var instance = this.instances[i - 1];

                    instance.Start();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log(ex.Message + "\r\n" + ex.StackTrace, this.Dispatcher, tBlock_ConsoleGeneral);
            }
        }

        private void StopInstances()
        {
            foreach (var instance in this.instances)
            {
                instance.Stop();
            }
            EnableStep3();
        }

        private void CheckModelData()
        {
            if (!string.IsNullOrWhiteSpace(tBox_ModelData.Text))
            {
                if (Directory.Exists(tBox_ModelData.Text + @"\models") && Directory.Exists(tBox_ModelData.Text + @"\voices"))
                {
                    if (prepareDone)
                        UpdateInstanceConfigs();
                    return;
                }
            }
        }

        private void UpdateInstanceConfigs()
        {
            for (int i = 1; i <= instances.Count; i++)
            {
                var instancePath = $@"{localPath}\alltalk_tts_{i}";
                instancePath += @"\alltalk_tts";

                dynamic configEngine = JsonConvert.DeserializeObject(File.ReadAllText(instancePath + @"\system\tts_engines\tts_engines.json"));
                if (configEngine != null)
                {
                    configEngine["engine_loaded"] = "xtts";
                    configEngine["selected_model"] = tBox_ModelName.Text;
                    File.WriteAllText(instancePath + @"\system\tts_engines\tts_engines.json", JsonConvert.SerializeObject(configEngine));
                }

                LogHelper.Log($"Modifying configs:", this.Dispatcher, tBlock_ConsoleGeneral);
                try
                {
                    Directory.Delete(instancePath + @"\voices", true);
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"Error: {ex.Message + "\r\n" + ex.StackTrace}", this.Dispatcher, tBlock_ConsoleGeneral);
                }
                try
                {
                    Directory.Delete(instancePath + @"\models", true);
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"Error: {ex.Message + "\r\n" + ex.StackTrace}", this.Dispatcher, tBlock_ConsoleGeneral);
                }
                try
                {
                    SoftwareHelper.CreateHardlinks(instancePath, tBox_ModelData.Text, this.Dispatcher, tBlock_ConsoleGeneral);
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"Error: {ex.Message + "\r\n" + ex.StackTrace}", this.Dispatcher, tBlock_ConsoleGeneral);
                }
            }
        }

        private void CleanupOldData()
        {
            var directories = Directory.GetDirectories(localPath);

            foreach (var directory in directories)
            {
                if (directory.StartsWith("alltalk_tts_"))
                    Directory.Delete(directory, true);
            }

            if (this.instances.Count > 0)
            {
                foreach (var instance in this.instances)
                {
                    if (!instance.process.HasExited)
                    {
                        instance.Stop();
                    }
                }

                this.instances.Clear();
            }
        }

        private void CreateAlltalkInstance(string instancePath, int instanceNumber, int port)
        {
            var process = SoftwareHelper.CreateInstance(instancePath, this.Dispatcher, tBlock_ConsoleGeneral);
            AlltalkInstance instance = new AlltalkInstance(instanceNumber, port, process);
            this.instances.Add(instance);
        }

        private void PrepareInstances()
        {
            progress_Instances.Maximum = (slider_InstanceCount.Value * 5) + 1;
            progress_Instances.Value = 0;
            preparing = true;
            progressValue = 0;
            progressMaximum = progress_Instances.Maximum;
            progressText = $"Preparing Instances ({progress_Instances.Value}/{progress_Instances.Maximum})";
            Thread updateThread = new Thread(() =>
            {
                while (preparing)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        progress_Instances.Value = progressValue;
                        lbl_Instances.Content = progressText;
                    });

                    if (progressMaximum == progressValue)
                        preparing = false;

                    Thread.Sleep(200);
                }
            });
            updateThread.Start();

            var instancesToPrepare = slider_InstanceCount.Value;
            workerThread = new Thread(() =>
            {
                for (int i = 1; i <= instancesToPrepare; i++)
                {
                    var instancePath = $@"{localPath}\alltalk_tts_{i}";
                    var intanceIndex = i;
                    var port = 7851 + (2 * intanceIndex) - 2;
                    var gradioPort = 7852 + (2 * intanceIndex) - 2;
                    LogHelper.Log($"Creating instance {intanceIndex}:", this.Dispatcher, tBlock_ConsoleGeneral);

                    ZipFile.ExtractToDirectory(localPath + @"\alltalk_tts.zip", instancePath);
                    instancePath += @"\alltalk_tts";
                    progressValue++;
                    progressText = $"Preparing Instances ({progressValue}/{progressMaximum})";

                    dynamic config = JsonConvert.DeserializeObject(File.ReadAllText(instancePath + @"\confignew.json"));
                    if (config != null)
                    {
                        LogHelper.Log($"Modifying configs:", this.Dispatcher, tBlock_ConsoleGeneral);
                        config["gradio_port_number"] = gradioPort;
                        config["firstrun_model"] = false;
                        config["api_def"]["api_port_number"] = port;

                        File.WriteAllText(instancePath + @"\confignew.json", JsonConvert.SerializeObject(config));
                    }

                    dynamic configEngine = JsonConvert.DeserializeObject(File.ReadAllText(instancePath + @"\system\tts_engines\tts_engines.json"));
                    if (configEngine != null)
                    {
                        configEngine["engine_loaded"] = "xtts";
                        configEngine["selected_model"] = "xtts - xttsv2_2.0.3";
                        File.WriteAllText(instancePath + @"\system\tts_engines\tts_engines.json", JsonConvert.SerializeObject(configEngine));
                    }

                    dynamic configEngine2 = JsonConvert.DeserializeObject(File.ReadAllText(instancePath + @"\system\tts_engines\xtts\model_settings.json"));
                    if (configEngine2 != null)
                    {
                        configEngine2["settings"]["deepspeed_enabled"] = true;
                        File.WriteAllText(instancePath + @"\system\tts_engines\xtts\model_settings.json", JsonConvert.SerializeObject(configEngine2));
                    }
                    progressValue++;
                    progressText = $"Preparing Instances ({progressValue}/{progressMaximum})";
                    LogHelper.Log($"Cleaning default data:", this.Dispatcher, tBlock_ConsoleGeneral);
                    try
                    {
                        Directory.Delete(instancePath + @"\voices", true);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Log($"Error: {ex.Message + "\r\n" + ex.StackTrace}", this.Dispatcher, tBlock_ConsoleGeneral);
                    }
                    progressValue++;
                    progressText = $"Preparing Instances ({progressValue}/{progressMaximum})";
                    this.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            SoftwareHelper.CreateHardlinks(instancePath, tBox_ModelData.Text, this.Dispatcher, tBlock_ConsoleGeneral);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Log($"Error: {ex.Message + "\r\n" + ex.StackTrace}", this.Dispatcher, tBlock_ConsoleGeneral);
                        }
                        progressValue++;
                        progressText = $"Installing Alltalk for Instance {intanceIndex} ({progressValue}/{progressMaximum})";
                    });

                    SoftwareHelper.InstallAlltalk(instancePath, this.Dispatcher, tBlock_ConsoleGeneral);
                    progressValue++;
                    progressText = $"Preparing Instances ({progressValue}/{progressMaximum})";

                    this.Dispatcher.Invoke(() =>
                    {
                        CreateAlltalkInstance(instancePath, i, port);
                    });
                }

                SoftwareHelper.InstallEspeak(this.Dispatcher, tBlock_ConsoleGeneral);
                progressValue++;
                progressText = $"Done ({progressValue}/{progressMaximum})";
                prepareDone = true;
                this.Dispatcher.Invoke(() => {
                    SaveConfig();
                    EnableStep3();
                });
            });
            workerThread.Start();
        }

        private void slider_InstanceCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (loaded)
            {
                var value = slider_InstanceCount.Value;

                value = Math.Round(value);
                slider_InstanceCount.Value = value;
                lbl_InstanceCount.Content = value;
                tBox_PortRange.Text = "7851 - " + (7852 + (2 * value) - 2);
                EnableStep2();

                SaveConfig();
            }
        }

        private void chBox_ShutDownAfter1Hour_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
        }

        private void btn_PrepareInstances_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CleanupOldData();
                PrepareInstances();
            }
            catch (Exception ex) {
                string messageBoxText = "An error occured while preparing your alltalk instances: \r\n" + ex.Message + "\r\n" + ex.StackTrace;
                string caption = "Error while preparing instances";
                MessageBoxButton button = MessageBoxButton.OK;
                MessageBoxImage icon = MessageBoxImage.Warning;
                MessageBoxResult result;

                result = MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.OK);
            }
        }

        private void btn_Start_Click(object sender, RoutedEventArgs e)
        {
            StartInstances();
        }

        private void btn_Stop_Click(object sender, RoutedEventArgs e)
        {
            StopInstances();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            closing = true;
            preparing = false;
            StopInstances();
            SaveConfig();
        }

        private void btn_IsCudaInstalled_Click(object sender, RoutedEventArgs e)
        {
            if (!SoftwareHelper.IsCudaSoftwareInstalled(this.Dispatcher, tBlock_ConsoleGeneral))
            {
                img_CudaCross.Visibility = Visibility.Visible;
                img_CudaCheckmark.Visibility = Visibility.Hidden;
                tBlock_DownloadCuda.Visibility = Visibility.Visible;
                lbl_DownloadCuda.Visibility = Visibility.Visible;
            }
            else
            {
                img_CudaCross.Visibility = Visibility.Hidden;
                img_CudaCheckmark.Visibility = Visibility.Visible;
                tBlock_DownloadCuda.Visibility = Visibility.Hidden;
                lbl_DownloadCuda.Visibility = Visibility.Hidden;

                EnableStep2();
                SaveConfig();
            }
        }

        private void btn_SelectModelData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog();
            dlg.Multiselect = false;
            dlg.Title = "Select Model main folder";

            dlg.InitialDirectory = tBox_ModelData.Text;

            if (dlg.ShowDialog().Value)
            {
                tBox_ModelData.Text = dlg.FolderName;
            }
        }

        private void tBox_ModelData_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (loaded)
            {
                CheckModelData();
                SaveConfig();
            }
        }

        private void tBox_ModelName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (loaded)
            {
                CheckModelData();
                SaveConfig();
            }
        }

        private void btn_ResetModel_Click(object sender, RoutedEventArgs e)
        {
            tBox_ModelData.Text = localPath + @"\FFXIV - trained model";
            tBox_ModelName.Text = "xtts - xttsv2_2.0.3";
        }

        private void tBlock_DownloadCuda_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            SoftwareHelper.OpenUrl("https://developer.nvidia.com/cuda-12-1-0-download-archive", this.Dispatcher, tBlock_ConsoleGeneral);
        }
    }
}
