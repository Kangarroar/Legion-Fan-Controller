using Microsoft.Win32;
using System;

namespace Lenovo_Fan_Controller
{
    public static class SettingsManager
    {
        private const string RegistryKeyPath = @"Software\LegionFanController";
        private const string ShowGpuTempKey = "ShowGpuTemp";
        private const string StartMinimizedKey = "StartMinimized";
        private const string UnlockMaxRpmKey = "UnlockMaxRpm";

        // Default values
        public const bool DefaultShowGpuTemp = true;
        public const bool DefaultStartMinimized = true;
        public const bool DefaultUnlockMaxRpm = false;

        public const int NormalMaxRpm = 4400;
        public const int UnlockedMaxRpm = 5000;

        /// <summary>
        ///////////////////////////////////
        /// </summary>
        public static bool GetShowGpuTemp()
        {
            return GetBoolSetting(ShowGpuTempKey, DefaultShowGpuTemp);
        }

        /// <summary>
        /// Sets whether GPU temperature controls should be shown
        /// </summary>
        public static void SetShowGpuTemp(bool value)
        {
            SetBoolSetting(ShowGpuTempKey, value);
        }

        /// <summary>
        /// Gets whether the app should start minimized to tray
        /// </summary>
        public static bool GetStartMinimized()
        {
            return GetBoolSetting(StartMinimizedKey, DefaultStartMinimized);
        }

        /// <summary>
        /// Sets tray minimized
        /// </summary>
        public static void SetStartMinimized(bool value)
        {
            SetBoolSetting(StartMinimizedKey, value);
        }

        /// <summary>
        /// max rpm
        /// </summary>
        public static bool GetUnlockMaxRpm()
        {
            return GetBoolSetting(UnlockMaxRpmKey, DefaultUnlockMaxRpm);
        }

        /// <summary>
        /// Sets whether max RPM values should be unlocked
        /// </summary>
        public static void SetUnlockMaxRpm(bool value)
        {
            SetBoolSetting(UnlockMaxRpmKey, value);
        }

        /// <summary>
        /// Gets the maximum RPM value based on settings
        /// </summary>
        public static int GetMaxRpm()
        {
            return GetUnlockMaxRpm() ? UnlockedMaxRpm : NormalMaxRpm;
        }

        private static bool GetBoolSetting(string key, bool defaultValue)
        {
            try
            {
                using var regKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (regKey?.GetValue(key) is int value)
                {
                    return value != 0;
                }
                return defaultValue;
            }
            catch (Exception ex)
            {
                return defaultValue;
            }
        }

        private static void SetBoolSetting(string key, bool value)
        {
            try
            {
                using var regKey = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                regKey?.SetValue(key, value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
            }
        }
    }
}

