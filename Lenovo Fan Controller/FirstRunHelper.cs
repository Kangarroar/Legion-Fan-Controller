using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;

namespace Lenovo_Fan_Controller
{
    public static class FirstRunHelper
    {
        private const string RegistryKeyPath = @"Software\LegionFanController";
        private const string FirstRunValueName = "HasShownWarning";

        /// <summary>
        /// Checks if this is the first time the application is running
        /// </summary>
        public static bool IsFirstRun()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                bool keyExists = key?.GetValue(FirstRunValueName) != null;
                bool isFirstRun = !keyExists;
                
                
                return isFirstRun;
            }
            catch (Exception ex)
            {
                return false; 
            }
        }

        /// <summary>
        /// Marks that the first run warning has been shown
        /// </summary>
        public static void MarkFirstRunComplete()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(FirstRunValueName, 1, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Shows the first-run warning dialog if needed
        /// </summary>
        public static async Task<bool> ShowFirstRunWarning(Microsoft.UI.Xaml.Window window)
        {
            if (!IsFirstRun())
            {
                return false; // Not first run, dialog not shown
            }

            
            // Show the warning dialog
            await ShowWarningDialog(window);
            
            // Mark as complete
            MarkFirstRunComplete();
            
            return true; // Dialog was shown
        }

        /// <summary>
        /// Shows the warning dialog (same pattern as ShowDialogSafeAsync)
        /// </summary>
        private static async Task ShowWarningDialog(Microsoft.UI.Xaml.Window window)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "⚠️ Important Warning",
                    Content = CreateWarningContent(),
                    PrimaryButtonText = "I Understand",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = window.Content?.XamlRoot
                };

                if (dialog.XamlRoot == null)
                {
                    await Task.Delay(100);
                    dialog.XamlRoot = window.Content?.XamlRoot;
                }

                if (dialog.XamlRoot != null)
                {
                    await dialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Creates the warning content for the dialog
        /// </summary>
        private static StackPanel CreateWarningContent()
        {
            var stackPanel = new StackPanel
            {
                Spacing = 16
            };

            // Warning text
            var warningText = new TextBlock
            {
                Text = "Legion Fan Controller allows you to modify your laptop's fan curves and temperature points.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            };

            var dangerText = new TextBlock
            {
                Text = "⚠️ DANGER: Incorrect fan settings can cause overheating and permanent hardware damage!",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.OrangeRed),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            };

            var instructionsText = new TextBlock
            {
                Text = "Only modify these settings if you:\n" +
                       "• Understand fan curves and thermal management\n" +
                       "• Know your laptop's thermal limits\n" +
                       "• Monitor temperatures while testing\n" +
                       "• Are willing to accept the risks",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            };

            var disclaimerText = new TextBlock
            {
                Text = "Any modifications you made are your own responsibility.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Opacity = 0.8
            };

            stackPanel.Children.Add(warningText);
            stackPanel.Children.Add(dangerText);
            stackPanel.Children.Add(instructionsText);
            stackPanel.Children.Add(disclaimerText);

            return stackPanel;
        }

        /// <summary>
        /// Resets the first run flag
        /// </summary>
        public static void ResetFirstRun()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
                if (key != null)
                {
                    var value = key.GetValue(FirstRunValueName);
                    if (value != null)
                    {
                        key.DeleteValue(FirstRunValueName);
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
    }
}

