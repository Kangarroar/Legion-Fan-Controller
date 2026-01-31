using Microsoft.UI.Xaml;
using System;
using System.IO;

namespace Lenovo_Fan_Controller
{
    public partial class App : Application
    {
        public static string FanControlPath { get; private set; }
        public static string BalancedConfigPath { get; private set; }
        public static string PerformanceConfigPath { get; private set; }
        public static string QuietConfigPath { get; private set; }

        public App()
        {
            this.InitializeComponent();
            InitializePaths();
            InitializeConfigFiles();

        }


        private void InitializePaths()
        {
            string baseDir = AppContext.BaseDirectory;
            string fanControlDir = Path.Combine(baseDir, "Fan Control");
            Directory.CreateDirectory(fanControlDir);

            FanControlPath = Path.Combine(fanControlDir, "FanControl.exe");
            BalancedConfigPath = Path.Combine(fanControlDir, "fan_config_balanced.txt");
            PerformanceConfigPath = Path.Combine(fanControlDir, "fan_config_perfcust.txt");
            QuietConfigPath = Path.Combine(fanControlDir, "fan_config_quiet.txt");
        }

        private void InitializeConfigFiles()
        {
            string defaultConfig = @"legion_gen : 5
fan_curve_points : 5
fan_accl_value : 2
fan_deccl_value : 2
fan_rpm_points : 1000 1500 2200 3600 4400
cpu_temps_ramp_up : 31 45 55 60 65
cpu_temps_ramp_down : 30 43 53 58 63
gpu_temps_ramp_up : 31 50 55 60 63
gpu_temps_ramp_down : 30 48 53 58 61
hst_temps_ramp_up : 31 50 55 65 70
hst_temps_ramp_down : 30 48 53 63 68";

            CreateConfigIfNeeded(BalancedConfigPath, defaultConfig);
            CreateConfigIfNeeded(PerformanceConfigPath, defaultConfig);
            CreateConfigIfNeeded(QuietConfigPath, defaultConfig.Replace("3900", "4400"));
        }

        private void CreateConfigIfNeeded(string path, string content)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                File.WriteAllText(path, content);
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Config files are already initialized by now
            m_window = new MainWindow();
            m_window.ExtendsContentIntoTitleBar = true;

            if (m_window.Content is FrameworkElement rootElement)
            {
                m_window.SetTitleBar(rootElement.FindName("AppTitleBar") as UIElement);
            }

            m_window.Activate();
        }

        private Window m_window;
    }
}