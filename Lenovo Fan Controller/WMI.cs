using LegionFanController.Hardware;
using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;

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

    public static async Task<bool> SetPowerModeAndWaitAsync(LegionPowerMode mode, int legionGen, int timeoutMs = 2000)
    {
        try
        {
            // Select registers based on generation
            ushort acclAddr, declAddr, tempAddr;

            if (legionGen == 5)
            {
                acclAddr = 0xC3DC;
                declAddr = 0xC3DD;
                tempAddr = 0xC580;  // CPU first temp point
            }
            else
            {
                acclAddr = 0xC560;
                declAddr = 0xC570;
                tempAddr = 0xC580;
            }

            // Read values before mode change
            byte beforeAccl = ECUtils.ReadECByte(acclAddr);
            byte beforeDecl = ECUtils.ReadECByte(declAddr);
            byte beforeTemp = ECUtils.ReadECByte(tempAddr);

            // Call WMI
            var scope = new ManagementScope("\\\\.\\ROOT\\WMI");
            var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA"));

            bool invoked = false;
            foreach (ManagementObject instance in searcher.Get())
            {
                var inParams = instance.GetMethodParameters("SetSmartFanMode");
                inParams["Data"] = (int)mode;
                instance.InvokeMethod("SetSmartFanMode", inParams, null);
                invoked = true;
                break;
            }

            if (!invoked) return false;

            // Poll for change
            int elapsed = 0;
            int interval = 50;

            while (elapsed < timeoutMs)
            {
                await Task.Delay(interval);
                elapsed += interval;

                byte afterAccl = ECUtils.ReadECByte(acclAddr);
                byte afterDecl = ECUtils.ReadECByte(declAddr);
                byte afterTemp = ECUtils.ReadECByte(tempAddr);

                if (afterAccl != beforeAccl || afterDecl != beforeDecl || afterTemp != beforeTemp)
                {
                    Debug.WriteLine($"EC mode changed: Gen{legionGen} Accl {beforeAccl}->{afterAccl}, Decl {beforeDecl}->{afterDecl}, Temp {beforeTemp}->{afterTemp}");
                    await Task.Delay(100);
                    return true;
                }
            }

            Debug.WriteLine($"EC mode change timeout (Gen{legionGen})");
            return true;
        }
        catch (ManagementException ex)
        {
            Debug.WriteLine($"WMI Error: {ex.Message}");
            return false;
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
            LegionPowerMode.Balanced => "balanced",
            LegionPowerMode.Performance => "performance",
            LegionPowerMode.Custom => "performance",
            _ => "default"
        };
    }
}