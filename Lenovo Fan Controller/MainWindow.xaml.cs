using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinRT.Interop;

namespace Lenovo_Fan_Controller
{
    public sealed partial class MainWindow : Window
    {
        private FanConfig currentConfig;
        private string currentProfile = "default";
        private XamlRoot _xamlRoot;
        private bool _isWindowReady = false;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Activated += MainWindow_Activated;
            SetWindowProperties();
            SetupEventHandlers();

            // Start initialization when window is ready
            _ = InitializeWhenReadyAsync();
        }

        private void SetWindowProperties()
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var width = 800;
            var height = 750;
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                (displayArea.WorkArea.Width - width) / 2,
                (displayArea.WorkArea.Height - height) / 2,
                width,
                height));
        }
        private void SetupEventHandlers()
        {
            NavigationView.SelectionChanged += NavigationView_SelectionChanged;
            DeviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;
            Save.Click += Save_Click;
            Restart.Click += Restart_Click;
            Turbo.Click += Turbo_Click;
            Quiet.Click += Quiet_Click;
        }
        private async Task InitializeWhenReadyAsync()
        {
            // Wait until window is fully activated
            while (!_isWindowReady)
            {
                await Task.Delay(50);
            }

            // Now safe to load config
            try
            {
                LoadInitialConfig();
            }
            catch (Exception ex)
            {
                await ShowDialogSafeAsync("Initialization Error",
                    $"Failed to load initial configuration: {ex.Message}");
            }
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            _xamlRoot = this.Content.XamlRoot;
            _isWindowReady = true;
            this.Activated -= MainWindow_Activated;
        }

        private async Task ShowDialogSafeAsync(string title, string message)
        {
            try
            {
                // Remove the await from TryEnqueue since it returns bool
                DispatcherQueue.TryEnqueue(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = title,
                        Content = message,
                        CloseButtonText = "OK",
                        XamlRoot = _xamlRoot ?? this.Content?.XamlRoot
                    };

                    if (dialog.XamlRoot == null)
                    {
                        await Task.Delay(100);
                        dialog.XamlRoot = this.Content?.XamlRoot;
                    }

                    if (dialog.XamlRoot != null)
                    {
                        await dialog.ShowAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dialog show failed: {ex.Message}");
            }
        }

        private void LoadInitialConfig()
        {
            LoadConfig(App.BalancedConfigPath);
            DeviceSelector.SelectedIndex = currentConfig.LegionGeneration == 5 ? 0 : 1;
        }

        private void LoadConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    var defaultConfig = GetDefaultConfigContent();
                    File.WriteAllText(configPath, defaultConfig);
                }

                var lines = File.ReadAllLines(configPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                currentConfig = ParseConfig(lines);
                UpdateUI();
            }
            catch (Exception ex)
            {
                _ = ShowDialogSafeAsync("Config Error", $"Failed to load config: {ex.Message}");
                currentConfig = CreateDefaultConfig(); // Fallback to defaults
            }
        }

        private FanConfig ParseConfig(string[] lines)
        {
            return new FanConfig
            {
                LegionGeneration = GetConfigValue(lines, "legion_gen", 5),
                FanCurvePoints = GetConfigValue(lines, "fan_curve_points", 5),
                AccelerationValue = GetConfigValue(lines, "fan_accl_value", 2),
                DecelerationValue = GetConfigValue(lines, "fan_deccl_value", 2),
                FanRpmPoints = GetConfigArray(lines, "fan_rpm_points", new[] { 0, 0, 2200, 3600, 3900 }),
                CpuTempsRampUp = GetConfigArray(lines, "cpu_temps_ramp_up", new[] { 11, 45, 55, 60, 65 }),
                CpuTempsRampDown = GetConfigArray(lines, "cpu_temps_ramp_down", new[] { 10, 43, 53, 58, 63 }),
                GpuTempsRampUp = GetConfigArray(lines, "gpu_temps_ramp_up", new[] { 11, 50, 55, 60, 63 }),
                GpuTempsRampDown = GetConfigArray(lines, "gpu_temps_ramp_down", new[] { 10, 48, 53, 58, 61 }),
                HstTempsRampUp = GetConfigArray(lines, "hst_temps_ramp_up", new[] { 11, 50, 55, 65, 70 }),
                HstTempsRampDown = GetConfigArray(lines, "hst_temps_ramp_down", new[] { 10, 48, 53, 63, 68 })
            };
        }

        private int GetConfigValue(string[] lines, string key, int defaultValue)
        {
            try
            {
                var line = lines.FirstOrDefault(l => l.StartsWith(key));
                if (line != null)
                {
                    var value = line.Split(':')[1].Trim();
                    return int.Parse(value);
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private int[] GetConfigArray(string[] lines, string key, int[] defaultValue)
        {
            try
            {
                var line = lines.FirstOrDefault(l => l.StartsWith(key));
                if (line != null)
                {
                    var values = line.Split(':')[1].Trim()
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return values.Select(int.Parse).ToArray();
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private FanConfig CreateDefaultConfig()
        {
            return new FanConfig
            {
                LegionGeneration = 5,
                FanCurvePoints = 5,
                AccelerationValue = 2,
                DecelerationValue = 2,
                FanRpmPoints = new[] { 0, 0, 2200, 3600, 3900 },
                CpuTempsRampUp = new[] { 11, 45, 55, 60, 65 },
                CpuTempsRampDown = new[] { 10, 43, 53, 58, 63 },
                GpuTempsRampUp = new[] { 11, 50, 55, 60, 63 },
                GpuTempsRampDown = new[] { 10, 48, 53, 58, 61 },
                HstTempsRampUp = new[] { 11, 50, 55, 65, 70 },
                HstTempsRampDown = new[] { 10, 48, 53, 63, 68 }
            };
        }

        private string GetDefaultConfigContent()
        {
            return @"legion_gen : 5
fan_curve_points : 5
fan_accl_value : 2
fan_deccl_value : 2
fan_rpm_points : 0 0 2200 3600 3900
cpu_temps_ramp_up : 11 45 55 60 65
cpu_temps_ramp_down : 10 43 53 58 63
gpu_temps_ramp_up : 11 50 55 60 63
gpu_temps_ramp_down : 10 48 53 58 61
hst_temps_ramp_up : 11 50 55 65 70
hst_temps_ramp_down : 10 48 53 63 68";
        }

        private string GetValue(string[] lines, string key)
        {
            return lines.FirstOrDefault(l => l.StartsWith(key))?.Split(':')[1].Trim();
        }

        private int[] GetIntArray(string value)
        {
            return value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(int.Parse).ToArray();
        }

        private void UpdateUI()
        {
            // Update sliders
            Slider1.Value = currentConfig.FanRpmPoints[0];
            Slider2.Value = currentConfig.FanRpmPoints[1];
            Slider3.Value = currentConfig.FanRpmPoints[2];
            Slider4.Value = currentConfig.FanRpmPoints[3];
            Slider5.Value = currentConfig.FanRpmPoints[4];

            // Update temperature values
            CpuTemp1.Value = currentConfig.CpuTempsRampUp[0];
            CpuTemp2.Value = currentConfig.CpuTempsRampUp[1];
            CpuTemp3.Value = currentConfig.CpuTempsRampUp[2];
            CpuTemp4.Value = currentConfig.CpuTempsRampUp[3];
            CpuTemp5.Value = currentConfig.CpuTempsRampUp[4];

            GpuTemp1.Value = currentConfig.GpuTempsRampUp[0];
            GpuTemp2.Value = currentConfig.GpuTempsRampUp[1];
            GpuTemp3.Value = currentConfig.GpuTempsRampUp[2];
            GpuTemp4.Value = currentConfig.GpuTempsRampUp[3];
            GpuTemp5.Value = currentConfig.GpuTempsRampUp[4];

            // Update advanced settings
            AccVal.Value = currentConfig.AccelerationValue;
            DecVal.Value = currentConfig.DecelerationValue;

            // Update profile name
            ProfileText.Text = $"Profile: \"{currentProfile.ToUpper()}\"";
        }

        private void SaveConfig()
        {
            try
            {
                // Update fan RPMs and acceleration values
                currentConfig.FanRpmPoints = new[] {
            (int)Slider1.Value, (int)Slider2.Value,
            (int)Slider3.Value, (int)Slider4.Value,
            (int)Slider5.Value
        };
                currentConfig.AccelerationValue = (int)AccVal.Value;
                currentConfig.DecelerationValue = (int)DecVal.Value;

                // Update temperature thresholds from UI
                currentConfig.CpuTempsRampUp = new[] {
            (int)CpuTemp1.Value, (int)CpuTemp2.Value,
            (int)CpuTemp3.Value, (int)CpuTemp4.Value,
            (int)CpuTemp5.Value
        };

                currentConfig.GpuTempsRampUp = new[] {
            (int)GpuTemp1.Value, (int)GpuTemp2.Value,
            (int)GpuTemp3.Value, (int)GpuTemp4.Value,
            (int)GpuTemp5.Value
        };

                // Get correct config path
                string configPath = currentProfile switch
                {
                    "performance" => App.PerformanceConfigPath,
                    "quiet" => App.QuietConfigPath,
                    _ => App.BalancedConfigPath
                };

                // Generate config content
                string configContent = GenerateConfigContent();
                File.WriteAllText(configPath, configContent);

                ShowSuccessDialog("Success", "Configuration saved successfully!");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error Saving Config", $"Failed to save configuration: {ex.Message}");
            }
        }
        private string GenerateConfigContent()
        {
            // Auto-calculate ramp_down values (3°C lower than ramp_up)
            int[] cpuRampDown = currentConfig.CpuTempsRampUp.Select(t => Math.Max(0, t - 3)).ToArray();
            int[] gpuRampDown = currentConfig.GpuTempsRampUp.Select(t => Math.Max(0, t - 3)).ToArray();

            return $@"legion_gen : {currentConfig.LegionGeneration}
fan_curve_points : {currentConfig.FanCurvePoints}
fan_accl_value : {currentConfig.AccelerationValue}
fan_deccl_value : {currentConfig.DecelerationValue}
fan_rpm_points : {string.Join(" ", currentConfig.FanRpmPoints)}
cpu_temps_ramp_up : {string.Join(" ", currentConfig.CpuTempsRampUp)}
cpu_temps_ramp_down : {string.Join(" ", cpuRampDown)}
gpu_temps_ramp_up : {string.Join(" ", currentConfig.GpuTempsRampUp)}
gpu_temps_ramp_down : {string.Join(" ", gpuRampDown)}
hst_temps_ramp_up : {string.Join(" ", currentConfig.GpuTempsRampUp)}
hst_temps_ramp_down : {string.Join(" ", gpuRampDown)}";  
}

        private async void ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK"
            };

            if (this.Content != null)
            {
                dialog.XamlRoot = this.Content.XamlRoot;
            }

            _ = await dialog.ShowAsync();
        }

        private async void ShowSuccessDialog(string title, string message)
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                currentProfile = item.Tag.ToString();
                string configPath = currentProfile switch
                {
                    "performance" => App.PerformanceConfigPath,
                    "quiet" => App.QuietConfigPath,
                    _ => App.BalancedConfigPath
                };
                LoadConfig(configPath);
            }
        }

        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            currentConfig.LegionGeneration = DeviceSelector.SelectedIndex == 0 ? 5 : 6;
        }

        private void Save_Click(object sender, RoutedEventArgs e) => SaveConfig();

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("FanControl"))
                    process.Kill();

                Process.Start(new ProcessStartInfo
                {
                    FileName = App.FanControlPath,
                    UseShellExecute = true
                });

                ShowSuccessDialog("Success", "Fan control service restarted!");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error", $"Failed to restart service: {ex.Message}");
            }
        }

        private void Turbo_Click(object sender, RoutedEventArgs e)
        {
            // Set aggressive fan curve
            Slider1.Value = 3000;
            Slider2.Value = 3500;
            Slider3.Value = 4000;
            Slider4.Value = 4200;
            Slider5.Value = 4400;
            AccVal.Value = 3;
            DecVal.Value = 1;
        }

        private void Quiet_Click(object sender, RoutedEventArgs e)
        {
            // Set quiet fan curve
            Slider1.Value = 1000;
            Slider2.Value = 2000;
            Slider3.Value = 2500;
            Slider4.Value = 3000;
            Slider5.Value = 3500;
            AccVal.Value = 1;
            DecVal.Value = 3;
        }
    }
}