using H.NotifyIcon;
using LegionFanController.Hardware;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using WinRT.Interop;
using Path = System.IO.Path;

namespace Lenovo_Fan_Controller
{
    public sealed partial class MainWindow : Window
    {
        // Configuration
        private FanConfig currentConfig;
        private string currentProfile = "balance";
        private int legionGeneration;

        // Window state
        private XamlRoot _xamlRoot;
        private bool _isWindowReady = false;
        private AppWindow _appWindow;
        private bool _isWindowVisible = true;
        private bool _isExiting = false;
        private bool _shouldStartMinimized = false;
        private bool _isInternalResize = false;
        private int _lastWidth = 1000;
        private int _lastHeight = 800;
        private bool _isReinitializing = false;
        private DispatcherTimer _monitoringTimer;

        private IntPtr _hwnd;
        private IntPtr _oldWndProc;
        private WndProcDelegate _wndProcDelegate;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const uint WM_SIZE = 0x0005;
        private const int SIZE_MINIMIZED = 1;

        // Fan curve data points 
        private List<CurvePoint> cpuCurvePoints;
        private List<CurvePoint> gpuCurvePoints;
        private string currentEditingCurve = "cpu"; // "cpu" or "gpu"

        // Graph constants
        private const double GRAPH_MARGIN = 40;
        private const int MIN_TEMP = 0;
        private const int MAX_TEMP = 100;
        private const int MIN_RPM = 0;
        private int MAX_RPM = 4500;

        // Dragging state
        private CurvePoint draggedPoint;
        private bool isDraggingCpu = false;
        private bool isDragging = false;
        private Windows.Foundation.Point pressedPosition;

        // Left click tray
        public ICommand ShowHideWindowCommand { get; }

        public MainWindow()
        {
            MAX_RPM = SettingsManager.GetMaxRpm();
            ShowHideWindowCommand = new RelayCommand(ShowHideWindow);

            this.InitializeComponent();
            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Closed;

            SetWindowProperties();
            SetupEventHandlers();
            CheckStartupStatus();

            // Initialize curve points (default 5 points)
            InitializeCurvePoints(5);

            // Start minimized
            CheckStartMinimized();

            _ = InitializeWhenReadyAsync();
            UpdatePointButtonsState();
        }

        private void InitializeCurvePoints(int count)
        {
            cpuCurvePoints = new List<CurvePoint>();
            gpuCurvePoints = new List<CurvePoint>();

            // Create default evenly spaced points
            for (int i = 0; i < count; i++)
            {
                double tempPercent = (double)i / (count - 1);
                int temp = (int)(MIN_TEMP + (MAX_TEMP - MIN_TEMP) * tempPercent);
                int rpm = (int)(MIN_RPM + (MAX_RPM - MIN_RPM) * tempPercent);

                cpuCurvePoints.Add(new CurvePoint { Temp = temp, Rpm = rpm });
                gpuCurvePoints.Add(new CurvePoint { Temp = temp, Rpm = rpm });
            }

            UpdatePointCountText();
        }

        private void UpdatePointCountText()
        {
            if (PointCountText != null)
            {
                PointCountText.Text = $"Current: {cpuCurvePoints.Count} points";
            }
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

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _monitoringTimer?.Stop();
            if (_oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldWndProc);
            }

            if (_isExiting)
            {
                try
                {
                    ECUtils.Cleanup();
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
            var width = 1000;
            var height = 800;
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                (displayArea.WorkArea.Width - width) / 2,
                (displayArea.WorkArea.Height - height) / 2,
                width,
                height));

            _lastWidth = width;
            _lastHeight = height;

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            if (_appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.IsMaximizable = true;
                overlapped.IsMinimizable = true;
                overlapped.IsResizable = true;
            }

            _hwnd = hWnd;
            _wndProcDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            _appWindow.Changed += AppWindow_Changed;
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SYSCOMMAND && (int)wParam == SC_MINIMIZE)
            {
                Debug.WriteLine("[WndProc] SC_MINIMIZE - calling HideWindow()");
                HideWindow();
                return IntPtr.Zero;
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange && !_isInternalResize)
            {
                if (_appWindow.Presenter is OverlappedPresenter overlapped &&
                    overlapped.State == OverlappedPresenterState.Maximized)
                {
                    return;
                }

                var size = _appWindow.Size;
                int newWidth = size.Width;
                int newHeight = size.Height;

                if (newWidth == _lastWidth && newHeight == _lastHeight) return;

                bool needsResize = false;
                const double targetRatio = 1.25; // 1000 / 800

                // Maintain aspect ratio
                if (!SettingsManager.GetAllowResizing())
                {
                    // If resizing is locked, force back to default 1000x800
                    if (newWidth != 1000 || newHeight != 800)
                    {
                        newWidth = 1000;
                        newHeight = 800;
                        needsResize = true;
                    }
                }
                else if (newWidth != _lastWidth)
                {
                    newHeight = (int)(newWidth / targetRatio);
                    needsResize = true;
                }
                else if (newHeight != _lastHeight)
                {
                    newWidth = (int)(newHeight * targetRatio);
                    needsResize = true;
                }

                // Enforce Min Size
                if (newWidth < 800)
                {
                    newWidth = 800;
                    newHeight = (int)(newWidth / targetRatio);
                    needsResize = true;
                }
                if (newHeight < 640)
                {
                    newHeight = 640;
                    newWidth = (int)(newHeight * targetRatio);
                    needsResize = true;
                }

                // Enforce Max Size
                if (newWidth > 1000)
                {
                    newWidth = 1000;
                    newHeight = (int)(newWidth / targetRatio);
                    needsResize = true;
                }
                if (newHeight > 800)
                {
                    newHeight = 800;
                    newWidth = (int)(newHeight * targetRatio);
                    needsResize = true;
                }

                if (needsResize)
                {
                    _isInternalResize = true;
                    _appWindow.Resize(new Windows.Graphics.SizeInt32(newWidth, newHeight));
                    _isInternalResize = false;

                    _lastWidth = newWidth;
                    _lastHeight = newHeight;
                    return;
                }

                _lastWidth = newWidth;
                _lastHeight = newHeight;
            }

