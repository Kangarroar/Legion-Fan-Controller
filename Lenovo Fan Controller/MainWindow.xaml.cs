using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using WinRT.Interop;
using H.NotifyIcon;
using LegionFanController.Hardware;

namespace Lenovo_Fan_Controller
{
    public sealed partial class MainWindow : Window
    {
        // I CANT FOR THE LIFE OF ME FIGURE OUT WHY THE DEFAULT BUTTON IS NOT WORKING
        // IT KEEPS BEING HIGHLIGHTED ALWAYS
        private FanConfig currentConfig;
        private string currentProfile = "default";
        private XamlRoot _xamlRoot;
        private bool _isWindowReady = false;
        private AppWindow _appWindow;
        private bool _isWindowVisible = true;
        private bool _isExiting = false;
        private bool _shouldStartMinimized = false;

        // left click tray
        public ICommand ShowHideWindowCommand { get; }

        public MainWindow()
        {
            // Initialize command
            ShowHideWindowCommand = new RelayCommand(ShowHideWindow);

            this.InitializeComponent();
            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Closed;
            SetWindowProperties();
            SetupEventHandlers();
            CheckStartupStatus();

            // Start minimized
            CheckStartMinimized();


            _ = InitializeWhenReadyAsync();
        }

        private void CheckStartMinimized()
        {
            var args = Environment.GetCommandLineArgs();

            if (args.Length > 1 && args[1].Equals("/minimized", StringComparison.OrdinalIgnoreCase))
            {
                _shouldStartMinimized = true;
            }
            else if (SettingsManager.GetStartMinimized())
            {
                _shouldStartMinimized = true;
            }

            if (_shouldStartMinimized)
            {
                _ = HideWindowAfterReadyAsync();
            }
        }

        private async Task HideWindowAfterReadyAsync()
        {
            // Hide immediately 
            HideWindow();
            while (!_isWindowReady)
            {
                await Task.Delay(50);
            }

            await Task.Delay(100);
            HideWindow();
        }

        private void CheckStartupStatus()
        {
            StartupMenuItem.IsChecked = StartupManager.IsStartupEnabled();
        }
        // Hijack close event
        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_isExiting)
            {
                // Force kill FanControl
                try
                {
                    foreach (var process in Process.GetProcessesByName("FanControl"))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                        catch { }
                    }
                }
                catch { }

