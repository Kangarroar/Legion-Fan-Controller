using System;
using System.Runtime.InteropServices;

namespace LegionFanController.Hardware
{
    internal static class WinRing
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string librayName);

        [DllImport("WinRing0x64.dll")]
        public static extern bool InitializeOls();

        [DllImport("WinRing0x64.dll")]
        public static extern void DeinitializeOls();

        [DllImport("WinRing0x64.dll")]
        public static extern byte ReadIoPortByte(uint port);

        [DllImport("WinRing0x64.dll")]
        public static extern void WriteIoPortByte(uint port, byte value);
    }

    internal static class ECUtils
    {
        private const byte EC_ADDR_PORT = 0x4E;
        private const byte EC_DATA_PORT = 0x4F;

        private static bool _initialized = false;

        public static bool Init()
        {
            if (_initialized)
                return true;

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string subDir = System.IO.Path.Combine(baseDir, "Fan Control");
                string webRingDll = System.IO.Path.Combine(subDir, "WinRing0x64.dll");
                string webRingSys = System.IO.Path.Combine(subDir, "WinRing0x64.sys");
                string destSys = System.IO.Path.Combine(baseDir, "WinRing0x64.sys");

                System.Diagnostics.Debug.WriteLine($"Checking for WinRing files in: {subDir}");

                // Ensure .sys file is in the execution directory
                if (System.IO.File.Exists(webRingSys))
                {
                    if (!System.IO.File.Exists(destSys))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"opying {webRingSys} to {destSys}...");
                            System.IO.File.Copy(webRingSys, destSys);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to copy .sys file: {ex.Message}");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"WinRing0x64.sys DOEST NOT EXIST IN {subDir}");
                }

                System.Diagnostics.Debug.WriteLine($"Attempting to load WinRing0 from: {webRingDll}");
                if (System.IO.File.Exists(webRingDll))
                {
                    IntPtr ptr = WinRing.LoadLibrary(webRingDll);
                    System.Diagnostics.Debug.WriteLine($"LoadLibrary result: {ptr}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WinRing0x64.dll not found at expected path!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception loading DLL: {ex.Message}");
            }

            _initialized = WinRing.InitializeOls();
            System.Diagnostics.Debug.WriteLine($"WinRing.InitializeOls() returned: {_initialized}");
            return _initialized;
        }

        public static byte ReadECByte(ushort addr)
        {
            // Set high byte
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x11);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)((addr >> 8) & 0xFF));

            // Set low byte
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x10);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)(addr & 0xFF));

            // Read data
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x12);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);

            byte val = WinRing.ReadIoPortByte(EC_DATA_PORT);
            // System.Diagnostics.Debug.WriteLine($"ReadECByte(0x{addr:X}) = {val}"); // Too spammy for every byte
            return val;
        }

        private static ushort ReadECWord(ushort lsbAddr, ushort msbAddr)
        {
            byte lsb = ReadECByte(lsbAddr);
            byte msb = ReadECByte(msbAddr);
            return (ushort)((msb << 8) | lsb);
        }

        // =======================
        // Fan RPM
        // =======================


        // LEFT FAN
        public static int ReadFan1Rpm()
        {
            ushort raw = ReadECWord(
                (ushort)ECRegister.FAN1_RPM_LSB,
                (ushort)ECRegister.FAN1_RPM_MSB
            );
            System.Diagnostics.Debug.WriteLine($"Fan1 Raw: {raw}");
            return SanitizeRpm(raw);
        }

        // RIGHT FAN
        public static int ReadFan2Rpm()
        {
            ushort raw = ReadECWord(
                (ushort)ECRegister.FAN2_RPM_LSB,
                (ushort)ECRegister.FAN2_RPM_MSB
            );
            System.Diagnostics.Debug.WriteLine($"Fan2 Raw: {raw}");

            // 
            //raw /= 2;

            return SanitizeRpm(raw);
        }

        private static int SanitizeRpm(ushort rpm)
        {
            if (rpm == 0x0000 || rpm == 0xFFFF)
                return 0;

            if (rpm > 20000)
                return 0;

            return rpm;
        }

        // =======================
        // Temperatures (C)
        // =======================

        public static int ReadCpuTemp()
        {
            int val = ReadECByte((ushort)ECRegister.CPU_TEMP);
            System.Diagnostics.Debug.WriteLine($"CPU Temp Raw: {val}");
            return val;
        }

        public static int ReadGpuTemp()
        {
            int val = ReadECByte((ushort)ECRegister.GPU_TEMP);
            System.Diagnostics.Debug.WriteLine($"GPU Temp Raw: {val}");
            return val;
        }

        public static int ReadVrmTemp()
        {
            return ReadECByte((ushort)ECRegister.VRM_TEMP);
        }
    }

    internal enum ECRegister : ushort
    {
        // Temps (FF00D53X -> C53X)
        CPU_TEMP = 0xC538,
        GPU_TEMP = 0xC539,
        VRM_TEMP = 0xC53A,

        // Fan RPM
        FAN1_RPM_LSB = 0xC5E0,
        FAN1_RPM_MSB = 0xC5E1,
        FAN2_RPM_LSB = 0xC5E2,
        FAN2_RPM_MSB = 0xC5E3
    }
}



//This was used to debug only
//using System;
//using System.Threading;
//using LegionFanController.Hardware;
//
//namespace LegionFanController.Debug
//{
//    internal class Program
//    {
//        static void Main(string[] args)
//        {
//            Console.WriteLine("Starting EC debug...");
//
//            if (!ECUtils.Init())
//            {
//                Console.WriteLine("WinRing failed to initialize. Make sure you run as Administrator, x64, and WinRing0.sys is present.");
//                return;
//            }
//
//
//            Console.WriteLine("WinRing initialized");
//
//            Console.CancelKeyPress += (_, e) =>
//            {
//                e.Cancel = true;
//                Environment.Exit(0);
//            };
//
//            while (true)
//            {
//                try
//                {
//                    Console.Clear();
//                    int cpuTemp = ECUtils.ReadCpuTemp();
//                    int gpuTemp = ECUtils.ReadGpuTemp();
//                    int vrmTemp = ECUtils.ReadVrmTemp();
//                    int fan1 = ECUtils.ReadFan1Rpm();
//                    int fan2 = ECUtils.ReadFan2Rpm();
//                    byte value = ECUtils.ReadECByte(0xC538);
//                    Console.WriteLine($"Raw CPU Temp read: 0x{value:X2}");
//                    Console.WriteLine($"CPU Temp : {cpuTemp} C");
//                    Console.WriteLine($"GPU Temp : {gpuTemp} C");
//                    Console.WriteLine($"VRM Temp : {vrmTemp} C\n");
//
//                    Console.WriteLine($"Fan 1 RPM: {fan1}");
//                    Console.WriteLine($"Fan 2 RPM: {fan2}");
//
//                    
//
//                    Thread.Sleep(1000);
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Error reading EC: {ex.Message}");
//                    Console.ReadLine(); // pause so you can see it
//                    break;
//                }
//            }
//        }
//    }
//}