            if (args.DidPresenterChange && _appWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                return;
            }
        }

        private void ContextMenu_Opening(object sender, object e)
        {
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
                CheckStartupStatus();
            }
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
            var startMinimizedCheckBox = new CheckBox
            {
                Content = "Start Minimized to Tray",
                IsChecked = SettingsManager.GetStartMinimized(),
                Margin = new Thickness(0, 0, 0, 12),
                Opacity = 1
            };

            var unlockMaxRpmCheckBox = new CheckBox
            {
                Content = "Unlock Max RPM ⚠️",
                IsChecked = SettingsManager.GetUnlockMaxRpm(),
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
            };


            var lockPointsCheckBox = new CheckBox
            {
                Content = "Lock Fan Curve Points (Prevent Add/Remove)",
                IsChecked = SettingsManager.GetLockPoints(),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var enableSafeguardsCheckBox = new CheckBox
            {
                Content = "Enable Safeguards",
                IsChecked = SettingsManager.GetEnableSafeguards(),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var allowResizingCheckBox = new CheckBox
            {
                Content = "Allow Window Resizing",
                IsChecked = SettingsManager.GetAllowResizing(),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var hysteresisLabel = new TextBlock
            {
                Text = "Temperature Hysteresis (Ramp Down Offset):",
                Margin = new Thickness(0, 8, 0, 4),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };

            var hysteresisBox = new NumberBox
            {
                Value = currentConfig.Hysteresis == 0 ? 3 : currentConfig.Hysteresis,
                Minimum = 1,
                Maximum = 8,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Header = "Offset in °C (Default is 3)"
            };

            var stackPanel = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(4)
            };

            stackPanel.Children.Add(startMinimizedCheckBox);
            stackPanel.Children.Add(lockPointsCheckBox);
            stackPanel.Children.Add(enableSafeguardsCheckBox);
            stackPanel.Children.Add(allowResizingCheckBox);
            stackPanel.Children.Add(hysteresisLabel);
            stackPanel.Children.Add(hysteresisBox);
            stackPanel.Children.Add(new MenuFlyoutSeparator { Margin = new Thickness(0, 12, 0, 12) });
            stackPanel.Children.Add(unlockMaxRpmCheckBox);

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
                bool maxRpmChanged = SettingsManager.GetUnlockMaxRpm() != (unlockMaxRpmCheckBox.IsChecked == true);
                bool startMinimizedChanged = SettingsManager.GetStartMinimized() != (startMinimizedCheckBox.IsChecked == true);

                SettingsManager.SetStartMinimized(startMinimizedCheckBox.IsChecked == true);
                SettingsManager.SetUnlockMaxRpm(unlockMaxRpmCheckBox.IsChecked == true);
                SettingsManager.SetLockPoints(lockPointsCheckBox.IsChecked == true);
                SettingsManager.SetEnableSafeguards(enableSafeguardsCheckBox.IsChecked == true);
                SettingsManager.SetAllowResizing(allowResizingCheckBox.IsChecked == true);

                if (allowResizingCheckBox.IsChecked != true)
                {
                    if (_appWindow.Presenter is OverlappedPresenter overlapped && overlapped.State != OverlappedPresenterState.Maximized)
                    {
                        _isInternalResize = true;
                        _appWindow.Resize(new Windows.Graphics.SizeInt32(1000, 800));
                        _isInternalResize = false;
                        _lastWidth = 1000;
                        _lastHeight = 800;
                    }
                }

                currentConfig.Hysteresis = (int)hysteresisBox.Value;

                if (maxRpmChanged)
                {
                    MAX_RPM = SettingsManager.GetMaxRpm();

                    // Clamp existing points MAX_RPM
                    foreach (var p in cpuCurvePoints)
                    {
                        if (p.Rpm > MAX_RPM) p.Rpm = MAX_RPM;
                    }
                    foreach (var p in gpuCurvePoints)
                    {
                        if (p.Rpm > MAX_RPM) p.Rpm = MAX_RPM;
                    }
                }

                // Update button states
                UpdatePointButtonsState();

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

                CheckStartupStatus();
                DrawFanCurve();
                SaveConfig();
            }
        }

        private void UpdatePointButtonsState()
        {
            bool locked = SettingsManager.GetLockPoints();
            if (AddPointBtn != null) AddPointBtn.IsEnabled = !locked;
            if (RemovePointBtn != null) RemovePointBtn.IsEnabled = !locked;
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
            _monitoringTimer?.Stop();
            ShowHideMenuItem.Text = "Show Window";

            var hWnd = WindowNative.GetWindowHandle(this);
            User32.ShowWindow(hWnd, User32.SW_HIDE);
        }

        private void ShowWindow()
        {
            _isWindowVisible = true;
            ShowHideMenuItem.Text = "Hide Window";

            var hWnd = WindowNative.GetWindowHandle(this);
            IntPtr currentWndProc = GetWindowLongPtr(hWnd, GWLP_WNDPROC);

            if (currentWndProc != Marshal.GetFunctionPointerForDelegate(_wndProcDelegate))
            {
                Debug.WriteLine("WndProc lost, resetting...");
                _oldWndProc = SetWindowLongPtr(hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            }
            User32.ShowWindow(hWnd, User32.SW_SHOW);
            User32.SetForegroundWindow(hWnd);
            this.Activate();
            _monitoringTimer?.Start();
        }

        private void SetupEventHandlers()
        {
            // Profile buttons
            DefaultBtn.Click += ProfileButton_Click;
            PerformanceBtn.Click += ProfileButton_Click;
            QuietBtn.Click += ProfileButton_Click;

            // Other buttons
            DeviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;
            Save.Click += Save_Click;
            Restart.Click += Restart_Click;

            // Point management
            AddPointBtn.Click += AddPoint_Click;
            RemovePointBtn.Click += RemovePoint_Click;

            // Canvas events
            FanCurveCanvas.SizeChanged += FanCurveCanvas_SizeChanged;
            FanCurveCanvas.PointerPressed += FanCurveCanvas_PointerPressed;
            FanCurveCanvas.PointerMoved += FanCurveCanvas_PointerMoved;
            FanCurveCanvas.PointerReleased += FanCurveCanvas_PointerReleased;
            FanCurveCanvas.PointerCanceled += (s, e) => ResetDragState();
            FanCurveCanvas.PointerCaptureLost += (s, e) => ResetDragState();
        }

        private async Task ShowPawnIOInstallDialog()
        {
            while (this.Content?.XamlRoot == null)
            {
                await Task.Delay(1000);
            }

            var stackPanel = new StackPanel { Spacing = 12 };

            stackPanel.Children.Add(new TextBlock
            {
                Text = "PawnIO driver is not installed or failed to initialize.\n\n" +
                       "Would you like to install it automatically?",
                TextWrapping = TextWrapping.Wrap
            });

            var infoPanel = new StackPanel { Spacing = 4 };
            infoPanel.Children.Add(new TextBlock
            {
                Text = "Automatic install will:",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            infoPanel.Children.Add(new TextBlock { Text = "• Download & Run PawnIO Setup (pawnio.eu)" });
            infoPanel.Children.Add(new TextBlock { Text = "• Download & Extract LpcIO.bin module (GitHub)" });
            infoPanel.Children.Add(new TextBlock { Text = "• Place module in C:\\Program Files\\PawnIO\\" });
            stackPanel.Children.Add(infoPanel);

            var dialog = new ContentDialog
            {
                Title = "PawnIO Driver Required",
                Content = stackPanel,
                PrimaryButtonText = "Automatic Install",
                SecondaryButtonText = "Manual Steps",
                CloseButtonText = "Exit",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content?.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // tf???
                await Task.Delay(500);
                await RunAutomaticPawnIOInstall();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await Task.Delay(500);
                await ShowManualPawnIOInstallDialog();
            }
            else
            {
                _isExiting = true;
                this.Close();
            }
        }

        private async Task RunAutomaticPawnIOInstall()
        {
            var statusText = new TextBlock
            {
                Text = "Preparing installation...",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            var progressBar = new ProgressBar { IsIndeterminate = true };
            var stackPanel = new StackPanel { Spacing = 8, Padding = new Thickness(0, 8, 0, 0) };
            stackPanel.Children.Add(statusText);
            stackPanel.Children.Add(progressBar);

            var progressDialog = new ContentDialog
            {
                Title = "Installing PawnIO",
                Content = stackPanel,
                XamlRoot = this.Content?.XamlRoot
            };

            var showTask = progressDialog.ShowAsync();

            string lastErrorMessage = "";
            bool success = await PawnIOInstaller.InstallAsync(msg =>
            {
                if (msg.StartsWith("Error:")) lastErrorMessage = msg;
                DispatcherQueue.TryEnqueue(() => statusText.Text = msg);
            });

            progressDialog.Hide();
            // Wait for showTask to complete after Hide()
            await showTask;

            if (success)
            {
                await Task.Delay(500);
                await ShowDialogSafeAsync("Success", "PawnIO installed successfully. Retrying initialization...");
                _isReinitializing = true;
                await InitializeWhenReadyAsync();
                _isReinitializing = false;
            }
            else
            {
                await Task.Delay(500);
                await ShowDialogSafeAsync("Installation Failed",
                    $"Automatic installation failed.\n\n{lastErrorMessage}\n\nPlease try manual installation.");
                await Task.Delay(500);
                await ShowManualPawnIOInstallDialog();
            }
        }

        private async Task ShowManualPawnIOInstallDialog()
        {
            var stackPanel = new StackPanel { Spacing = 8 };

            stackPanel.Children.Add(new TextBlock
            {
                Text = "PawnIO driver is not installed or failed to initialize.\n\n" +
                       "Please follow these steps:",
                TextWrapping = TextWrapping.Wrap
            });

            var installPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };

            installPanel.Children.Add(new HyperlinkButton
            {
                Content = "Download PawnIO.Setup from pawnio.eu",
                NavigateUri = new Uri("https://pawnio.eu")
            });

            installPanel.Children.Add(new HyperlinkButton
            {
                Content = "Download from Github (alternative)",
                NavigateUri = new Uri("https://github.com/namazso/PawnIO.Setup/releases")
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "1. Install PawnIO driver:",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            stackPanel.Children.Add(installPanel);

            stackPanel.Children.Add(new TextBlock
            {
                Text = "2. Download modules:",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 0)
            });
            stackPanel.Children.Add(new HyperlinkButton
            {
                Content = "Download PawnIO.Modules from Github",
                NavigateUri = new Uri("https://github.com/namazso/PawnIO.Modules/releases"),
                Margin = new Thickness(0, 4, 0, 0)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "3. Extract LpcIO.bin to:",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 4)
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = "C:\\Program Files\\PawnIO\\LpcIO.bin",
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "⚠️ Run as Administrator",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
                Margin = new Thickness(0, 16, 0, 0)
            });

            var dialog = new ContentDialog
            {
                Title = "Manual Install Steps",
                Content = stackPanel,
                PrimaryButtonText = "Retry",
                CloseButtonText = "Exit",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content?.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                _isReinitializing = true;
                await InitializeWhenReadyAsync();
                _isReinitializing = false;
            }
            else
            {
                _isExiting = true;
                this.Close();
            }
        }

        private async Task InitializeWhenReadyAsync()
        {
            while (!_isWindowReady)
            {
                await Task.Delay(50);
            }

            await FirstRunHelper.ShowFirstRunWarning(this);

            // Initialize Hardware Monitoring
            if (!ECUtils.Init())
            {
                Debug.WriteLine("Show PawnIOInstallDialog");
                await ShowPawnIOInstallDialog();
                return;
            }

            Debug.WriteLine("ECUtils.Init() returned true in MainWindow");

            legionGeneration = SettingsManager.LegionGeneration;

            CreateSuggestedConfigsIfMissing();
            try
            {
                // Detect current power mode
                var powerMode = PowerModeHelper.GetCurrentPowerMode();
                currentProfile = PowerModeHelper.PowerModeToProfile(powerMode);

                // Load the corresponding config
                string configPath = GetConfigPath(currentProfile);

                // Apply max RPM setting BEFORE loading config
                MAX_RPM = SettingsManager.GetMaxRpm();

                LoadConfig(configPath, currentProfile);

                // Update UI to reflect current profile
                SetActiveProfileButton(currentProfile);
            }
            catch (Exception ex)
            {
                await ShowDialogSafeAsync("Initialization Error",
                    $"Failed to detect power mode: {ex.Message}");
                LoadConfig(App.BalancedConfigPath, "balance");
            }

            // Draw initial curve
            DrawFanCurve();

            _monitoringTimer = new DispatcherTimer();
            _monitoringTimer.Interval = TimeSpan.FromSeconds(1);
            _monitoringTimer.Tick += (s, e) =>
            {
                try
                {
                    int cpuTemp = ECUtils.ReadCpuTemp();
                    int gpuTemp = ECUtils.ReadGpuTemp();
                    int cpuFan = ECUtils.ReadFan1Rpm();
                    int gpuFan = ECUtils.ReadFan2Rpm();

                    MonitorCpuTemp.Text = $"{cpuTemp} °C";
                    MonitorGpuTemp.Text = $"{gpuTemp} °C";
                    MonitorCpuFan.Text = $"{cpuFan} RPM";
                    MonitorGpuFan.Text = $"{gpuFan} RPM";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Monitoring Tick Error: {ex.Message}");
                }
            };
            if (_appWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State != OverlappedPresenterState.Minimized &&
                _isWindowVisible)
            {
                _monitoringTimer.Start();
            }
        }

        private string GetConfigPath(string profile)
        {
            return profile switch
            {
                "performance" => App.PerformanceConfigPath,
                "quiet" => App.QuietConfigPath,
                _ => App.BalancedConfigPath
            };
        }

        private void SetActiveProfileButton(string profile)
        {
            var accentStyle = Application.Current.Resources["AccentButtonStyle"] as Style;

            // Reset all buttons
            DefaultBtn.Style = null;
            PerformanceBtn.Style = null;
            QuietBtn.Style = null;

            DefaultBtn.Opacity = 1.0;
            PerformanceBtn.Opacity = 1.0;
            QuietBtn.Opacity = 1.0;

            // Highlight active button
            switch (profile)
            {
                case "balanced":
                    DefaultBtn.Style = accentStyle;
                    break;
                case "performance":
                    PerformanceBtn.Style = accentStyle;
                    break;
                case "quiet":
                    QuietBtn.Style = accentStyle;
                    break;
            }
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            _xamlRoot = this.Content.XamlRoot;
            _isWindowReady = true;
            this.Activated -= MainWindow_Activated;

            if (_shouldStartMinimized)
            {
                HideWindow();
            }
        }

        private async Task ShowDialogSafeAsync(string title, string message)
        {
            try
            {
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



        #region Config Management

        private string ReadCurrentECConfig()
        {
            byte[] rpmPoints = new byte[9];
            byte[] cpuTemps = new byte[9];
            byte[] gpuTemps = new byte[9];

            for (int i = 0; i < 9; i++)
            {
                rpmPoints[i] = ECUtils.ReadECByte((ushort)(0xC551 + i));
                cpuTemps[i] = ECUtils.ReadECByte((ushort)(0xC580 + i));
                gpuTemps[i] = ECUtils.ReadECByte((ushort)(0xC5A0 + i));
            }

            byte acclValue, declValue;

            if (legionGeneration == 5)
            {
                acclValue = ECUtils.ReadECByte(0xC3DC);
                declValue = ECUtils.ReadECByte(0xC3DD);
            }
            else
            {
                acclValue = ECUtils.ReadECByte(0xC560);
                declValue = ECUtils.ReadECByte(0xC570);
            }

            int pointCount = ECUtils.ReadECByte(0xC535);

            // Read hysteresis from EC if available, otherwise use 3
            int hysteresis = ECUtils.ReadECByte(0xC5FE);
            if (hysteresis < 1 || hysteresis > 8)
            {
                hysteresis = 3;
            }

            return $@"legion_gen : {legionGeneration}
fan_curve_points : {pointCount}
fan_accl_value : {acclValue}
fan_deccl_value : {declValue}
hysteresis : {hysteresis}
fan_rpm_points : {string.Join(" ", rpmPoints.Select(b => (b * 100).ToString()))}
cpu_temps_ramp_up : {string.Join(" ", cpuTemps)}
cpu_temps_ramp_down : {string.Join(" ", cpuTemps.Select(b => Math.Max(0, b - hysteresis)))}
gpu_temps_ramp_up : {string.Join(" ", gpuTemps)}
gpu_temps_ramp_down : {string.Join(" ", gpuTemps.Select(b => Math.Max(0, b - hysteresis)))}
hst_temps_ramp_up : {string.Join(" ", gpuTemps)}
hst_temps_ramp_down : {string.Join(" ", gpuTemps.Select(b => Math.Max(0, b - hysteresis)))}";
        }

        private string GetBackupPath()
        {
            string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LegionFanController", "Backups");
            return Path.Combine(backupDir, $"fan_config_{currentProfile}_backup.txt");
        }

        private void LoadConfig(string configPath, string profileName)
        {
            try
            {
                string[] lines;
                bool needApply = true;

                if (!File.Exists(configPath))
                {
                    string defaultContent = ReadCurrentECConfig();
                    lines = defaultContent.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    needApply = false;
                }
                else
                {
                    lines = File.ReadAllLines(configPath)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToArray();
                }

                currentConfig = ParseConfig(lines);
                LoadCurvePointsFromConfig();
                UpdateDeviceSelector();
                if (needApply)
                    ApplyFanCurveToEC();
            }
            catch (Exception ex)
            {
                _ = ShowDialogSafeAsync("Config Error", $"Failed to load config: {ex.Message}");
                currentConfig = CreateDefaultConfig();
                //InitializeCurvePoints(5);
                //UpdateDeviceSelector();
            }
        }

        private void LoadCurvePointsFromConfig()
        {
            int pointCount = currentConfig.CpuTempsRampUp.Length;
            cpuCurvePoints.Clear();
            gpuCurvePoints.Clear();

            for (int i = 0; i < pointCount; i++)
            {
                int cpuTemp = Math.Clamp(currentConfig.CpuTempsRampUp[i], MIN_TEMP, MAX_TEMP);
                int gpuTemp = Math.Clamp(currentConfig.GpuTempsRampUp[i], MIN_TEMP, MAX_TEMP);
                int rpm = Math.Clamp(currentConfig.FanRpmPoints[i], MIN_RPM, MAX_RPM);

                cpuCurvePoints.Add(new CurvePoint { Temp = cpuTemp, Rpm = rpm });
                gpuCurvePoints.Add(new CurvePoint { Temp = gpuTemp, Rpm = rpm });
            }

            UpdatePointCountText();
            DrawFanCurve();
        }

        private FanConfig ParseConfig(string[] lines)
        {
            return new FanConfig
            {
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
                HstTempsRampDown = GetConfigArray(lines, "hst_temps_ramp_down", new[] { 28, 48, 53, 63, 68 }),
                Hysteresis = GetConfigValue(lines, "hysteresis", 3)
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
                HstTempsRampDown = new[] { 28, 48, 53, 63, 68 },
                Hysteresis = 3
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

        private void ApplyFanCurveToEC(bool isRestore = false)
        {
            if (currentConfig == null || cpuCurvePoints.Count < 2) return;

            for (int i = 1; i < cpuCurvePoints.Count; i++)
            {
                if (cpuCurvePoints[i].Temp <= cpuCurvePoints[i - 1].Temp) return;
            }

            string backupPath = GetBackupPath();
            if (!File.Exists(backupPath))
            {
                string defaultContent = ReadCurrentECConfig();
                Debug.WriteLine($"backupPath: {Path.GetDirectoryName(backupPath)}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                File.WriteAllText(backupPath, defaultContent);
            }

            byte acclValue = (byte)Math.Clamp(AccVal.Value, 1, 15);
            byte declValue = (byte)Math.Clamp(DecVal.Value, 1, 15);

            byte[] rpmPoints = cpuCurvePoints.Select(p => (byte)Math.Clamp(p.Rpm / 100, 0, 255)).ToArray();

            byte[] cpuRampUp = cpuCurvePoints.Select(p => (byte)Math.Clamp(p.Temp, 0, 255)).ToArray();
            byte[] cpuRampDown = cpuCurvePoints.Select(p => (byte)Math.Clamp(Math.Max(0, p.Temp - currentConfig.Hysteresis), 0, 255)).ToArray();

            byte[] gpuRampUp = gpuCurvePoints.Select(p => (byte)Math.Clamp(p.Temp, 0, 255)).ToArray();
            byte[] gpuRampDown = gpuCurvePoints.Select(p => (byte)Math.Clamp(Math.Max(0, p.Temp - currentConfig.Hysteresis), 0, 255)).ToArray();

            ECWriter.WriteFanAcclDeccl(legionGeneration, acclValue, declValue);
            ECWriter.WriteFanPointCount();
            ECWriter.WriteFanRpmPoints(rpmPoints);
            ECWriter.WriteTemperatureRamp(cpuRampUp, cpuRampDown,
                (ushort)ECWriteRegisters.CPU_RAMP_UP, (ushort)ECWriteRegisters.CPU_RAMP_DOWN);
            ECWriter.WriteTemperatureRamp(gpuRampUp, gpuRampDown,
                (ushort)ECWriteRegisters.GPU_RAMP_UP, (ushort)ECWriteRegisters.GPU_RAMP_DOWN);

            if (isRestore)
            {
                byte[] hstRampUp = currentConfig.HstTempsRampUp.Select(t => (byte)Math.Clamp(t, 0, 255)).ToArray();
                byte[] hstRampDown = currentConfig.HstTempsRampDown.Select(t => (byte)Math.Clamp(t, 0, 255)).ToArray();
                ECWriter.WriteTemperatureRamp(hstRampUp, hstRampDown,
                    (ushort)ECWriteRegisters.HST_RAMP_UP, (ushort)ECWriteRegisters.HST_RAMP_DOWN);
            }
            else
            {
                ECWriter.WriteTemperatureRamp(gpuRampUp, gpuRampDown,
                    (ushort)ECWriteRegisters.HST_RAMP_UP, (ushort)ECWriteRegisters.HST_RAMP_DOWN);
            }

            ECWriter.WriteStopRgbFanWake();
            ECWriter.WriteFanTableChangeCounter(0x64);
        }

        private void ApplyConfigToUI(FanConfig config)
        {
            currentConfig = config;
            LoadCurvePointsFromConfig();
            UpdateDeviceSelector();

            AccVal.Value = config.AccelerationValue;
            DecVal.Value = config.DecelerationValue;
        }

        private async void LoadSuggestedConfig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string mode)
            {
                if (LoadProfileBtn.Flyout is Flyout flyout)
                {
                    flyout.Hide();
                }

                string suggestedPath = mode switch
                {
                    "performance" => App.SuggestedPerformancePath,
                    "quiet" => App.SuggestedQuietPath,
                    _ => App.SuggestedBalancedPath
                };

                if (!File.Exists(suggestedPath))
                {
                    await ShowDialogSafeAsync("Suggested Config Not Found",
                        $"Suggested configuration for {mode} mode not found.\n\n" +
                        $"Please place the file at:\n{suggestedPath}");
                    return;
                }

                var result = await ShowConfirmationDialogAsync("Load Suggested Config",
                    $"Load suggested configuration for {mode} mode?\n\n" +
                    "This will replace your current curve settings. You can save your current settings first if needed.");

                if (result != ContentDialogResult.Primary)
                    return;

                try
                {
                    var lines = File.ReadAllLines(suggestedPath)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToArray();

                    var suggestedConfig = ParseConfig(lines);

                    ApplyConfigToUI(suggestedConfig);

                    await ShowDialogSafeAsync("Config Loaded",
                        $"Suggested {mode} configuration loaded.\n\nClick Save to apply to EC.");
                }
                catch (Exception ex)
                {
                    await ShowDialogSafeAsync("Error", $"Failed to load suggested config: {ex.Message}");
                }
            }
        }


        [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetOpenFileName(ref OPENFILENAME lpofn);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct OPENFILENAME
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public string lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpstrTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int flagsEx;
        }

        private async void LoadCustomConfig_Click(object sender, RoutedEventArgs e)
        {
            if (LoadProfileBtn.Flyout is Flyout flyout)
                flyout.Hide();

            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(typeof(OPENFILENAME));
            ofn.hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ofn.lpstrFilter = "Text files\0*.txt\0All files\0*.*\0";
            ofn.lpstrFile = new string(new char[2048]);
            ofn.nMaxFile = ofn.lpstrFile.Length;
            ofn.lpstrFileTitle = new string(new char[256]);
            ofn.nMaxFileTitle = 256;
            ofn.lpstrTitle = "Select Config File";
            ofn.Flags = 0x00080000;

            if (!GetOpenFileName(ref ofn)) return;

            string path = ofn.lpstrFile.TrimEnd('\0');

            var lines = File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            var config = ParseConfig(lines);

            // Check generation mismatch

            if (config.LegionGeneration != legionGeneration)
            {
                var warnResult = await ShowConfirmationDialogAsync("Generation Mismatch",
                    $"Warning: The loaded config is for Gen{config.LegionGeneration}, " +
                    $"but your device is detected as Gen{legionGeneration}.\n\n" +
                    $"Applying this config may cause unexpected fan behavior.\n\n" +
                    $"Continue anyway?");

                if (warnResult != ContentDialogResult.Primary) return;
            }

            var result = await ShowConfirmationDialogAsync("Load Custom Config",
                $"Load configuration from:\n{Path.GetFileName(path)}\n\n" +
                $"Legion Gen: {config.LegionGeneration}\n" +
                $"Curve Points: {config.FanCurvePoints}");

            if (result != ContentDialogResult.Primary) return;

            ApplyConfigToUI(config);

            await ShowDialogSafeAsync("Config Loaded",
                $"Configuration loaded from:\n{Path.GetFileName(path)}\n\nClick Save to apply to EC.");
        }

        private async Task<ContentDialogResult> ShowConfirmationDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Load",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content?.XamlRoot
            };

            return await dialog.ShowAsync();
        }

        private void SaveConfig()
        {
            try
            {
                currentConfig.FanCurvePoints = cpuCurvePoints.Count;
                currentConfig.AccelerationValue = (int)AccVal.Value;
                currentConfig.DecelerationValue = (int)DecVal.Value;

                // Extract RPM and temps from curve points
                currentConfig.FanRpmPoints = cpuCurvePoints.Select(p => p.Rpm).ToArray();
                currentConfig.CpuTempsRampUp = cpuCurvePoints.Select(p => p.Temp).ToArray();
                currentConfig.GpuTempsRampUp = gpuCurvePoints.Select(p => p.Temp).ToArray();
                ApplyFanCurveToEC();

                string configPath = GetConfigPath(currentProfile);

                string configContent = GenerateConfigContent();
                File.WriteAllText(configPath, configContent);

                ShowSuccessDialog("Configuration saved", "");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error Saving Config", $"Failed to save configuration: {ex.Message} ");
            }
        }

        private void CreateSuggestedConfigsIfMissing()
        {
            // Create default suggested configs
            CreateSuggestedConfigIfMissing(App.SuggestedBalancedPath, GetDefaultBalancedSuggested());
            CreateSuggestedConfigIfMissing(App.SuggestedPerformancePath, GetDefaultPerformanceSuggested());
            CreateSuggestedConfigIfMissing(App.SuggestedQuietPath, GetDefaultQuietSuggested());
        }

        private void CreateSuggestedConfigIfMissing(string path, string content)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, content);
            }
        }

        private string GetDefaultBalancedSuggested()
        {
            return @"fan_curve_points : 9
fan_accl_value : 2
fan_deccl_value : 2
hysteresis : 3
fan_rpm_points : 0 1500 2200 3200 3500 3800 4100 4400 4700
cpu_temps_ramp_up : 30 45 55 60 65 70 75 80 85
cpu_temps_ramp_down : 28 43 53 58 63 68 73 78 83
gpu_temps_ramp_up : 30 50 55 60 63 66 69 72 75
gpu_temps_ramp_down : 28 48 53 58 61 63 67 70 73
hst_temps_ramp_up : 30 50 55 65 70 75 80 85 90
hst_temps_ramp_down : 28 48 53 63 68 73 78 83 85";
        }

        private string GetDefaultPerformanceSuggested()
        {
            return @"fan_curve_points : 9
fan_accl_value : 1
fan_deccl_value : 1
hysteresis : 3
fan_rpm_points : 0 1800 2600 3400 3900 4200 4500 4800 5000
cpu_temps_ramp_up : 30 45 55 60 65 70 75 80 85
cpu_temps_ramp_down : 28 43 53 58 63 68 73 78 83
gpu_temps_ramp_up : 30 45 55 60 65 70 75 80 85
gpu_temps_ramp_down : 28 43 53 58 63 68 73 78 83
hst_temps_ramp_up : 30 50 55 65 70 75 80 85 90
hst_temps_ramp_down : 28 48 53 63 68 73 78 83 85";
        }

        private string GetDefaultQuietSuggested()
        {
            return @"fan_curve_points : 9
fan_accl_value : 3
fan_deccl_value : 4
hysteresis : 4
fan_rpm_points : 0 0 0 1500 1800 2100 2400 2700 3000
cpu_temps_ramp_up : 30 45 55 60 65 70 75 80 85
cpu_temps_ramp_down : 28 43 53 58 63 68 73 78 83
gpu_temps_ramp_up : 30 45 55 60 65 70 75 80 85
gpu_temps_ramp_down : 28 43 53 58 63 68 73 78 83
hst_temps_ramp_up : 30 45 55 65 70 75 80 85 90
hst_temps_ramp_down : 28 43 53 63 68 73 78 83 85";
        }

        private string GenerateConfigContent()
        {
            int h = currentConfig.Hysteresis == 0 ? 3 : currentConfig.Hysteresis;
            int[] cpuRampDown = currentConfig.CpuTempsRampUp.Select(t => Math.Max(0, t - h)).ToArray();
            int[] gpuRampDown = currentConfig.GpuTempsRampUp.Select(t => Math.Max(0, t - h)).ToArray();

            return $@"legion_gen : {legionGeneration}
fan_curve_points : {currentConfig.FanCurvePoints}
fan_accl_value : {currentConfig.AccelerationValue}
fan_deccl_value : {currentConfig.DecelerationValue}
hysteresis : {h}
fan_rpm_points : {string.Join(" ", currentConfig.FanRpmPoints)}
cpu_temps_ramp_up : {string.Join(" ", currentConfig.CpuTempsRampUp)}
cpu_temps_ramp_down : {string.Join(" ", cpuRampDown)}
gpu_temps_ramp_up : {string.Join(" ", currentConfig.GpuTempsRampUp)}
gpu_temps_ramp_down : {string.Join(" ", gpuRampDown)}
hst_temps_ramp_up : {string.Join(" ", currentConfig.GpuTempsRampUp)}
hst_temps_ramp_down : {string.Join(" ", gpuRampDown)}";
        }

        private void UpdateDeviceSelector()
        {
            if (DeviceSelector == null) return;

            try
            {
                DeviceSelector.SelectedIndex = legionGeneration == 5 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateDeviceSelector error: {ex.Message}");
            }
        }

        #endregion

        #region Fan Curve Drawing

        private void FanCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawFanCurve();
        }

        private void CpuCurveBtn_Click(object sender, RoutedEventArgs e)
        {
            CpuCurveBtn.IsChecked = true;
            GpuCurveBtn.IsChecked = false;
            currentEditingCurve = "cpu";
            UpdateCurveInfo();
            DrawFanCurve();
        }

        private void GpuCurveBtn_Click(object sender, RoutedEventArgs e)
        {
            CpuCurveBtn.IsChecked = false;
            GpuCurveBtn.IsChecked = true;
            currentEditingCurve = "gpu";
            UpdateCurveInfo();
            DrawFanCurve();
        }

        private void UpdateCurveInfo()
        {
            if (CurveInfoText != null)
            {
                if (currentEditingCurve == "cpu")
                {
                    CurveInfoText.Text = "Click and drag points to adjust the CPU fan curve.";
                }
                else
                {
                    CurveInfoText.Text = "Adjust GPU temperature points. RPM values mirror the CPU curve.";
                }
            }
        }

        private void DrawFanCurve()
        {
            if (DispatcherQueue.HasThreadAccess == false)
            {
                DispatcherQueue.TryEnqueue(() => DrawFanCurve());
                return;
            }
            if (FanCurveCanvas == null || FanCurveCanvas.ActualWidth == 0) return;

            FanCurveCanvas.Children.Clear();

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;
            double graphWidth = width - 2 * GRAPH_MARGIN;
            double graphHeight = height - 2 * GRAPH_MARGIN;

            // Draw grid
            DrawGrid(graphWidth, graphHeight);

            // Draw axes
            DrawAxes(graphWidth, graphHeight);

            // Draw curve based on selected mode
            var points = currentEditingCurve == "cpu" ? cpuCurvePoints : gpuCurvePoints;
            var curveColor = currentEditingCurve == "cpu" ?
                Application.Current.Resources["SystemAccentColor"] as Windows.UI.Color? ?? Colors.DodgerBlue :
                Colors.LimeGreen;

            DrawCurve(points, graphWidth, graphHeight, curveColor, currentEditingCurve);
        }

        private void DrawGrid(double graphWidth, double graphHeight)
        {
            // Use theme-aware grid color
            var gridBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as SolidColorBrush ??
                            new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255));

            // Vertical grid lines (temperature)
            for (int temp = MIN_TEMP; temp <= MAX_TEMP; temp += 10)
            {
                double x = GRAPH_MARGIN + (temp - MIN_TEMP) / (double)(MAX_TEMP - MIN_TEMP) * graphWidth;
                var line = new Line
                {
                    X1 = x,
                    Y1 = GRAPH_MARGIN,
                    X2 = x,
                    Y2 = GRAPH_MARGIN + graphHeight,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    Opacity = 0.3
                };
                FanCurveCanvas.Children.Add(line);
            }

            // Horizontal grid lines (RPM)
            for (int rpm = MIN_RPM; rpm <= MAX_RPM; rpm += 500)
            {
                double y = GRAPH_MARGIN + graphHeight - ((rpm - MIN_RPM) / (double)(MAX_RPM - MIN_RPM) * graphHeight);
                var line = new Line
                {
                    X1 = GRAPH_MARGIN,
                    Y1 = y,
                    X2 = GRAPH_MARGIN + graphWidth,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    Opacity = 0.3
                };
                FanCurveCanvas.Children.Add(line);
            }
        }

        private void DrawAxes(double graphWidth, double graphHeight)
        {
            // Use theme-aware colors
            var axisBrush = Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush ??
                            new SolidColorBrush(Colors.White);
            var textBrush = Application.Current.Resources["TextFillColorSecondaryBrush"] as SolidColorBrush ??
                            new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 255, 255));

            // X-axis
            var xAxis = new Line
            {
                X1 = GRAPH_MARGIN,
                Y1 = GRAPH_MARGIN + graphHeight,
                X2 = GRAPH_MARGIN + graphWidth,
                Y2 = GRAPH_MARGIN + graphHeight,
                Stroke = axisBrush,
                StrokeThickness = 2
            };
            FanCurveCanvas.Children.Add(xAxis);

            // Y-axis
            var yAxis = new Line
            {
                X1 = GRAPH_MARGIN,
                Y1 = GRAPH_MARGIN,
                X2 = GRAPH_MARGIN,
                Y2 = GRAPH_MARGIN + graphHeight,
                Stroke = axisBrush,
                StrokeThickness = 2
            };
            FanCurveCanvas.Children.Add(yAxis);

            // X-axis labels (Temperature)
            for (int temp = MIN_TEMP; temp <= MAX_TEMP; temp += 20)
            {
                double x = GRAPH_MARGIN + (temp - MIN_TEMP) / (double)(MAX_TEMP - MIN_TEMP) * graphWidth;
                var label = new TextBlock
                {
                    Text = $"{temp}°",
                    Foreground = textBrush,
                    FontSize = 10
                };
                Canvas.SetLeft(label, x - 10);
                Canvas.SetTop(label, GRAPH_MARGIN + graphHeight + 5);
                FanCurveCanvas.Children.Add(label);
            }

            // Y-axis labels (RPM)
            for (int rpm = MIN_RPM; rpm <= MAX_RPM; rpm += 1000)
            {
                double y = GRAPH_MARGIN + graphHeight - ((rpm - MIN_RPM) / (double)(MAX_RPM - MIN_RPM) * graphHeight);
                var label = new TextBlock
                {
                    Text = $"{rpm}",
                    Foreground = textBrush,
                    FontSize = 10
                };
                Canvas.SetLeft(label, 5);
                Canvas.SetTop(label, y - 7);
                FanCurveCanvas.Children.Add(label);
            }

            // Axis titles
            var xTitle = new TextBlock
            {
                Text = "Temperature (°C)",
                Foreground = textBrush,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            Canvas.SetLeft(xTitle, GRAPH_MARGIN + graphWidth / 2 - 50);
            Canvas.SetTop(xTitle, GRAPH_MARGIN + graphHeight + 25);
            FanCurveCanvas.Children.Add(xTitle);

            var yTitle = new TextBlock
            {
                Text = currentEditingCurve == "cpu" ? "Fan RPM" : "Fan RPM (mirrors CPU)",
                Foreground = textBrush,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            Canvas.SetLeft(yTitle, 5);
            Canvas.SetTop(yTitle, 5);
            FanCurveCanvas.Children.Add(yTitle);
        }

        private void DrawCurve(List<CurvePoint> points, double graphWidth, double graphHeight, Windows.UI.Color color, string curveType)
        {
            if (points.Count < 2) return;

            var curveBrush = new SolidColorBrush(color);
            var pointBrush = new SolidColorBrush(color);

            // Calculate all point positions first
            var positions = new List<Windows.Foundation.Point>();
            foreach (var point in points)
            {
                double x = GRAPH_MARGIN + (point.Temp - MIN_TEMP) / (double)(MAX_TEMP - MIN_TEMP) * graphWidth;
                double y = GRAPH_MARGIN + graphHeight - ((point.Rpm - MIN_RPM) / (double)(MAX_RPM - MIN_RPM) * graphHeight);
                positions.Add(new Windows.Foundation.Point(x, y));
            }

            // Extended positions to edges
            var drawingPositions = new List<Windows.Foundation.Point>();
            bool hasStartExtension = false;
            if (points[0].Temp > MIN_TEMP)
            {
                drawingPositions.Add(new Windows.Foundation.Point(GRAPH_MARGIN, positions[0].Y));
                hasStartExtension = true;
            }
            drawingPositions.AddRange(positions);
            bool hasEndExtension = false;
            if (points[points.Count - 1].Temp < MAX_TEMP)
            {
                drawingPositions.Add(new Windows.Foundation.Point(GRAPH_MARGIN + graphWidth, positions[positions.Count - 1].Y));
                hasEndExtension = true;
            }

            // Create gradient fill below the curve
            var fillPath = new Microsoft.UI.Xaml.Shapes.Path();
            var fillGeometry = new PathGeometry();
            var fillFigure = new PathFigure();

            // Start from bottom-left
            fillFigure.StartPoint = new Windows.Foundation.Point(drawingPositions[0].X, GRAPH_MARGIN + graphHeight);

            // Line up to first point
            fillFigure.Segments.Add(new LineSegment { Point = drawingPositions[0] });

            // Create smooth curve through all points
            for (int i = 0; i < drawingPositions.Count - 1; i++)
            {
                // Straigh on edges
                if ((i == 0 && hasStartExtension) || (i == drawingPositions.Count - 2 && hasEndExtension))
                {
                    fillFigure.Segments.Add(new LineSegment { Point = drawingPositions[i + 1] });
                    continue;
                }

                var p0 = drawingPositions[Math.Max(0, i - 1)];
                var p1 = drawingPositions[i];
                var p2 = drawingPositions[i + 1];
                var p3 = drawingPositions[Math.Min(drawingPositions.Count - 1, i + 2)];

                // Calculate control points for smooth bezier curve
                double tension = 0.1; // Smoothness factor (0 = sharp corners, 1 = very smooth)

                var cp1 = new Windows.Foundation.Point(
                    p1.X + (p2.X - p0.X) * tension,
                    p1.Y + (p2.Y - p0.Y) * tension
                );

                var cp2 = new Windows.Foundation.Point(
                    p2.X - (p3.X - p1.X) * tension,
                    p2.Y - (p3.Y - p1.Y) * tension
                );

                if (p1.Y == p2.Y)
                {
                    fillFigure.Segments.Add(new LineSegment { Point = p2 });
                }
                else
                {
                    fillFigure.Segments.Add(new BezierSegment
                    {
                        Point1 = cp1,
                        Point2 = cp2,
                        Point3 = p2
                    });
                }
            }

            // Line down to bottom-right
            fillFigure.Segments.Add(new LineSegment
            {
                Point = new Windows.Foundation.Point(drawingPositions[drawingPositions.Count - 1].X, GRAPH_MARGIN + graphHeight)
            });

            fillGeometry.Figures.Add(fillFigure);
            fillPath.Data = fillGeometry;

            // Create gradient brush for fill
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1)
            };

            gradientBrush.GradientStops.Add(new GradientStop
            {
                Color = Windows.UI.Color.FromArgb(80, color.R, color.G, color.B),
                Offset = 0
            });
            gradientBrush.GradientStops.Add(new GradientStop
            {
                Color = Windows.UI.Color.FromArgb(20, color.R, color.G, color.B),
                Offset = 0.5
            });
            gradientBrush.GradientStops.Add(new GradientStop
            {
                Color = Windows.UI.Color.FromArgb(5, color.R, color.G, color.B),
                Offset = 1
            });

            fillPath.Fill = gradientBrush;
            FanCurveCanvas.Children.Add(fillPath);

            // Draw smooth curve line on top
            var curvePath = new Microsoft.UI.Xaml.Shapes.Path();
            var curveGeometry = new PathGeometry();
            var curveFigure = new PathFigure { StartPoint = drawingPositions[0] };

            // Create smooth curve through all points
            for (int i = 0; i < drawingPositions.Count - 1; i++)
            {
                // Use a straight line for edge extensions
                if ((i == 0 && hasStartExtension) || (i == drawingPositions.Count - 2 && hasEndExtension))
                {
                    curveFigure.Segments.Add(new LineSegment { Point = drawingPositions[i + 1] });
                    continue;
                }

                var p0 = drawingPositions[Math.Max(0, i - 1)];
                var p1 = drawingPositions[i];
                var p2 = drawingPositions[i + 1];
                var p3 = drawingPositions[Math.Min(drawingPositions.Count - 1, i + 2)];

                double tension = 0.1;

                var cp1 = new Windows.Foundation.Point(
                    p1.X + (p2.X - p0.X) * tension,
                    p1.Y + (p2.Y - p0.Y) * tension
                );

                var cp2 = new Windows.Foundation.Point(
                    p2.X - (p3.X - p1.X) * tension,
                    p2.Y - (p3.Y - p1.Y) * tension
                );

                if (p1.Y == p2.Y)
                {
                    curveFigure.Segments.Add(new LineSegment { Point = p2 });
                }
                else
                {
                    curveFigure.Segments.Add(new BezierSegment
                    {
                        Point1 = cp1,
                        Point2 = cp2,
                        Point3 = p2
                    });
                }
            }

            curveGeometry.Figures.Add(curveFigure);
            curvePath.Data = curveGeometry;
            curvePath.Stroke = curveBrush;
            curvePath.StrokeThickness = 3;
            curvePath.StrokeLineJoin = PenLineJoin.Round;
            FanCurveCanvas.Children.Add(curvePath);

            // Draw points on top
            var whiteBrush = Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush ??
                            new SolidColorBrush(Colors.White);

            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var pos = positions[i];

                // Point circle (no number inside now)
                var ellipse = new Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = pointBrush,
                    Stroke = whiteBrush,
                    StrokeThickness = 2,
                    Tag = new PointTag { Point = point, IsCpu = curveType == "cpu" }
                };

                Canvas.SetLeft(ellipse, pos.X - 10);
                Canvas.SetTop(ellipse, pos.Y - 10);
                FanCurveCanvas.Children.Add(ellipse);

                // Temperature label on TOP of point
                var tempLabel = new TextBlock
                {
                    Text = $"{point.Temp}°C",
                    FontSize = 10,
                    Foreground = whiteBrush,
                    Opacity = 0.9,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                Canvas.SetLeft(tempLabel, pos.X - 15);
                Canvas.SetTop(tempLabel, pos.Y - 28);
                FanCurveCanvas.Children.Add(tempLabel);

                // RPM label on BOTTOM of point
                var rpmLabel = new TextBlock
                {
                    Text = $"{point.Rpm}",
                    FontSize = 10,
                    Foreground = whiteBrush,
                    Opacity = 0.85
                };
                Canvas.SetLeft(rpmLabel, pos.X - 15);
                Canvas.SetTop(rpmLabel, pos.Y + 15);
                FanCurveCanvas.Children.Add(rpmLabel);
            }
        }

        #endregion

        #region Point Dragging

        private void FanCurveCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint(FanCurveCanvas).Position;
            pressedPosition = position;
            isDragging = false;

            // Find if we clicked on a point (20px diameter)
            foreach (var child in FanCurveCanvas.Children)
            {
                if (child is Ellipse ellipse && ellipse.Tag is PointTag tag)
                {
                    double left = Canvas.GetLeft(ellipse);
                    double top = Canvas.GetTop(ellipse);

                    // Check if click is within the 20px circle
                    if (Math.Abs(position.X - (left + 10)) < 15 && Math.Abs(position.Y - (top + 10)) < 15)
                    {
                        draggedPoint = tag.Point;
                        isDraggingCpu = tag.IsCpu;
                        FanCurveCanvas.CapturePointer(e.Pointer);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        private void FanCurveCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (draggedPoint == null || SettingsManager.GetLockPoints()) return;

            var position = e.GetCurrentPoint(FanCurveCanvas).Position;

            // Check if we've moved enough to consider it a drag (threshold of 5 pixels)
            if (!isDragging)
            {
                double distance = Math.Sqrt(
                    Math.Pow(position.X - pressedPosition.X, 2) +
                    Math.Pow(position.Y - pressedPosition.Y, 2));

                if (distance < 5)
                {
                    return; // Not dragging yet
                }

                isDragging = true;
            }

            double graphWidth = FanCurveCanvas.ActualWidth - 2 * GRAPH_MARGIN;
            double graphHeight = FanCurveCanvas.ActualHeight - 2 * GRAPH_MARGIN;

            // Get the list we're working with
            var points = currentEditingCurve == "cpu" ? cpuCurvePoints : gpuCurvePoints;
            int pointIndex = points.IndexOf(draggedPoint);
            if (pointIndex < 0) return;

            if (currentEditingCurve == "cpu")
            {
                // CPU mode: can change both temp and RPM with chain movement
                double tempPercent = (position.X - GRAPH_MARGIN) / graphWidth;
                double rpmPercent = 1 - (position.Y - GRAPH_MARGIN) / graphHeight;

                int newTemp = (int)Math.Clamp(MIN_TEMP + (MAX_TEMP - MIN_TEMP) * tempPercent, MIN_TEMP, MAX_TEMP);
                int rawRpm = (int)Math.Clamp(MIN_RPM + (MAX_RPM - MIN_RPM) * rpmPercent, MIN_RPM, MAX_RPM);

                // Snap to 50 RPM intervals
                int newRpm = (int)(Math.Round(rawRpm / 50.0) * 50);
                newRpm = (int)Math.Clamp(newRpm, MIN_RPM, MAX_RPM);

                // Safety constraint
                if (SettingsManager.GetEnableSafeguards())
                {
                    if (newTemp > 80 || pointIndex == points.Count - 1)
                    {
                        newRpm = Math.Max(newRpm, 2000);
                    }
                }

                // Constrain temperature: can't pass previous or next point
                if (pointIndex > 0)
                {
                    newTemp = Math.Max(newTemp, points[pointIndex - 1].Temp + 1);
                }
                if (pointIndex < points.Count - 1)
                {
                    newTemp = Math.Min(newTemp, points[pointIndex + 1].Temp - 1);
                }

                int oldRpm = draggedPoint.Rpm;

                // DIRECTIONAL CHAIN MOVEMENT LOGIC
                if (newRpm > oldRpm)
                {
                    // Moving UP: only affect this point and points AFTER it at the same level
                    int chainEnd = pointIndex;

                    // Find how many consecutive points after this one share the same RPM
                    while (chainEnd < points.Count - 1 && points[chainEnd + 1].Rpm == oldRpm)
                    {
                        chainEnd++;
                    }

                    // Constrain by the first point AFTER the chain
                    if (chainEnd < points.Count - 1)
                    {
                        newRpm = Math.Min(newRpm, points[chainEnd + 1].Rpm);
                    }

                    // Move this point and all points after it that share the same RPM
                    for (int i = pointIndex; i <= chainEnd; i++)
                    {
                        points[i].Rpm = newRpm;
                    }
                }
                else if (newRpm < oldRpm)
                {
                    // Moving DOWN: only affect this point and points BEFORE it at the same level
                    int chainStart = pointIndex;

                    // Find how many consecutive points before this one share the same RPM
                    while (chainStart > 0 && points[chainStart - 1].Rpm == oldRpm)
                    {
                        chainStart--;
                    }

                    // Constrain by the first point BEFORE the chain
                    if (chainStart > 0)
                    {
                        newRpm = Math.Max(newRpm, points[chainStart - 1].Rpm);
                    }

                    // Move this point and all points before it that share the same RPM
                    for (int i = chainStart; i <= pointIndex; i++)
                    {
                        points[i].Rpm = newRpm;
                    }
                }

                draggedPoint.Temp = newTemp;
            }
            else
            {
                // GPU mode: directional push movement on Temperature (X-axis)
                double tempPercent = (position.X - GRAPH_MARGIN) / graphWidth;
                int newTemp = (int)Math.Clamp(MIN_TEMP + (MAX_TEMP - MIN_TEMP) * tempPercent, MIN_TEMP, MAX_TEMP);
                int oldTemp = draggedPoint.Temp;

                if (newTemp > oldTemp)
                {
                    // Dragging RIGHT: pushes points AFTER if hitting them
                    draggedPoint.Temp = newTemp;
                    for (int i = pointIndex; i < points.Count - 1; i++)
                    {
                        if (points[i + 1].Temp <= points[i].Temp)
                        {
                            points[i + 1].Temp = points[i].Temp + 1;
                        }
                        else
                        {
                            break;
                        }
                    }

                    // If pushed beyond MAX_TEMP, cap and cascade back
                    if (points[points.Count - 1].Temp > MAX_TEMP)
                    {
                        points[points.Count - 1].Temp = MAX_TEMP;
                        for (int i = points.Count - 1; i > 0; i--)
                        {
                            if (points[i - 1].Temp >= points[i].Temp)
                                points[i - 1].Temp = points[i].Temp - 1;
                        }
                    }
                }
                else if (newTemp < oldTemp)
                {
                    // Dragging LEFT: pushes points BEFORE if hitting them
                    draggedPoint.Temp = newTemp;
                    for (int i = pointIndex; i > 0; i--)
                    {
                        if (points[i - 1].Temp >= points[i].Temp)
                        {
                            points[i - 1].Temp = points[i].Temp - 1;
                        }
                        else
                        {
                            break;
                        }
                    }

                    // If pushed before MIN_TEMP, cap and cascade forward
                    if (points[0].Temp < MIN_TEMP)
                    {
                        points[0].Temp = MIN_TEMP;
                        for (int i = 0; i < points.Count - 1; i++)
                        {
                            if (points[i + 1].Temp <= points[i].Temp)
                                points[i + 1].Temp = points[i].Temp + 1;
                        }
                    }
                }
            }

            DrawFanCurve();
            e.Handled = true;
        }

        private async void FanCurveCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (draggedPoint != null)
            {
                var pointToEdit = draggedPoint;
                bool wasActuallyDragging = isDragging;

                // Sticking fix
                ResetDragState();

                if (!wasActuallyDragging)
                {
                    // Show flyout to edit point
                    await ShowPointEditorFlyout(pointToEdit);
                }

                DrawFanCurve();
            }
        }

        private void ResetDragState()
        {
            draggedPoint = null;
            isDragging = false;
            FanCurveCanvas.ReleasePointerCaptures();
        }

        private async Task ShowPointEditorFlyout(CurvePoint point)
        {
            var stackPanel = new StackPanel { Spacing = 12, MinWidth = 250 };

            // Temperature input
            var tempLabel = new TextBlock { Text = "Temperature (°C):", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var tempBox = new NumberBox
            {
                Value = point.Temp,
                Minimum = MIN_TEMP,
                Maximum = MAX_TEMP,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };

            // RPM input (only for CPU mode)
            var rpmLabel = new TextBlock
            {
                Text = currentEditingCurve == "cpu" ? "RPM:" : "RPM (mirrors CPU):",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            var rpmBox = new NumberBox
            {
                Value = point.Rpm,
                Minimum = MIN_RPM,
                Maximum = MAX_RPM,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                IsEnabled = currentEditingCurve == "cpu"
            };

            stackPanel.Children.Add(tempLabel);
            stackPanel.Children.Add(tempBox);
            stackPanel.Children.Add(rpmLabel);
            stackPanel.Children.Add(rpmBox);

            var dialog = new ContentDialog
            {
                Title = $"Edit Point ({currentEditingCurve.ToUpper()} Curve)",
                Content = stackPanel,
                PrimaryButtonText = "✓ Apply",
                CloseButtonText = "✕ Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content?.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var points = currentEditingCurve == "cpu" ? cpuCurvePoints : gpuCurvePoints;
                int pointIndex = points.IndexOf(point);

                if (pointIndex >= 0)
                {
                    int newTemp = (int)tempBox.Value;
                    int newRpm = (int)rpmBox.Value;

                    // Safety constraint
                    if (SettingsManager.GetEnableSafeguards() && currentEditingCurve == "cpu")
                    {
                        if (newTemp > 80 || pointIndex == points.Count - 1)
                        {
                            newRpm = Math.Max(newRpm, 2000);
                        }
                    }

                    // Apply constraints
                    if (pointIndex > 0)
                    {
                        newTemp = Math.Max(newTemp, points[pointIndex - 1].Temp + 1);
                    }
                    if (pointIndex < points.Count - 1)
                    {
                        newTemp = Math.Min(newTemp, points[pointIndex + 1].Temp - 1);
                    }

                    if (currentEditingCurve == "cpu")
                    {
                        if (pointIndex > 0)
                        {
                            newRpm = Math.Max(newRpm, points[pointIndex - 1].Rpm);
                        }
                        if (pointIndex < points.Count - 1)
                        {
                            newRpm = Math.Min(newRpm, points[pointIndex + 1].Rpm);
                        }
                        point.Rpm = newRpm;
                    }

                    point.Temp = newTemp;
                    DrawFanCurve();
                }
            }
        }

        #endregion

        #region Event Handlers

        private async Task<bool> WaitForECModeChangeAsync(int timeoutMs = 3000)
        {
            // Store values before mode change
            byte beforeCpu = ECUtils.ReadECByte(0xC580);
            byte beforeGpu = ECUtils.ReadECByte(0xC5A0);
            byte beforeRpm = ECUtils.ReadECByte(0xC551);

            int elapsed = 0;
            int interval = 50;

            while (elapsed < timeoutMs)
            {
                await Task.Delay(interval);
                elapsed += interval;

                byte afterCpu = ECUtils.ReadECByte(0xC580);
                byte afterGpu = ECUtils.ReadECByte(0xC5A0);
                byte afterRpm = ECUtils.ReadECByte(0xC551);

                // Check if any of the key registers changed
                if (afterCpu != beforeCpu || afterGpu != beforeGpu || afterRpm != beforeRpm)
                {
                    Debug.WriteLine($"EC mode changed: CPU {beforeCpu}->{afterCpu}, GPU {beforeGpu}->{afterGpu}, RPM {beforeRpm}->{afterRpm}");
                    await Task.Delay(100); // Wait for stability
                    return true;
                }
            }

            return false;
        }

        private async void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string newProfile)
            {
                var powerMode = newProfile switch
                {
                    "quiet" => PowerModeHelper.LegionPowerMode.Quiet,
                    "performance" => PowerModeHelper.LegionPowerMode.Performance,
                    _ => PowerModeHelper.LegionPowerMode.Balanced
                };

                SetLoadingState(true);

                try
                {
                    // Wait for system to apply default EC curve for this mode, then override with user config
                    bool success = await PowerModeHelper.SetPowerModeAndWaitAsync(powerMode, legionGeneration);

                    if (!success)
                    {
                        await ShowDialogSafeAsync("Error",
                            "Failed to change power mode. Please ensure:\n" +
                            "1. You're running as Administrator\n" +
                            "2. Lenovo Vantage/WMI services are running\n" +
                            "3. Your model supports power mode switching");

                        return;
                    }

                    currentProfile = newProfile;
                    string configPath = GetConfigPath(currentProfile);

                    LoadConfig(configPath, newProfile);
                    SetActiveProfileButton(newProfile);
                }
                finally
                {
                    SetLoadingState(false);
                }
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            ProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            DefaultBtn.IsEnabled = !isLoading;
            PerformanceBtn.IsEnabled = !isLoading;
            QuietBtn.IsEnabled = !isLoading;
        }

        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceSelector.SelectedItem is ComboBoxItem selectedItem && currentConfig != null)
            {
                legionGeneration = selectedItem.Content.ToString().Contains("5th") ? 5 : 6;
                SettingsManager.LegionGeneration = legionGeneration;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) => SaveConfig();

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyFanCurveToEC();
                ShowSuccessDialog("Fan Control", "Fan curve reapplied");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error", $"Failed to restart service: {ex.Message}");
            }
        }

        private async void RestoreDefaultBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = await ShowConfirmationDialogAsync("Restore Default Curve",
                $"Restore default fan curve for {currentProfile} mode?\n\n" +
                "This will replace your current settings and apply to EC immediately.");

            if (result != ContentDialogResult.Primary)
                return;

            RestoreDefaultConfig();
        }

        private void RestoreDefaultConfig()
        {
            string backupPath = GetBackupPath();

            if (!File.Exists(backupPath))
            {
                ShowErrorDialog("Error", $"No backup found for {currentProfile} mode.\n\nBackup path: {backupPath}");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(backupPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                currentConfig = ParseConfig(lines);
                LoadCurvePointsFromConfig();
                UpdateDeviceSelector();
                DrawFanCurve();
                ApplyFanCurveToEC(true);

                // Update UI sliders
                AccVal.Value = currentConfig.AccelerationValue;
                DecVal.Value = currentConfig.DecelerationValue;

                ShowSuccessDialog("Restored", $"Default curve for {currentProfile} mode restored.");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Error", $"Failed to restore: {ex.Message}");
            }
        }

        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            if (cpuCurvePoints.Count >= 8)
            {
                _ = ShowDialogSafeAsync("Maximum Points Reached", "You can have a maximum of 8 points on the curve.");
                return;
            }

            // Find a good spot to add a point (between existing points with largest gap)
            int bestIndex = 0;
            int largestGap = 0;

            for (int i = 0; i < cpuCurvePoints.Count - 1; i++)
            {
                int gap = cpuCurvePoints[i + 1].Temp - cpuCurvePoints[i].Temp;
                if (gap > largestGap)
                {
                    largestGap = gap;
                    bestIndex = i;
                }
            }

            // Insert new point in the middle of the largest gap
            int newTemp = (cpuCurvePoints[bestIndex].Temp + cpuCurvePoints[bestIndex + 1].Temp) / 2;
            int newRpm = (cpuCurvePoints[bestIndex].Rpm + cpuCurvePoints[bestIndex + 1].Rpm) / 2;

            cpuCurvePoints.Insert(bestIndex + 1, new CurvePoint { Temp = newTemp, Rpm = newRpm });
            gpuCurvePoints.Insert(bestIndex + 1, new CurvePoint { Temp = newTemp, Rpm = newRpm });

            UpdatePointCountText();
            DrawFanCurve();
        }

        private void RemovePoint_Click(object sender, RoutedEventArgs e)
        {
            if (cpuCurvePoints.Count <= 2)
            {
                _ = ShowDialogSafeAsync("Minimum Points Required", "You must have at least 2 points on the curve.");
                return;
            }

            // Remove middle point to keep end points
            int middleIndex = cpuCurvePoints.Count / 2;
            cpuCurvePoints.RemoveAt(middleIndex);
            gpuCurvePoints.RemoveAt(middleIndex);

            UpdatePointCountText();
            DrawFanCurve();
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

        #endregion
    }

    #region Helper Classes

    public class CurvePoint
    {
        public int Temp { get; set; }
        public int Rpm { get; set; }
    }

    public class PointTag
    {
        public CurvePoint Point { get; set; }
        public bool IsCpu { get; set; }
    }

    internal static class User32
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

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

    #endregion
}