                TrayIcon?.Dispose();
                return;
            }

            args.Handled = true;
            HideWindow();
        }

        private void SetWindowProperties()
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var width = 850;
            var height = 780;
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                (displayArea.WorkArea.Width - width) / 2,
                (displayArea.WorkArea.Height - height) / 2,
                width,
                height));
            _appWindow.Changed += AppWindow_Changed;
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPresenterChange && _appWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                return;
            }
            if (args.DidSizeChange || args.DidPresenterChange)
            {
                // WinUI doesn't have a direct "IsMinimized" property
            }
        }

        private void ContextMenu_Opening(object sender, object e)
        {
            // Update menu items to reflect current state
            CheckStartupStatus();
            ShowHideMenuItem.Text = _isWindowVisible ? "Hide Window" : "Show Window";
        }

        private void ShowHideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowHideWindow();
        }

        private async void StartupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success;
                if (StartupMenuItem.IsChecked)
                {
                    // Enable startup
                    success = StartupManager.EnableStartup();
                    if (success)
                    {
                        await ShowDialogSafeAsync("Startup Enabled",
                            "Legion Fan Controller will now start automatically when Windows boots.\n\n" +
                            "You can disable this anytime from the tray icon menu or Task Manager's Startup tab.");
                    }
                    else
                    {
                        StartupMenuItem.IsChecked = false;
                        await ShowDialogSafeAsync("Error",
                            "Failed to enable startup. Please make sure the application is running as Administrator.");
                    }
                }
                else
                {
                    // Disable startup
                    success = StartupManager.DisableStartup();
                    if (success)
                    {
                        await ShowDialogSafeAsync("Startup Disabled",
                            "Legion Fan Controller will no longer start automatically with Windows.");
                    }
                    else
                    {
                        StartupMenuItem.IsChecked = true;
                        await ShowDialogSafeAsync("Error",
                            "Failed to disable startup. Please make sure the application is running as Administrator.");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowDialogSafeAsync("Error", $"Failed to change startup setting: {ex.Message}");
                CheckStartupStatus(); // Revert to actual state
            }
        }

        private async void ResetFirstRunMenuItem_Click(object sender, RoutedEventArgs e)
        {
            FirstRunHelper.ResetFirstRun();
            await ShowDialogSafeAsync("First Run Reset",
                "Set");
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _isExiting = true;
            this.Close();
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowSettingsDialog();
        }

        private async Task ShowSettingsDialog()
        {
            // Create checkboxes for settings
            var showGpuCheckBox = new CheckBox
            {
                Content = "Show GPU Temperature Controls",
                IsChecked = SettingsManager.GetShowGpuTemp(),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var startMinimizedCheckBox = new CheckBox
            {
                Content = "Start Minimized to Tray",
                IsChecked = SettingsManager.GetStartMinimized(),
                Margin = new Thickness(0, 0, 0, 12),
                Opacity = 1
            };

            var unlockMaxRpmCheckBox = new CheckBox
            {
                Content = "Unlock Max RPM (5000 RPM - DANGEROUS!)",
                IsChecked = SettingsManager.GetUnlockMaxRpm(),
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
            };

            var warningText = new TextBlock
            {
                Text = "⚠️ Warning: Setting RPM values too high can damage your fans!",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.7,
                Margin = new Thickness(24, 0, 0, 0)
            };

            var stackPanel = new StackPanel
            {
                Spacing = 8
            };

            stackPanel.Children.Add(showGpuCheckBox);
            stackPanel.Children.Add(startMinimizedCheckBox);
            stackPanel.Children.Add(unlockMaxRpmCheckBox);
            stackPanel.Children.Add(warningText);

            var dialog = new ContentDialog
            {
                Title = "Settings",
                Content = stackPanel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content?.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                bool showGpuChanged = SettingsManager.GetShowGpuTemp() != showGpuCheckBox.IsChecked;
                bool maxRpmChanged = SettingsManager.GetUnlockMaxRpm() != unlockMaxRpmCheckBox.IsChecked;
                bool startMinimizedChanged = SettingsManager.GetStartMinimized() != startMinimizedCheckBox.IsChecked;

                // Save settings
                SettingsManager.SetShowGpuTemp(showGpuCheckBox.IsChecked == true);
                SettingsManager.SetStartMinimized(startMinimizedCheckBox.IsChecked == true);
                SettingsManager.SetUnlockMaxRpm(unlockMaxRpmCheckBox.IsChecked == true);

                // Apply settings immediately
                if (showGpuChanged)
                {
                    ApplyGpuVisibilitySetting();
                }

                if (maxRpmChanged)
                {
                    ApplyMaxRpmSetting();
                }

                // Update Task Scheduler if startup setting changed
                if (startMinimizedChanged)
                {
                    try
                    {
                        if (StartupManager.IsStartupEnabled())
                        {
                            StartupManager.DisableStartup();
                            StartupManager.EnableStartup();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to update Task Scheduler: {ex.Message}");
                    }
                }

                await ShowDialogSafeAsync("Settings Saved", "Your settings have been saved successfully.");
            }
        }

        private void ApplyGpuVisibilitySetting()
        {
            bool showGpu = SettingsManager.GetShowGpuTemp();
            var visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;

            GPU1.Visibility = visibility;
            GpuTemp1.Visibility = visibility;
            GPU2.Visibility = visibility;
            GpuTemp2.Visibility = visibility;
            GPU3.Visibility = visibility;
            GpuTemp3.Visibility = visibility;
            GPU4.Visibility = visibility;
            GpuTemp4.Visibility = visibility;
            GPU5.Visibility = visibility;
            GpuTemp5.Visibility = visibility;
        }

        private void ApplyMaxRpmSetting()
        {
            int maxRpm = SettingsManager.GetMaxRpm();

            Slider1.Maximum = maxRpm;
            Slider2.Maximum = maxRpm;
            Slider3.Maximum = maxRpm;
            Slider4.Maximum = maxRpm;
            Slider5.Maximum = maxRpm;
        }

        private void ShowHideWindow()
        {
            if (_isWindowVisible)
            {
                HideWindow();
            }
            else
            {
                ShowWindow();
            }
        }

        private void HideWindow()
        {
            _isWindowVisible = false;
            ShowHideMenuItem.Text = "Show Window";

            var hWnd = WindowNative.GetWindowHandle(this);
            User32.ShowWindow(hWnd, User32.SW_HIDE);
        }

        private void ShowWindow()
        {
            _isWindowVisible = true;
            ShowHideMenuItem.Text = "Hide Window";

            var hWnd = WindowNative.GetWindowHandle(this);
            User32.ShowWindow(hWnd, User32.SW_SHOW);
            this.Activate();
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

            await FirstRunHelper.ShowFirstRunWarning(this);

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
                RestartFanControl();

                // Update UI to reflect current profile
                SetActiveProfileUI(currentProfile);

                // Apply saved settings
                ApplyGpuVisibilitySetting();
                ApplyMaxRpmSetting();
            }
            catch (Exception ex)
            {
                await ShowDialogSafeAsync("Initialization Error",
                    $"Failed to detect power mode: {ex.Message}");
                // Fallback to balanced
                LoadConfig(App.BalancedConfigPath);
                RestartFanControl();
            }

            // Initialize Hardware Monitoring
            if (ECUtils.Init())
            {
                System.Diagnostics.Debug.WriteLine("ECUtils.Init() returned true in MainWindow");
                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += (s, e) =>
                {
                    try
                    {
                        int cpuTemp = ECUtils.ReadCpuTemp();
                        int gpuTemp = ECUtils.ReadGpuTemp();
                        int cpuFan = ECUtils.ReadFan1Rpm();
                        int gpuFan = ECUtils.ReadFan2Rpm();

                        System.Diagnostics.Debug.WriteLine($"Tick: CPU={cpuTemp}, GPU={gpuTemp}, Fan1={cpuFan}, Fan2={gpuFan}");

                        MonitorCpuTemp.Text = $"{cpuTemp} °C";
                        MonitorGpuTemp.Text = $"{gpuTemp} °C";
                        MonitorCpuFan.Text = $"{cpuFan} RPM";
                        MonitorGpuFan.Text = $"{gpuFan} RPM";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Monitoring Tick Error: {ex.Message}");
                    }
                };
                timer.Start();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ECUtils.Init() returned false in MainWindow");
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

            // If we should start minimized, hide the window again
            if (_shouldStartMinimized)
            {
                HideWindow();
            }
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
            }
            catch (Exception ex)
            {
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
                FanRpmPoints = GetConfigArray(lines, "fan_rpm_points", new[] { 0, 1500, 2200, 3600, 3900 }),
                CpuTempsRampUp = GetConfigArray(lines, "cpu_temps_ramp_up", new[] { 30, 45, 55, 60, 65 }),
                CpuTempsRampDown = GetConfigArray(lines, "cpu_temps_ramp_down", new[] { 28, 43, 53, 58, 63 }),
                GpuTempsRampUp = GetConfigArray(lines, "gpu_temps_ramp_up", new[] { 30, 50, 55, 60, 63 }),
                GpuTempsRampDown = GetConfigArray(lines, "gpu_temps_ramp_down", new[] { 28, 48, 53, 58, 61 }),
                HstTempsRampUp = GetConfigArray(lines, "hst_temps_ramp_up", new[] { 30, 50, 55, 65, 70 }),
                HstTempsRampDown = GetConfigArray(lines, "hst_temps_ramp_down", new[] { 28, 48, 53, 63, 68 })
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
                FanRpmPoints = new[] { 0, 1500, 2200, 3600, 3900 },
                CpuTempsRampUp = new[] { 30, 45, 55, 60, 65 },
                CpuTempsRampDown = new[] { 28, 43, 53, 58, 63 },
                GpuTempsRampUp = new[] { 30, 50, 55, 60, 63 },
                GpuTempsRampDown = new[] { 28, 48, 53, 58, 61 },
                HstTempsRampUp = new[] { 30, 50, 55, 65, 70 },
                HstTempsRampDown = new[] { 28, 48, 53, 63, 68 }
            };
        }

        private string GetDefaultConfigContent()
        {
            return @"legion_gen : 5
fan_curve_points : 5
fan_accl_value : 2
fan_deccl_value : 2
fan_rpm_points : 0 1500 2200 3600 3900
cpu_temps_ramp_up : 30 45 55 60 65
cpu_temps_ramp_down : 28 43 53 58 63
gpu_temps_ramp_up : 30 50 55 60 63
gpu_temps_ramp_down : 28 48 53 58 61
hst_temps_ramp_up : 30 50 55 65 70
hst_temps_ramp_down : 28 48 53 63 68";
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

                // Update from UI �
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
            // Auto-calc 3�C lower than ramp_up
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

            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) => SaveConfig();

        private void Restart_Click(object sender, RoutedEventArgs e)
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
                    catch { }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = App.FanControlPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                ShowSuccessDialog("Success", "Fan control service restarted!");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error", $"Failed to restart service: {ex.Message}");
            }
        }

        ////TURBO
        /// Gonna implement this later.
        /// Alternative is Settings > Enable 5000RPM > Modify the profile to use 5000RPM
        /// 
        //private bool turboEnabled = false;
        //private int[] originalRpmValues = new int[5];
        //
        //private void Turbo_Checked(object sender, RoutedEventArgs e)
        //{
        //    originalRpmValues[0] = (int)Slider1.Value;
        //    originalRpmValues[1] = (int)Slider2.Value;
        //    originalRpmValues[2] = (int)Slider3.Value;
        //    originalRpmValues[3] = (int)Slider4.Value;
        //    originalRpmValues[4] = (int)Slider5.Value;
        //
        //    // Set all fans to max RPM (4400) LEGION 5 15ANH05
        //    // I cant find tech information about the max rpm of 5th and 6th gen, thanks lenovo
        //    Slider1.Value = 4400;
        //    Slider2.Value = 4400;
        //    Slider3.Value = 4400;
        //    Slider4.Value = 4400;
        //    Slider5.Value = 4400;
        //
        //    turboEnabled = true;
        //    Turbo.Background = new SolidColorBrush(Colors.Red);
        //    SaveConfig();
        //}
        //
        //private void Turbo_Unchecked(object sender, RoutedEventArgs e)
        //{
        //    // Restore
        //    Slider1.Value = originalRpmValues[0];
        //    Slider2.Value = originalRpmValues[1];
        //    Slider3.Value = originalRpmValues[2];
        //    Slider4.Value = originalRpmValues[3];
        //    Slider5.Value = originalRpmValues[4];
        //
        //    turboEnabled = false;
        //    Turbo.Background = new SolidColorBrush(Colors.Transparent);
        //    SaveConfig();
        //}
    }

    // Helper class for window show/hide operations
    internal static class User32
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }

    // Simple relay command implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object? parameter)
        {
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}