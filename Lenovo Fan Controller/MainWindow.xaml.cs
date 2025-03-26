using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
            var width = 850;
            var height = 780;
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
        }
        private async Task InitializeWhenReadyAsync()
        {
            while (!_isWindowReady)
            {
                await Task.Delay(50);
            }

            try
            {
                // Detect current power mode
                var powerMode = PowerModeHelper.GetCurrentPowerMode();
                currentProfile = PowerModeHelper.PowerModeToProfile(powerMode);

                // Load the corresponding config
                string configPath = currentProfile switch
                {
                    "performance" => App.PerformanceConfigPath,
                    "quiet" => App.QuietConfigPath,
                    _ => App.BalancedConfigPath
                };

                LoadConfig(configPath);

                // Update UI to reflect current profile
                SetActiveProfileUI(currentProfile);
            }
            catch (Exception ex)
            {
                await ShowDialogSafeAsync("Initialization Error",
                    $"Failed to detect power mode: {ex.Message}");
                // Fallback to balanced
                LoadConfig(App.BalancedConfigPath);
            }
        }

        private void SetActiveProfileUI(string profile)
        {
            foreach (NavigationViewItem item in NavigationView.MenuItems)
            {
                if (item.Tag.ToString() == profile)
                {
                    NavigationView.SelectedItem = item;
                    break;
                }
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
        private void UpdateDeviceSelector()//
        {
            if (DeviceSelector == null || currentConfig == null) return;

            try
            {
                DeviceSelector.SelectedIndex = currentConfig.LegionGeneration == 5 ? 0 : 1;
                Debug.WriteLine($"Updated DeviceSelector to generation: {currentConfig.LegionGeneration}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating DeviceSelector: {ex.Message}");
            }
        }
        private void LoadConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    File.WriteAllText(configPath, GetDefaultConfigContent());
                }

                var lines = File.ReadAllLines(configPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                currentConfig = ParseConfig(lines);
                UpdateUI();

                UpdateDeviceSelector();
            }
            catch (Exception ex)
            {
                _ = ShowDialogSafeAsync("Config Error", $"Failed to load config: {ex.Message}");
                currentConfig = CreateDefaultConfig();
                UpdateDeviceSelector(); 
            }
        }

        private FanConfig ParseConfig(string[] lines)
        {
            
            return new FanConfig
              
            {
                //TODO: Check if Legion Gen 5 or 6th
                //      Check if sys has info about max rpm for fans to automatically set the max.
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
            Slider1.Value = currentConfig.FanRpmPoints[0];
            Slider2.Value = currentConfig.FanRpmPoints[1];
            Slider3.Value = currentConfig.FanRpmPoints[2];
            Slider4.Value = currentConfig.FanRpmPoints[3];
            Slider5.Value = currentConfig.FanRpmPoints[4];

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

            AccVal.Value = currentConfig.AccelerationValue;
            DecVal.Value = currentConfig.DecelerationValue;
            UpdateDeviceSelector();

            ProfileText.Text = $"Profile: \"{currentProfile.ToUpper()}\"";
        }

        private void SaveConfig()
        {
            try
            {
                currentConfig.FanCurvePoints = 5;
                // Update fan RPMs and acceleration values
                currentConfig.FanRpmPoints = new[] {
            (int)Slider1.Value, (int)Slider2.Value,
            (int)Slider3.Value, (int)Slider4.Value,
            (int)Slider5.Value
        };
                currentConfig.AccelerationValue = (int)AccVal.Value;
                currentConfig.DecelerationValue = (int)DecVal.Value;

                // Update from UI °
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

                // Config path
                string configPath = currentProfile switch
                {
                    "performance" => App.PerformanceConfigPath,
                    "quiet" => App.QuietConfigPath,
                    _ => App.BalancedConfigPath
                };

                // Generate config
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
            // Auto-calc 3°C lower than ramp_up
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
        private void RestartFanControl()
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("FanControl"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error killing process: {ex.Message}");
                    }
                }
                var startInfo = new ProcessStartInfo
                {
                    FileName = App.FanControlPath,
                    UseShellExecute = false,  
                    CreateNoWindow = true,   
                    WindowStyle = ProcessWindowStyle.Hidden 
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FanControl restart failed: {ex.Message}");
              
                ShowErrorDialog("Fan Control Error", $"Failed to restart fan control: {ex.Message}");
            }
        
        }
        private async void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                string newProfile = selectedItem.Tag.ToString();
                var powerMode = newProfile switch
                {
                    "quiet" => PowerModeHelper.LegionPowerMode.Quiet,
                    "performance" => PowerModeHelper.LegionPowerMode.Performance,
                    _ => PowerModeHelper.LegionPowerMode.Balanced
                };

                SetLoadingState(true);

                try
                {
                    bool success = PowerModeHelper.SetPowerMode(powerMode);

                    if (!success)
                    {
                        await ShowDialogSafeAsync("Error",
                            "Failed to change power mode. Please ensure:\n" +
                            "1. You're running as Administrator\n" +
                            "2. Lenovo Vantage/WMI services are running\n" +
                            "3. Your model supports power mode switching");

                        // Revert UI to current actual mode
                        var actualMode = PowerModeHelper.GetCurrentPowerMode();
                        string actualProfile = PowerModeHelper.PowerModeToProfile(actualMode);
                        sender.SelectedItem = NavigationView.MenuItems
                            .OfType<NavigationViewItem>()
                            .FirstOrDefault(item => item.Tag.ToString() == actualProfile);
                        return;
                    }

                    currentProfile = newProfile;
                    string configPath = newProfile switch
                    {
                        "performance" => App.PerformanceConfigPath,
                        "quiet" => App.QuietConfigPath,
                        _ => App.BalancedConfigPath
                    };

                    LoadConfig(configPath);
                    RestartFanControl();
                }
                finally
                {
                    SetLoadingState(false);
                }
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            // Visual feedback during mode change
            ProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            NavigationView.IsEnabled = !isLoading;
        }

        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceSelector.SelectedItem is ComboBoxItem selectedItem && currentConfig != null)
            {
                currentConfig.LegionGeneration = selectedItem.Content.ToString().Contains("5th") ? 5 : 6;
                Debug.WriteLine($"User selected Legion Generation: {currentConfig.LegionGeneration}");

            }
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

        //TURBO
        private bool turboEnabled = false;
        private int[] originalRpmValues = new int[5];

        private void Turbo_Checked(object sender, RoutedEventArgs e)
        {
            originalRpmValues[0] = (int)Slider1.Value;
            originalRpmValues[1] = (int)Slider2.Value;
            originalRpmValues[2] = (int)Slider3.Value;
            originalRpmValues[3] = (int)Slider4.Value;
            originalRpmValues[4] = (int)Slider5.Value;

            // Set all fans to max RPM (4400) LEGION 5 15ANH05
            // I cant find tech information about the max rpm of 5th and 6th gen, thanks lenovo
            Slider1.Value = 4400;
            Slider2.Value = 4400;
            Slider3.Value = 4400;
            Slider4.Value = 4400;
            Slider5.Value = 4400;

            turboEnabled = true;
            Turbo.Background = new SolidColorBrush(Colors.Red);
            SaveConfig();
        }

        private void Turbo_Unchecked(object sender, RoutedEventArgs e)
        {
            // Restore
            Slider1.Value = originalRpmValues[0];
            Slider2.Value = originalRpmValues[1];
            Slider3.Value = originalRpmValues[2];
            Slider4.Value = originalRpmValues[3];
            Slider5.Value = originalRpmValues[4];

            turboEnabled = false;
            Turbo.Background = new SolidColorBrush(Colors.Transparent);
            SaveConfig();
        }
    }
}