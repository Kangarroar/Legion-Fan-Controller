using System;
using System.Runtime.InteropServices;

namespace LegionFanController.Hardware
{
    internal static class WinRing
    {
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

            _initialized = WinRing.InitializeOls();
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

            return WinRing.ReadIoPortByte(EC_DATA_PORT);
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

            return SanitizeRpm(raw);
        }

        // RIGHT FAN
        public static int ReadFan2Rpm()
        {
            ushort raw = ReadECWord(
                (ushort)ECRegister.FAN2_RPM_LSB,
                (ushort)ECRegister.FAN2_RPM_MSB
            );

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
            return ReadECByte((ushort)ECRegister.CPU_TEMP);
        }

        public static int ReadGpuTemp()
        {
            return ReadECByte((ushort)ECRegister.GPU_TEMP);
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
