using System;
using System.Diagnostics;
using System.Management;

public static class PowerModeHelper
{
    public enum LegionPowerMode
    {
        Quiet = 1,
        Balanced = 2,
        Performance = 3,
        Custom = 255
    }

    public static bool SetPowerMode(LegionPowerMode mode)
    {
        ManagementScope scope = null;
        ManagementObjectSearcher searcher = null;

        try
        {
            scope = new ManagementScope("\\\\.\\ROOT\\WMI");
            searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA"));

            foreach (ManagementObject instance in searcher.Get())
            {
                var inParams = instance.GetMethodParameters("SetSmartFanMode");
                inParams["Data"] = (int)mode;
                instance.InvokeMethod("SetSmartFanMode", inParams, null);
                return true;
            }
            return false;
        }
        catch (ManagementException ex)
        {
            Debug.WriteLine($"WMI Error (Set): {ex.Message}");
            return false;
        }
        finally
        {
            searcher?.Dispose();
            // ManagementScope doesn't need disposal in most versions
        }
    }

    public static LegionPowerMode GetCurrentPowerMode()
    {
        ManagementScope scope = null;
        ManagementObjectSearcher searcher = null;

        try
        {
            scope = new ManagementScope("\\\\.\\ROOT\\WMI");
            searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA"));

            foreach (ManagementObject instance in searcher.Get())
            {
                var outParams = instance.InvokeMethod("GetSmartFanMode", null, null);
                if (outParams != null && outParams["Data"] != null)
                {
                    return (LegionPowerMode)Convert.ToInt32(outParams["Data"]);
                }
            }
            return LegionPowerMode.Balanced;
        }
        catch (ManagementException ex)
        {
            Debug.WriteLine($"WMI Error (Get): {ex.Message}");
            return LegionPowerMode.Balanced;
        }
        finally
        {
            searcher?.Dispose();
        }
    }

    public static string PowerModeToProfile(LegionPowerMode mode)
    {
        return mode switch
        {
            LegionPowerMode.Quiet => "quiet",
            LegionPowerMode.Balanced => "default",
            LegionPowerMode.Performance => "performance",
            LegionPowerMode.Custom => "performance",
            _ => "default"
        };
    }
}