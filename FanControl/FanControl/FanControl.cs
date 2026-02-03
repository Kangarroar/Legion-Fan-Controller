using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using static FanControl.Utils.EC;

// Continued and Finished by Zandyz(Discord) https://github.com/Z4ndyz
// Modified version of https://github.com/SmokelessCPUv2/Lagon-Fan-EC-Control/blob/main/FanControl/Utils/Utils.cs
// Credits to SmokelessCpu for providing us with this file as open source. This is a better alternative to LFC as it uses
// WinRing in a more secure way and writting to the EC through the use of I/O ports. RwDrv.sys won't be used anymore compared to LFC
// nor will the service run 24/7
// Credits also to Underv0lti #5317 / akillisandalye (Discord) and SmokelessCPU for the EC mapping of Fan System
// File will keep previous enums by Smokeless in his test run app used for authenticity 
// This .cs file will allow to be built as an .exe and paired with WinRing0x64.dll to offer Fan Control on Legions Gen 5 and Gen 6
// Setting up the Fan Curve will be done through a config .txt file

namespace FanControl.Utils
{
    internal static class WinRing
    {
        //DLL
        public static bool WinRingInitOk = false;
        [DllImport("WinRing0x64.dll")]
        public static extern DLL_Error_Code GetDllStatus();
        [DllImport("WinRing0x64.dll")]
        public static extern UInt64 GetDllVersion(ref byte major, ref byte minor, ref byte revision, ref byte release);
        [DllImport("WinRing0x64.dll")]
        public static extern UInt64 GetDriverVersion(ref byte major, ref byte minor, ref byte revision, ref byte release);
        [DllImport("WinRing0x64.dll")]
        public static extern DriverType GetDriverType();
        [DllImport("WinRing0x64.dll")]
        public static extern bool InitializeOls();
        [DllImport("WinRing0x64.dll")]
        public static extern void DeinitializeOls();

        [DllImport("WinRing0x64.dll")]
        public static extern byte ReadIoPortByte(UInt32 address);

        [DllImport("WinRing0x64.dll")]
        public static extern void WriteIoPortByte(UInt32 port, byte value);

    }


    internal static class EC
    {
        //=======================================EC Direct Access interface=================================
        //Port Config:
        //  BADRSEL(0x200A) bit1-0  Addr    Data
        //                  00      2Eh     2Fh
        //                  01      4Eh     4Fh
        //
        //              01      4Eh     4Fh
        //  ITE-EC Ram Read/Write Algorithm:
        //  Addr    w   0x2E
        //  Data    w   0x11
        //  Addr    w   0x2F
        //  Data    w   high byte
        //  Addr    w   0x2E
        //  Data    w   0x10
        //  Addr    w   0x2F
        //  Data    w   low byte
        //  Addr    w   0x2E
        //  Data    w   0x12
        //  Addr    w   0x2F
        //  Data    rw  value

        public static void DirectECWrite(byte EC_ADDR_PORT, byte EC_DATA_PORT, UInt16 Addr, byte data)
        {
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x11);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)((Addr >> 8) & 0xFF));

            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x10);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)(Addr & 0xFF));

            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x12);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            WinRing.WriteIoPortByte(EC_DATA_PORT, data);
        }

        public static void DirectECWriteArray(byte EC_ADDR_PORT, byte EC_DATA_PORT, UInt16 Addr_base, byte[] data)
        {
            for (var i = 0; i < data.Count(); i++)
            {
                var Addr = (ushort)(Addr_base + i);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                WinRing.WriteIoPortByte(EC_DATA_PORT, 0x11);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)((Addr >> 8) & 0xFF));

                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                WinRing.WriteIoPortByte(EC_DATA_PORT, 0x10);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)(Addr & 0xFF));

                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                WinRing.WriteIoPortByte(EC_DATA_PORT, 0x12);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                WinRing.WriteIoPortByte(EC_DATA_PORT, data[i]);
            }
        }

        public static byte DirectECRead(byte EC_ADDR_PORT, byte EC_DATA_PORT, UInt16 Addr)
        {
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x11);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)((Addr >> 8) & 0xFF));

            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x10);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)(Addr & 0xFF));

            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
            WinRing.WriteIoPortByte(EC_DATA_PORT, 0x12);
            WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
            return WinRing.ReadIoPortByte(EC_DATA_PORT);
        }

        public static byte[] DirectECReadArray(byte EC_ADDR_PORT, byte EC_DATA_PORT, UInt16 Addr_base, int size)
        {
            var buffer = new byte[size];
            for (var i = 0; i < size; i++)
            {
                var Addr = (ushort)(Addr_base + i);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                WinRing.WriteIoPortByte(EC_DATA_PORT, 0x11);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)((Addr >> 8) & 0xFF));

                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                WinRing.WriteIoPortByte(EC_DATA_PORT, 0x10);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                WinRing.WriteIoPortByte(EC_DATA_PORT, (byte)(Addr & 0xFF));

                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                WinRing.WriteIoPortByte(EC_DATA_PORT, 0x12);
                WinRing.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                buffer[i] = WinRing.ReadIoPortByte(EC_DATA_PORT);
            }
            return buffer;
        }

        public enum ITE_PORT : byte
        {
            EC_ADDR_PORT = 0x4E,
            EC_DATA_PORT = 0x4F,
        }
        public enum ITE_REGISTER_MAP : UInt16
        {
            ECINDAR0 = 0x103B, // Values from Smokeless Project, i kept them there but
            ECINDAR1 = 0x103C, // i'll use another set of addresses for fan control
            ECINDAR2 = 0x103D,
            ECINDAR3 = 0x103E,
            ECINDDR = 0x103F,
            GPDRA = 0x1601,
            GPCRA0 = 0x1610,
            GPCRA1 = 0x1611,
            GPCRA2 = 0x1612,
            GPCRA3 = 0x1613,
            GPCRA4 = 0x1614,
            GPCRA5 = 0x1615,
            GPCRA6 = 0x1616,
            GPCRA7 = 0x1617,
            GPOTA = 0x1671,
            GPDMRA = 0x1661,
            DCR0 = 0x1802,
            DCR1 = 0x1803,
            DCR2 = 0x1804,
            DCR3 = 0x1805,
            DCR4 = 0x1806,
            DCR5 = 0x1807,
            DCR6 = 0x1808,
            DCR7 = 0x1809,
            CTR2 = 0x1842,
            ECHIPID1 = 0x2000,
            ECHIPID2 = 0x2001,
            ECHIPVER = 0x2002,
            ECDEBUG = 0x2003,
            EADDR = 0x2100,
            EDAT = 0x2101,
            ECNT = 0x2102,
            ESTS = 0x2103,
            FW_VER = 0xC2C7, // Mapping out addresses from Smokeless' App, will use different enum for my purposes but here's what i found
            FAN_CUR_POINT = 0xC534, // Current point from fan curve selected by EC
            FAN_POINT = 0xC535, // Fan curve endpoint
            FAN1_BASE = 0xC540, // Fan1 Field Start addr
            FAN2_BASE = 0xC550, // Fan2 Field Start addr
            FAN_ACC_BASE = 0xC560, // Fan Accl Field addr
            FAN_DEC_BASE = 0xC570, // Fan Deccl Field addr
            CPU_TEMP = 0xC580, // Cpu ramp up addr field
            CPU_TEMP_HYST = 0xC590, // Cpu ramp down addr field
            GPU_TEMP = 0xC5A0, // Gpu ramp up addr field
            GPU_TEMP_HYST = 0xC5B0, // Gpu ramp down addr field
            VRM_TEMP = 0xC5C0, // Vrm/Hst ramp up addr field
            VRM_TEMP_HYST = 0xC5D0, // Vrm/Hst ramp down addr field
            CPU_TEMP_EN = 0xC631, // Unknown on gen 6 i get a 0 value
            GPU_TEMP_EN = 0xC632, // Unknown
            VRM_TEMP_EN = 0xC633, // Unknown
            FAN1_ACC_TIMER = 0xC3DA, // Didn't test
            FAN2_ACC_TIMER = 0xC3DB, // Didn't test
            FAN1_CUR_ACC = 0xC3DC, // Fan1 Accl gen5
            FAN1_CUR_DEC = 0xC3DD, // Fan1 Deccl gen5
            FAN2_CUR_ACC = 0xC3DE, // Fan2 Accl gen5
            FAN2_CUR_DEC = 0xC3DF, // Fan2 Deccl gen5
            FAN1_RPM_LSB = 0xC5E0, // Fan1 & Fan2 RPM LSB & MSB
            FAN1_RPM_MSB = 0xC5E1, // ^
            FAN2_RPM_LSB = 0xC5E2, // ^
            FAN2_RPM_MSB = 0xC5E3, // ^


            // Fan Control

            /*# Steps and Info required to set a Fan Curve

            # 1) Setting left & right fan acceleration and deceleration values
            # 2) Setting the number of points in the curve
            # 3) Setting left & right fan RPM values and number of points
            # 4) Setting temperature target points for when to ramp up or ramp down fans to a specific RPM for CPU, GPU and Heatsink for hysteresis
            # 5) (Optional for Gen6, required for Gen5 according to Underv0lti) Overriding Keyboard RGB Temp Trigger Value
            # 6) Set Fan speed change to be near instant as opposed to 100RPM every 5 seconds as Lenovo default for registering the new table*/

            //---------------------------------------------------------------------------------------------------------------------------------------

            // Step 1
            // FAN ACC/DEC Addresses -> This is where to set accl/decll speeds, values closer to 0 (in hex) will lead to instant changes but might
            // wear out fans. A value of 2 (2 seconds for example until it ramps up/down to the next fan step.) Use values between 1 and 0F(15).

            //---------------------------------------------------------------------------------------------------------------------------------------

            FAN1_ACC_GEN5 = 0xC3DC, // GEN 5 
            FAN1_DEC_GEN5 = 0xC3DD,
            FAN2_ACC_GEN5 = 0xC3DE,
            FAN2_DEC_GEN5 = 0xC3DF, // GEN 5 ACC/DEC VALUES

            FAN_ACC_GEN6 = 0xC560, // GEN 6 ACC/DEC VALUES FROM 0 TO 9 (10 usable points, 11th hard stop point marker bsods cant touch it)
            FAN_DEC_GEN6 = 0xC570,

            //---------------------------------------------------------------------------------------------------------------------------------------

            // Step 2
            // FAN Points used for the curve

            FAN_POINTS_NO = 0xC535, // 9 usable POINTS (a value of 0A) is the maximum, 10th point will be used by the EC, more points result in BSOD

            //---------------------------------------------------------------------------------------------------------------------------------------

            // Step 3
            // FAN RPM Addresses to write the RPM values to (HEX number 26 hex => 38 dec => 3800RPM)

            FAN1_RPM_ST_ADDR = 0xC551,
            FAN2_RPM_ST_ADDR = 0xC541,

            // For each point up to 0xC559 or 0xC549 depending on how many points are used
            // an rpm value must be specified like mentioned above.

            //---------------------------------------------------------------------------------------------------------------------------------------

            // Step 4
            // CPU RAMP UP AND RAMP DOWN Temperature THRESHOLDS

            CPU_RAMP_UP_THRS = 0xC580,
            CPU_RAMP_DOWN_THRS = 0xC591,

            // Starting address for ramp up last digit goes from 0 to 9
            // Starting address for ramp down last digit goes from 1 to 9. Ramp down
            // needs to be 1 value higher than ramp up and ramp up has to have it's
            // last digit equal to the last digit of the last point of ramp down and have
            // it's hex value equal to 7F which is used by Lenovo as the ignore value.
            // Same as before the value is a hex byte for example w 0xC580 37 would mean the first Ramp Up point activates at 55 degrees Celsius

            // GPU RAMP UP AND RAMP DOWN Temperature THRESHOLDS

            GPU_RAMP_UP_THRS = 0xC5A0,
            GPU_RAMP_DOWN_THRS = 0xC5B1,

            // HEATSINK RAMP UP AND RAMP DOWN Temperature THRESHOLDS

            HST_RAMP_UP_THRS = 0xC5C0,
            HST_RAMP_DOWN_THRS = 0xC5D1,

            // Set 7F(127) for ignoring values in the temperature threshold tables.

            //---------------------------------------------------------------------------------------------------------------------------------------

            // Step 5
            // Disable Fans from turning On when RGB keyboard is turned on

            STOP_RGB_FAN_WAKE = 0xC64D, // If this value is written to hex 25 it'll disable fans from turning on when RGB keyboard is turned on
            // at low temps/idling

            //---------------------------------------------------------------------------------------------------------------------------------------

            // Step 6
            // Set up fan counter registers to allow the change of fan tables faster internally
            // Setting to 64 for example will lead to a quick change

            FAN_TABLE_CHG_COUNTER = 0xC5FE,
            FAN_TABLE_CHG_COUNTER_SEC = 0xC5FF,

            //---------------------------------------------------------------------------------------------------------------------------------------

        }
    }

    enum DLL_Error_Code
    {
        OLS_DLL_NO_ERROR = 0,
        OLS_DLL_UNSUPPORTED_PLATFORM = 1,
        OLS_DLL_DRIVER_NOT_LOADED = 2,
        OLS_DLL_DRIVER_NOT_FOUND = 3,
        OLS_DLL_DRIVER_UNLOADED = 4,
        OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK = 5,
        OLS_DLL_UNKNOWN_ERROR = 9
    }

    enum DriverType
    {
        OLS_DRIVER_TYPE_UNKNOWN = 0,
        OLS_DRIVER_TYPE_WIN_9X = 1,
        OLS_DRIVER_TYPE_WIN_NT = 2,
        OLS_DRIVER_TYPE_WIN_NT4 = 3,
        OLS_DRIVER_TYPE_WIN_NT_X64 = 4,
        OLS_DRIVER_TYPE_WIN_NT_IA64 = 5
    }

    internal static class FanControl
{
    private static TempHysteresis hysteresis = new TempHysteresis();
    private static int lastAppliedPoint = -1;
        private static async Task Main()
        {
            while (true)
            {
                // Initialize WinRing
                WinRing.WinRingInitOk = WinRing.InitializeOls();
                // Check if WinRing initialized properly, if it did then Write & Read from the EC
                if (WinRing.WinRingInitOk)
                {
                    ApplyFanCurve();
                }
                else
                {
                    // Print a message indicating initialization failure
                    Console.WriteLine("WinRing initialization failed. Check if the driver is loaded.");
                }

                // Deallocate info related to WinRing
                WinRing.DeinitializeOls();

                // Wait for 10 seconds before looping back
                await Task.Delay(15000); // 15000 milliseconds = 15 seconds
            }
        }
        public static void ApplyFanCurve()
        {
            byte ecAddrPort = (byte)EC.ITE_PORT.EC_ADDR_PORT;
            byte ecDataPort = (byte)EC.ITE_PORT.EC_DATA_PORT;
            int powerModeWMI = 0;
            string filePath;
            powerModeWMI = ExtractPowerModeWMI(); // Get WMI Power Mode
            filePath = GetFilePathBasedOnPowerMode(powerModeWMI); // Select the Right Fan Config based on WMI Power Mode
            var extractedValues = ExtractValuesFromFile(filePath); // Get the Necesarry values from the file
                                                                   // Access the extracted values and perform type casting
            int legionGen = (int)extractedValues["legion_gen"];
            int fanCurvePoints = (int)extractedValues["fan_curve_points"];
            int fanAcclValue = (int)extractedValues["fan_accl_value"];
            int fanDecclValue = (int)extractedValues["fan_deccl_value"];
            int[] fanRpmPointsValue = (int[])extractedValues["fan_rpm_points"];
            int[] cpuTempsRampUp = (int[])extractedValues["cpu_temps_ramp_up"];
            int[] cpuTempsRampDown = (int[])extractedValues["cpu_temps_ramp_down"];
            int[] gpuTempsRampUp = (int[])extractedValues["gpu_temps_ramp_up"];
            int[] gpuTempsRampDown = (int[])extractedValues["gpu_temps_ramp_down"];
            int[] hstTempsRampUp = (int[])extractedValues["hst_temps_ramp_up"];
            int[] hstTempsRampDown = (int[])extractedValues["hst_temps_ramp_down"];
            // Assuming fanCurvePoints, fanAcclValue, fanDecclValue are single integers
            byte fanCurvePointsByte = (byte)fanCurvePoints;
            byte fanAcclValueByte = (byte)fanAcclValue;
            byte fanDecclValueByte = (byte)fanDecclValue;
            // Convert and divide fanRpmPointsValue by 100
            byte[] fanRpmPointsBytes = fanRpmPointsValue.Select(value => (byte)(value / 100)).ToArray();
            // Convert other arrays to bytes
            byte[] cpuTempsRampUpBytes = cpuTempsRampUp.Select(value => (byte)value).ToArray();
            byte[] cpuTempsRampDownBytes = cpuTempsRampDown.Select(value => (byte)value).ToArray();
            byte[] gpuTempsRampUpBytes = gpuTempsRampUp.Select(value => (byte)value).ToArray();
            byte[] gpuTempsRampDownBytes = gpuTempsRampDown.Select(value => (byte)value).ToArray();
            byte[] hstTempsRampUpBytes = hstTempsRampUp.Select(value => (byte)value).ToArray();
            byte[] hstTempsRampDownBytes = hstTempsRampDown.Select(value => (byte)value).ToArray();
            byte fanPointCounterMaxSize = 0xA; // Max Hard Limit of the Fan Points Counter
            UInt16[] startingAddresses = { (UInt16)EC.ITE_REGISTER_MAP.FAN1_RPM_ST_ADDR, (UInt16)EC.ITE_REGISTER_MAP.FAN2_RPM_ST_ADDR };
            byte fillValueRampUp = 0x7F; // The fill value for Temperature Ramp Up (This is the Lenovo Ignore value)
            byte stopRGBFanWakeValue = 0x25; // Value to disable fans when RGB keyboard is turned on
            byte fanTableChangeCounterValue = 0x64;

            WriteFanAcclDecclToEC(ecAddrPort, ecDataPort, legionGen, fanAcclValueByte, fanDecclValueByte);
            WriteFanPointCounterMaxSizeToEC(ecAddrPort, ecDataPort, fanPointCounterMaxSize);
            WriteFanRpmPointsToEC(ecAddrPort, ecDataPort, fanRpmPointsBytes, fanCurvePoints, startingAddresses);
            WriteTemperatureRampToEC(ecAddrPort, ecDataPort, cpuTempsRampUpBytes, (UInt16)ITE_REGISTER_MAP.CPU_RAMP_UP_THRS, 10, fillValueRampUp); // CPU Ramp Up
            byte fillValueRampDown = cpuTempsRampDownBytes.LastOrDefault(); // The fill value for CPU Ramp Down
            WriteTemperatureRampToEC(ecAddrPort, ecDataPort, cpuTempsRampDownBytes, (UInt16)ITE_REGISTER_MAP.CPU_RAMP_DOWN_THRS, 10, fillValueRampDown); // CPU Ramp Down
            WriteTemperatureRampToEC(ecAddrPort, ecDataPort, gpuTempsRampUpBytes, (UInt16)ITE_REGISTER_MAP.GPU_RAMP_UP_THRS, 10, fillValueRampUp); // GPU Ramp Up
            fillValueRampDown = gpuTempsRampDownBytes.LastOrDefault(); // The fill value for CPU Ramp Down
            WriteTemperatureRampToEC(ecAddrPort, ecDataPort, gpuTempsRampDownBytes, (UInt16)ITE_REGISTER_MAP.GPU_RAMP_DOWN_THRS, 10, fillValueRampDown); // GPU Ramp Down
            WriteTemperatureRampToEC(ecAddrPort, ecDataPort, hstTempsRampUpBytes, (UInt16)ITE_REGISTER_MAP.HST_RAMP_UP_THRS, 10, fillValueRampUp); // HST Ramp Up
            fillValueRampDown = hstTempsRampDownBytes.LastOrDefault(); // The fill value for CPU Ramp Down
            WriteTemperatureRampToEC(ecAddrPort, ecDataPort, hstTempsRampDownBytes, (UInt16)ITE_REGISTER_MAP.HST_RAMP_DOWN_THRS, 10, fillValueRampDown); // HST Ramp Down  
            WriteStopRGBFanWakeToEC(ecAddrPort, ecDataPort, stopRGBFanWakeValue);
            WriteFanTableChangeCounterToEC(ecAddrPort, ecDataPort, fanTableChangeCounterValue);

            /*            DebugReadECAddresses( // Decomment this if you don't need to double check the data and the program works correctly
                            legionGen,
                            fanCurvePoints,
                            fanAcclValue,
                            fanDecclValue,
                            fanRpmPointsValue,
                            cpuTempsRampUp,
                            cpuTempsRampDown,
                            gpuTempsRampUp,
                            gpuTempsRampDown,
                            hstTempsRampUp,
                            hstTempsRampDown,
                            ecAddrPort,
                            ecDataPort,
                            powerModeWMI,
                            filePath
                       );

                        // Keep the console window open for user observation (Debug Reading)
                        Console.ReadLine();*/
        }

        private static void WriteFanTableChangeCounterToEC(byte ecAddrPort, byte ecDataPort, byte fanTableChangeCounterValue)
        {
            UInt16 fanTableChangeCounterAddress = (UInt16)ITE_REGISTER_MAP.FAN_TABLE_CHG_COUNTER;
            EC.DirectECWrite(ecAddrPort, ecDataPort, fanTableChangeCounterAddress, fanTableChangeCounterValue);

            UInt16 fanTableChangeCounterSecAddress = (UInt16)ITE_REGISTER_MAP.FAN_TABLE_CHG_COUNTER_SEC;
            EC.DirectECWrite(ecAddrPort, ecDataPort, fanTableChangeCounterSecAddress, fanTableChangeCounterValue);
        }

        private static void WriteStopRGBFanWakeToEC(byte ecAddrPort, byte ecDataPort, byte stopRGBFanWakeValue)
        {
            UInt16 stopRGBFanWakeAddress = (UInt16)ITE_REGISTER_MAP.STOP_RGB_FAN_WAKE;
            EC.DirectECWrite(ecAddrPort, ecDataPort, stopRGBFanWakeAddress, stopRGBFanWakeValue);
        }

        private static void WriteTemperatureRampToEC(byte ecAddrPort, byte ecDataPort, byte[] temperatureRampBytes, UInt16 startAddress, int rampSize, byte fillValue)
        {
            int numberOfBytesToWrite = Math.Min(rampSize, temperatureRampBytes.Length);

            // Create a new array with the bytes to write
            byte[] bytesToWrite = new byte[rampSize];

            // Copy the bytes from temperatureRampBytes to bytesToWrite
            Array.Copy(temperatureRampBytes, bytesToWrite, numberOfBytesToWrite);

            // Fill the remaining bytes with the specified fillValue
            for (int i = numberOfBytesToWrite; i < rampSize; i++)
            {
                bytesToWrite[i] = fillValue;
            }

            // Iterate through the range and write bytes to EC
            for (int i = 0; i < rampSize; i++)
            {
                // Calculate the current address to write
                UInt16 currentAddress = (UInt16)(startAddress + i);

                // Write to the current address
                EC.DirectECWrite(ecAddrPort, ecDataPort, currentAddress, bytesToWrite[i]);
            }
        }

        public static void WriteFanRpmPointsToEC(byte ecAddrPort, byte ecDataPort, byte[] fanRpmPointsBytes, int fanCurvePoints, UInt16[] startingAddresses)
        {
            // Determine the number of bytes to write (up to 9, limited by fanCurvePoints)
            int numberOfBytesToWrite = Math.Min(fanCurvePoints, 9);

            // Compute the lastByteValue by taking the last value of the fanRpmPointsBytes
            byte lastByteValue = numberOfBytesToWrite > 0 ? fanRpmPointsBytes[numberOfBytesToWrite - 1] : (byte)0;

            // Create a byte array to hold the values to be written
            byte[] valuesToWrite = new byte[9];

            // Copy the fanRpmPointsBytes to the valuesToWrite array
            Array.Copy(fanRpmPointsBytes, valuesToWrite, numberOfBytesToWrite);

            // If there are fewer than 9 bytes to write, complete the rest with the lastByteValue
            for (int i = numberOfBytesToWrite; i < 9; i++)
            {
                valuesToWrite[i] = lastByteValue;
            }

            // Loop through the starting addresses and write the values
            for (int i = 0; i < startingAddresses.Length; i++)
            {
                UInt16 currentAddress = (UInt16)startingAddresses[i];

                // Write to the current address
                EC.DirectECWriteArray(ecAddrPort, ecDataPort, currentAddress, valuesToWrite);
            }
        }


        public static void WriteFanPointCounterMaxSizeToEC(byte ecAddrPort, byte ecDataPort, byte fanPointCounterMaxSize)
        {
            // Address for FAN_POINTS_NO
            UInt16 fanPointsNoAddress = (UInt16)ITE_REGISTER_MAP.FAN_POINTS_NO;

            // Write to the FAN_POINTS_NO address
            EC.DirectECWrite(ecAddrPort, ecDataPort, fanPointsNoAddress, fanPointCounterMaxSize);
        }


        private static void WriteFanAcclDecclToEC(byte ecAddrPort, byte ecDataPort, int legionGen, byte fanAcclValueByte, byte fanDecclValueByte)
        {
            // Check if legionGen is equal to 5
            if (legionGen == 5)
            {
                // Addresses for GEN 5 fan acceleration and deceleration values
                UInt16 fan1AccGen5Address = (UInt16)ITE_REGISTER_MAP.FAN1_ACC_GEN5;
                UInt16 fan1DecGen5Address = (UInt16)ITE_REGISTER_MAP.FAN1_DEC_GEN5;
                UInt16 fan2AccGen5Address = (UInt16)ITE_REGISTER_MAP.FAN2_ACC_GEN5;
                UInt16 fan2DecGen5Address = (UInt16)ITE_REGISTER_MAP.FAN2_DEC_GEN5;

                // Write fan1 acceleration value
                EC.DirectECWrite(ecAddrPort, ecDataPort, fan1AccGen5Address, fanAcclValueByte);
                // Write fan1 deceleration value
                EC.DirectECWrite(ecAddrPort, ecDataPort, fan1DecGen5Address, fanDecclValueByte);

                // Write fan2 acceleration value
                EC.DirectECWrite(ecAddrPort, ecDataPort, fan2AccGen5Address, fanAcclValueByte);
                // Write fan2 deceleration value
                EC.DirectECWrite(ecAddrPort, ecDataPort, fan2DecGen5Address, fanDecclValueByte);

                //Console.WriteLine("GEN 5 Fan Acceleration and Deceleration values written successfully."); // Debug
            }
            else
            {
                // Addresses for GEN 6 fan acceleration and deceleration values
                UInt16 fanAccGen6StartAddress = (UInt16)ITE_REGISTER_MAP.FAN_ACC_GEN6;
                UInt16 fanDecGen6StartAddress = (UInt16)ITE_REGISTER_MAP.FAN_DEC_GEN6;

                // Number of addresses to span
                int numberOfAddresses = 10; // 0xC569 - 0xC560 + 1 = 10

                // Create arrays with acceleration and deceleration values
                byte[] fanAccValues = Enumerable.Repeat((byte)fanAcclValueByte, numberOfAddresses).ToArray();
                byte[] fanDecValues = Enumerable.Repeat((byte)fanDecclValueByte, numberOfAddresses).ToArray();

                // Write to the GEN 6 fan acceleration addresses
                EC.DirectECWriteArray(ecAddrPort, ecDataPort, fanAccGen6StartAddress, fanAccValues);

                // Write to the GEN 6 fan deceleration addresses
                EC.DirectECWriteArray(ecAddrPort, ecDataPort, fanDecGen6StartAddress, fanDecValues);

                //Console.WriteLine("GEN 6 Fan Acceleration and Deceleration values written successfully."); // Debug
            }
        }


        private static void DebugReadECAddresses(
            int legionGen,
            int fanCurvePoints,
            int fanAcclValue,
            int fanDecclValue,
            int[] fanRpmPointsValue,
            int[] cpuTempsRampUp,
            int[] cpuTempsRampDown,
            int[] gpuTempsRampUp,
            int[] gpuTempsRampDown,
            int[] hstTempsRampUp,
            int[] hstTempsRampDown,
            byte ecAddrPort,
            byte ecDataPort,
            int powerModeWMI,
            string filePath)
        {


            // Debug Section

            // Print EC_ADDR_PORT and EC_DATA_PORT values
            Console.WriteLine($"EC_ADDR_PORT: 0x{ecAddrPort:X}");
            Console.WriteLine($"EC_DATA_PORT: 0x{ecDataPort:X}");

            Console.WriteLine($"Power mode: {powerModeWMI}"); // Print Power Mode
            Console.WriteLine($"File to extract fan curves from: {filePath}"); // Print File Path

            // Display the extracted values (optional)
            Console.WriteLine($"Legion Gen: {legionGen}");
            Console.WriteLine($"Fan Curve Points: {fanCurvePoints}");
            Console.WriteLine($"Fan Accl Value: {fanAcclValue}");
            Console.WriteLine($"Fan Decl Value: {fanDecclValue}");
            Console.WriteLine($"Fan RPM Points: {string.Join(", ", fanRpmPointsValue)}");
            Console.WriteLine($"CPU Temps Ramp Up: {string.Join(", ", cpuTempsRampUp)}");
            Console.WriteLine($"CPU Temps Ramp Down: {string.Join(", ", cpuTempsRampDown)}");
            Console.WriteLine($"GPU Temps Ramp Up: {string.Join(", ", gpuTempsRampUp)}");
            Console.WriteLine($"GPU Temps Ramp Down: {string.Join(", ", gpuTempsRampDown)}");
            Console.WriteLine($"HST Temps Ramp Up: {string.Join(", ", hstTempsRampUp)}");
            Console.WriteLine($"HST Temps Ramp Down: {string.Join(", ", hstTempsRampDown)}"); // Debug Values


            // Debug Test Read Addresses

            Console.WriteLine("Check if Addresses are handled right");

            if (legionGen == 5)
            {
                byte fan1AccGen5 = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN1_ACC_GEN5);
                byte fan1DecGen5 = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN1_DEC_GEN5);
                byte fan2AccGen5 = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN2_ACC_GEN5);
                byte fan2DecGen5 = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN2_DEC_GEN5);

                Console.WriteLine($"FAN1_ACC_GEN5: {fan1AccGen5}");
                Console.WriteLine($"FAN1_DEC_GEN5: {fan1DecGen5}");
                Console.WriteLine($"FAN2_ACC_GEN5: {fan2AccGen5}");
                Console.WriteLine($"FAN2_DEC_GEN5: {fan2DecGen5}");
            }
            else
            {
                byte[] fan1AccGen6Values = EC.DirectECReadArray(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN_ACC_GEN6, 10);
                byte[] fan1DecGen6Values = EC.DirectECReadArray(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN_DEC_GEN6, 10);

                Console.WriteLine("FAN_ACC_GEN6 Values:");
                for (int i = 0; i < fan1AccGen6Values.Length; i++)
                {
                    Console.WriteLine($"Index {i}: {fan1AccGen6Values[i]}");
                }

                Console.WriteLine("FAN_DEC_GEN6 Values:");
                for (int i = 0; i < fan1DecGen6Values.Length; i++)
                {
                    Console.WriteLine($"Index {i}: {fan1DecGen6Values[i]}");
                }
            }

            byte fanPointsNo = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN_POINTS_NO);
            Console.WriteLine($"Fan Points Number (Hex): 0x{fanPointsNo:X}");

            // Print integer value
            int fanPointsInt = fanPointsNo;
            Console.WriteLine($"Fan Points Number (Int): {fanPointsInt}");

            // 

            // Fan RPM Values Debug

            // Read and print Fan1 RPM values
            // Variables to store the values during the loop
            string hexValues = "";
            string intValues = "";
            string multipliedValues = "";

            for (int i = 0; i < 9; i++)
            {
                UInt16 fan1RpmAddress = (UInt16)((int)ITE_REGISTER_MAP.FAN1_RPM_ST_ADDR + i);
                byte fan1RpmValue = EC.DirectECRead(ecAddrPort, ecDataPort, fan1RpmAddress);

                // Multiply the RPM value by 100
                int multipliedValue = fan1RpmValue * 100;

                // Concatenate the values for each iteration
                hexValues += $"0x{fan1RpmValue:X} ";
                intValues += $"{fan1RpmValue} ";
                multipliedValues += $"{multipliedValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"Fan1 RPM (Hex): {hexValues.Trim()}, (Int): {intValues.Trim()}, * 100: {multipliedValues.Trim()}");

            // Read and print Fan2 RPM values
            // Variables to store the values during the loop
            hexValues = "";
            intValues = "";
            multipliedValues = "";

            for (int i = 0; i < 9; i++)
            {
                UInt16 fan2RpmAddress = (UInt16)((int)ITE_REGISTER_MAP.FAN2_RPM_ST_ADDR + i);
                byte fan2RpmValue = EC.DirectECRead(ecAddrPort, ecDataPort, fan2RpmAddress);

                // Multiply the RPM value by 100
                int multipliedValue = fan2RpmValue * 100;

                // Concatenate the values for each iteration
                hexValues += $"0x{fan2RpmValue:X} ";
                intValues += $"{fan2RpmValue} ";
                multipliedValues += $"{multipliedValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"Fan2 RPM (Hex): {hexValues.Trim()}, (Int): {intValues.Trim()}, * 100: {multipliedValues.Trim()}");

            // Fan RPM Values
            //

            // Cpu Ramp Up and Ramp Down Temperatures Debug //
            // CPU Temperature Thresholds Debug
            // Read and print CPU Ramp Up thresholds
            // Variables to store the values during the loop
            string cpuRampUpHexValues = "";
            string cpuRampUpIntValues = "";

            for (int i = 0; i < 10; i++)
            {
                UInt16 cpuRampUpAddress = (UInt16)((int)ITE_REGISTER_MAP.CPU_RAMP_UP_THRS + i);
                byte cpuRampUpValue = EC.DirectECRead(ecAddrPort, ecDataPort, cpuRampUpAddress);

                // Concatenate the values for each iteration
                cpuRampUpHexValues += $"0x{cpuRampUpValue:X} ";
                cpuRampUpIntValues += $"{cpuRampUpValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"CPU Ramp Up Thresholds (Hex): {cpuRampUpHexValues.Trim()}, (Int): {cpuRampUpIntValues.Trim()}");

            // Read and print CPU Ramp Down thresholds
            // Variables to store the values during the loop
            string cpuRampDownHexValues = "";
            string cpuRampDownIntValues = "";

            for (int i = 0; i < 10; i++)
            {
                UInt16 cpuRampDownAddress = (UInt16)((int)ITE_REGISTER_MAP.CPU_RAMP_DOWN_THRS + i);
                byte cpuRampDownValue = EC.DirectECRead(ecAddrPort, ecDataPort, cpuRampDownAddress);

                // Concatenate the values for each iteration
                cpuRampDownHexValues += $"0x{cpuRampDownValue:X} ";
                cpuRampDownIntValues += $"{cpuRampDownValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"CPU Ramp Down Thresholds (Hex): {cpuRampDownHexValues.Trim()}, (Int): {cpuRampDownIntValues.Trim()}");

            // GPU Ramp Up and Ramp Down Temperatures Debug //

            // GPU Temperature Thresholds Debug

            // Read and print GPU Ramp Up thresholds
            // Variables to store the values during the loop
            string gpuRampUpHexValues = "";
            string gpuRampUpIntValues = "";

            for (int i = 0; i < 10; i++)
            {
                UInt16 gpuRampUpAddress = (UInt16)((int)ITE_REGISTER_MAP.GPU_RAMP_UP_THRS + i);
                byte gpuRampUpValue = EC.DirectECRead(ecAddrPort, ecDataPort, gpuRampUpAddress);

                // Concatenate the values for each iteration
                gpuRampUpHexValues += $"0x{gpuRampUpValue:X} ";
                gpuRampUpIntValues += $"{gpuRampUpValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"GPU Ramp Up Thresholds (Hex): {gpuRampUpHexValues.Trim()}, (Int): {gpuRampUpIntValues.Trim()}");

            // Read and print GPU Ramp Down thresholds
            // Variables to store the values during the loop
            string gpuRampDownHexValues = "";
            string gpuRampDownIntValues = "";

            for (int i = 0; i < 10; i++)
            {
                UInt16 gpuRampDownAddress = (UInt16)((int)ITE_REGISTER_MAP.GPU_RAMP_DOWN_THRS + i);
                byte gpuRampDownValue = EC.DirectECRead(ecAddrPort, ecDataPort, gpuRampDownAddress);

                // Concatenate the values for each iteration
                gpuRampDownHexValues += $"0x{gpuRampDownValue:X} ";
                gpuRampDownIntValues += $"{gpuRampDownValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"GPU Ramp Down Thresholds (Hex): {gpuRampDownHexValues.Trim()}, (Int): {gpuRampDownIntValues.Trim()}");

            // Heatsink Ramp Up and Ramp Down Temperatures Debug //

            // Heatsink Temperature Thresholds Debug

            // Read and print Heatsink Ramp Up thresholds
            // Variables to store the values during the loop
            string hstRampUpHexValues = "";
            string hstRampUpIntValues = "";

            for (int i = 0; i < 10; i++)
            {
                UInt16 hstRampUpAddress = (UInt16)((int)ITE_REGISTER_MAP.HST_RAMP_UP_THRS + i);
                byte hstRampUpValue = EC.DirectECRead(ecAddrPort, ecDataPort, hstRampUpAddress);

                // Concatenate the values for each iteration
                hstRampUpHexValues += $"0x{hstRampUpValue:X} ";
                hstRampUpIntValues += $"{hstRampUpValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"Heatsink Ramp Up Thresholds (Hex): {hstRampUpHexValues.Trim()}, (Int): {hstRampUpIntValues.Trim()}");

            // Read and print Heatsink Ramp Down thresholds
            // Variables to store the values during the loop
            string hstRampDownHexValues = "";
            string hstRampDownIntValues = "";

            for (int i = 0; i < 10; i++)
            {
                UInt16 hstRampDownAddress = (UInt16)((int)ITE_REGISTER_MAP.HST_RAMP_DOWN_THRS + i);
                byte hstRampDownValue = EC.DirectECRead(ecAddrPort, ecDataPort, hstRampDownAddress);

                // Concatenate the values for each iteration
                hstRampDownHexValues += $"0x{hstRampDownValue:X} ";
                hstRampDownIntValues += $"{hstRampDownValue} ";
            }

            // Print all values in a single line after the loop
            Console.WriteLine($"Heatsink Ramp Down Thresholds (Hex): {hstRampDownHexValues.Trim()}, (Int): {hstRampDownIntValues.Trim()}");

            // Read the STOP_RGB_FAN_WAKE value
            byte stopRgbFanWakeValue = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.STOP_RGB_FAN_WAKE);

            // Check if the hexadecimal value is 0x25 and print "(Stopped)"
            string stoppedMessage = (stopRgbFanWakeValue == 0x25) ? " (Stopped)" : "";

            // Print the value
            Console.WriteLine($"STOP_RGB_FAN_WAKE Value: 0x{stopRgbFanWakeValue:X}{stoppedMessage}");


            // Read the FAN_TABLE_CHG_COUNTER value
            byte fanTableChgCounterValue = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN_TABLE_CHG_COUNTER);

            // Print the FAN_TABLE_CHG_COUNTER value
            Console.WriteLine($"FAN_TABLE_CHG_COUNTER Value: 0x{fanTableChgCounterValue:X}");

            // Read the FAN_TABLE_CHG_COUNTER_SEC value
            byte fanTableChgCounterSecValue = EC.DirectECRead(ecAddrPort, ecDataPort, (UInt16)ITE_REGISTER_MAP.FAN_TABLE_CHG_COUNTER_SEC);

            // Print the FAN_TABLE_CHG_COUNTER_SEC value
            Console.WriteLine($"FAN_TABLE_CHG_COUNTER_SEC Value: 0x{fanTableChgCounterSecValue:X}");



            // Keep the console window open for user observation (Debug Reading)
            Console.ReadLine();
        }

        private static int ExtractPowerModeWMI()
        {
            int powerModeWMI = 0;
#pragma warning disable CA1416 // Validate platform compatibility
            ManagementScope scope = new ManagementScope("\\\\.\\ROOT\\WMI");
#pragma warning restore CA1416 // Validate platform compatibility

            try
            {
                // Set up the WMI query
#pragma warning disable CA1416 // Validate platform compatibility
                ObjectQuery query = new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA");
#pragma warning restore CA1416 // Validate platform compatibility
#pragma warning disable CA1416 // Validate platform compatibility
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
#pragma warning restore CA1416 // Validate platform compatibility

                // Execute the query and retrieve the first result
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CA1416 // Validate platform compatibility
                ManagementObject gameZoneData = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
#pragma warning restore CA1416 // Validate platform compatibility
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

                // Check if the result is not null
                if (gameZoneData != null)
                {
                    // Call the GetSmartFanMode method
#pragma warning disable CA1416 // Validate platform compatibility
                    ManagementBaseObject outParams = gameZoneData.InvokeMethod("GetSmartFanMode", null, null);
#pragma warning restore CA1416 // Validate platform compatibility

                    // Check if the outParams object is not null
                    if (outParams != null)
                    {
                        // Retrieve the 'Data' property from outParams
#pragma warning disable CA1416 // Validate platform compatibility
                        object dataProperty = outParams["Data"];
#pragma warning restore CA1416 // Validate platform compatibility

                        // Check if the 'Data' property is not null
                        if (dataProperty != null)
                        {
                            // Convert 'Data' property to the appropriate type (e.g., int)
                            powerModeWMI = Convert.ToInt32(dataProperty);
                        }
                        else
                        {
                            Console.WriteLine("'Data' property is null.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("outParams is null.");
                    }
                }
                else
                {
                    Console.WriteLine("gameZoneData is null.");
                }
            }
            catch (ManagementException ex)
            {
                Console.WriteLine($"WMI Error: {ex.Message}");
            }

            return powerModeWMI;
        }

        static string GetFilePathBasedOnPowerMode(int powerModeWMI)
        {
            string executableDirectory = AppDomain.CurrentDomain.BaseDirectory;

            switch (powerModeWMI)
            {
                case 1:
                    return Path.Combine(executableDirectory, "fan_config_quiet.txt");
                case 2:
                    return Path.Combine(executableDirectory, "fan_config_balanced.txt");
                case 3:
                case 255:
                    return Path.Combine(executableDirectory, "fan_config_perfcust.txt");
                default:
                    // Invalid powerModeWMI value
                    Console.WriteLine("Invalid powerModeWMI value. Please set it correctly.");
                    Environment.Exit(1); // Exit the program with an error code
                    return null; // This line won't be reached, but added to satisfy the compiler
            }
        }

        static Dictionary<string, object> ExtractValuesFromFile(string filePath)
        {
            // Read all lines from the file
            string[] lines = File.ReadAllLines(filePath);

            // Create dictionaries to store the parsed values
            Dictionary<string, object> values = new Dictionary<string, object>();

            // Parse each line and store the values in the dictionary
            foreach (var line in lines)
            {
                var split = line.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length == 2)
                {
                    var key = split[0].Trim();
                    var valuesString = split[1].Trim().Split();
                    if (valuesString.Length == 1)
                    {
                        // Single integer value
                        values[key] = int.Parse(valuesString[0]);
                    }
                    else
                    {
                        // Array of integers
                        values[key] = valuesString.Select(int.Parse).ToArray();
                    }
                }
            }

            return values;
        }

    }
}

// Info from an older .ps1 script i was using instead
// The previous script was using Smokeless PoC.exe paired with RwDrv.sys
// FE or FF00DXXX addresses map to 0xCXXX addresses in this C# app

/*    $current_power_profile = (Get - WmiObject -namespace ROOT\WMI -class LENOVO_GAMEZONE_DATA).GetSmartFanMode() | findstr -b "Data" | % { $_.replace("Data", "") } | % { $_.replace(":", "") } | % { $_.Trim() }
[-1] # Get Current Power Mode
    # 1 = Quiet, 2 = Balanced, 3 = Performance, 255 = Custom (Will be treated the same as Performance Mode)*/  // This was used in Powershell but i implemented it in C# in a different way

/*# Steps and Info required to set a Fan Curve

# 1) Setting left & right fan acceleration and deceleration values
# 2) Setting the number of points in the curve
# 3) Setting left & right fan RPM values and number of points
# 4) Setting temperature target points for when to ramp up or ramp down fans to a specific RPM for CPU, GPU and Heatsink for hysteresis
# 5) (Optional for Gen6, required for Gen5 according to Underv0lti) Overriding Keyboard RGB Temp Trigger Value
# 6) Set Fan speed change to be near instant as opposed to 100RPM every 5 seconds as Lenovo default for registering the new table*/

/*# Detailed Information

# Fan Addresses we're looking for in EC are marked with FF for Intel and FE for AMD, i'll provide examples for an AMD Legion5Pro Gen6(2021), so if you're on Intel replace FE with FF

# Step 1) 

# To set fans accel/dec values we have to write to the following addresses
# Left Fan Accel  : FE00D3DC        Left Fan Dec  : FE00D3DD
# Right Fan Accel : FE00D3DE        Right Fan Dec : FE00D3DF (This is for Gen5)
# For Gen6 we have to write the Accel and Deccel Values for each point
# Addresses are : FE00D560 to FE00D56A // Both Fan Acceleration FF00D570 to FF00D57A // Both fan Deceleration

# We have to write a value of 1 hex byte, for example 1, 2... 0F(15). The lower the value the faster it is, 1 is near instantenous, i recommend using 2, 3 or 4
# For example using PoC we need to write "w FE00D3DC 2".

# Step 2)

# We have to provide to the fan table the exact amount of Fan Points we will use otherwise it might incorectly use more, and/or because missing data it might get stuck or crash the EC leading to a reboot
# We need to write at the address FE00D535 a hex byte, for example if we use w FE00D535 06 we will have 5 points in our fan curve. You can't use more than 9 points otherwise it will crash your PC

# Step 3)

# Left Fan RPM Starting  address is  : FE00D551
# Right FAN RPM Starting address is  : FE00D541
# For each point FE00D551 ... up to FE00D559 (depending on how many points you use) you will do something like w FE00D551 26, The fan speed needs to be in a Hex value, for example 26(Hex) = 38(Dec) -> translated to 3800RPM 
# E.g w FE00D551 26 (using PoC)

# Step 4)

# CPU_Ramp_Up   FE00D580  Starting Address last digit goes from 0 to 9 
# CPU_Ramp_Down FE00D591  Starting Address last digit goes from 1 to 9, Ramp_Down needs to be 1 value higher than Ramp up and Ramp_Up has to have it's last digit equal to the last digit of the last point of Ramp_Down and have
# it's hex value equal to 7F which is used by lenovo as an ignore value. Same as before the value is a hex byte for example w FE00D580 37 would mean the first Ramp Up point activates at 55 degrees Celsius

# GPU_Ramp_Up   FE00D5A0   
# GPU_Ramp_Down FE00D5B1  

# Heatsink_Ramp_Up    FE00D5C0 
# Heatsink_Ramp_Down  FE00D5D1  Same principle as before

# Step 5)

# Write w FE00D64D 25 to disable Fans turning on When RGB keyboard is turned on

# Step 6)

# Set fan change counters to 100 to favour a faster fan table change (There is an internal counter that counts to 100 in 5 seconds by setting this to 100 we will change instantly)
# Write w FE00D5FE 64  and w FE00D5FF 64

# My curves will use the same temps for all the profiles, the only difference will be the FAN RPM between the 3 modes, and i will use 9 Points

# Example for my Gen6 Legion5ProAMD(2021)
# Number of points : 9
# Acceleration/Deceleration Values will be 2
# CPU Temps On       11 45 55 60 65 70 75 80 90
# Cpu Temps Off      10 43 53 58 63 68 73 78 87
# Gpu Temps On       11 50 55 60 63 66 69 72 75
# Gpu Temps Off      10 48 53 58 61 63 67 70 73
# Heatsink Temps On  11 50 55 65 70 75 80 85 90
# Heatsink Temps Off 10 48 53 63 68 73 78 83 85
# RPM Quiet              0 0 0 1800 2500 3200 3500 3800 4400       
# RPM Balanced           0 0 2200 3200 3500 3800 4100 4400 4700
# RPM Performance/Custom 0 0 2200 3600 3900 4200 4500 4800 5000*/

// Other info

/*# Technical Information provided by Underv0lti  This was based for using RwDrv.sys and PoC.exe(from smokelesscpu) FE/FF00DXXX from gen5, gen6 usually map out with succes to 0xCXXX using the WinRing and I/O Ports
// Implemented by Smokeless in this file

# Change the memory address based on year and CPU:
# FF00D520: Intel 2021/2020
# FE00D520: AMD 2021/2020
# FE0B0520: Intel/AMD 2022

# FF00D530: Mode indicator, changing it won't do anything useful.
# FF00D531: Indicates the fan curve is in the low temperature mode or not(changing it won't do anything useful, low temp curve: 1 normal curve: 2)
# FF00D534: Indicates the fan curve level the fans are currently at.
# FF00D535: Indicates the fan curve endpoint, can be changed to increase the fan curve length(the fans will ignore values that are placed after the endpoint)
# FF00D538: CPU Temperature
# FF00D539: GPU Temperature (can be disabled by setting 0xBD to 20 in the main EC or FF00D4BD)
# FF00D53A: Heatsink/VRM's Temperature
# FF00D5FC: CPU Fan Target RPM
# FF00D5FD: GPU Fan Target RPM
# FF00D4AB: Locks the fan speed and disables the fan delay counters at FF00D5FE/FF.

# Max Fan Curve Step Count: 9 Steps (10 might blue screen not advised to try)

# -------------------------------------

# FAN CONTROL

# -------------------------------------

# FF00D5E0&E1: CPU(Right) Fan TACH High and Low bits, read it in 16bit and you get the full fan RPM
# FF00D5E2&E3: GPU(Left) Fan TACH High and Low bits, read it in 16bit and you get the full fan RPM
# FF00D5E4: CPU(Right) Fan Instant Set Register(will get overwritten by the fan curve once the fan speed change delay ends) (fan delay bypassable by setting this register and the fan curve at the same time)
# FF00D5E5: CPU(Left) Fan Instant Set Register(will get overwritten by the fan curve once the fan speed change delay ends) (fan delay bypassable by setting this register and the fan curve at the same time)

# FF00D5FE: is the cpu fan delay counter, counts up to 100 in 5 seconds, you can bypass the fan delay if you set it to 64.
# FF00D5FF: is the cpu fan delay counter, counts up to 100 in 5 seconds, you can bypass the fan delay if you set it to 64.

# ----------------------------------------------------

# Fan Acceleration/Deceleration fix for 2020 models: 

# FF00D3DC: Fan 1 Acceleration
# FF00D3DD: Fan 2 Acceleration
# FF00D3DE: Fan 1 Deceleration
# FF00D3DF: Fan 2 Deceleration
# ----------------------------------------------------

# Fan Curve: 


# For CPU Fan, FF00D541 to whatever value you set at FF00D535 (e.g if you set 0A, the fan curve will be 9 steps max)
# For GPU Fan, FF00D551 to whatever value you set at FF00D535

# Fan Acceleration for both fan: FF00D561 to whatever value you set at FF00D535 (you can set different values for each steps in the curve)(doesn't work for some reason on 2020 models)
# Fan Deceleration for both fan: FF00D571 to whatever value you set at FF00D535 (you can set different values for each steps in the curve)(doesn't work for some reason on 2020 models)

# CPU Temperature Thresholds for Ramp Up: FF00D581 to whatever value you set at FF00D535
# CPU Temperature Thresholds for Hysteresis(Slow Down): FF00D591 to whatever value you set at FF00D535 

# GPU Temperature Thresholds for Ramp Up: FF00D5A1 to whatever value you set at FF00D535
# GPU Temperature Thresholds for Hysteresis(Slow Down): FF00D5B1 to whatever value you set at FF00D535

# VRM/Heatsink Temperature Thresholds for Ramp Up: FF00D5C1 to whatever value you set at FF00D535
# VRM/Heatsink Temperature Thresholds for Hysteresis(Slow Down): FF00D5D1 to whatever value you set at FF00D5D5

# Set 7F(127) for ignoring values in the temperature threshold tables.

# -------------------------------------------------------------------------------------------------*/


// ---------------


// Unused Code that was used during testing, left for educational purposes

/*// Assuming FAN1_RPM_LSB and FAN1_RPM_MSB are the addresses for FAN1 RPM
UInt16 fan1RpmLsbAddress = (UInt16)EC.ITE_REGISTER_MAP.FAN1_RPM_LSB;
UInt16 fan1RpmMsbAddress = (UInt16)EC.ITE_REGISTER_MAP.FAN1_RPM_MSB;

// Read the low byte and high byte of FAN1 RPM
byte fan1RpmLsb = EC.DirectECRead(ecAddrPort, ecDataPort, fan1RpmLsbAddress);
byte fan1RpmMsb = EC.DirectECRead(ecAddrPort, ecDataPort, fan1RpmMsbAddress); // correct

// Combine the low byte and high byte to get the FAN1 RPM
int fan1Rpm = (fan1RpmMsb << 8) | fan1RpmLsb;

// Display the FAN1 RPM
Console.WriteLine($"FAN1 RPM: {fan1Rpm} RPM");*/



/*// Print the values of the converted file info to hex
Console.WriteLine($"fanCurvePoints (Decimal): {fanCurvePointsByte}");
Console.WriteLine($"fanAcclValue (Decimal): {fanAcclValueByte}");
Console.WriteLine($"fanDecclValue (Decimal): {fanDecclValueByte}");

Console.WriteLine("fanRpmPointsBytes (Decimal):");
Console.WriteLine(string.Join(", ", fanRpmPointsBytes));

Console.WriteLine("cpuTempsRampUpBytes (Decimal):");
Console.WriteLine(string.Join(", ", cpuTempsRampUpBytes));

Console.WriteLine("cpuTempsRampDownBytes (Decimal):");
Console.WriteLine(string.Join(", ", cpuTempsRampDownBytes));

Console.WriteLine("gpuTempsRampUpBytes (Decimal):");
Console.WriteLine(string.Join(", ", gpuTempsRampUpBytes));

Console.WriteLine("gpuTempsRampDownBytes (Decimal):");
Console.WriteLine(string.Join(", ", gpuTempsRampDownBytes));

Console.WriteLine("hstTempsRampUpBytes (Decimal):");
Console.WriteLine(string.Join(", ", hstTempsRampUpBytes));

Console.WriteLine("hstTempsRampDownBytes (Decimal):");
Console.WriteLine(string.Join(", ", hstTempsRampDownBytes));

Console.WriteLine();
Console.WriteLine();

// Print the hexadecimal values
Console.WriteLine($"fanCurvePoints (Hexadecimal): 0x{fanCurvePointsByte:X}");
Console.WriteLine($"fanAcclValue (Hexadecimal): 0x{fanAcclValueByte:X}");
Console.WriteLine($"fanDecclValue (Hexadecimal): 0x{fanDecclValueByte:X}");

Console.WriteLine("fanRpmPointsBytes (Hexadecimal):");
Console.WriteLine(string.Join(", ", fanRpmPointsBytes.Select(b => $"0x{b:X}")));

Console.WriteLine("cpuTempsRampUpBytes (Hexadecimal):");
Console.WriteLine(string.Join(", ", cpuTempsRampUpBytes.Select(b => $"0x{b:X}")));

Console.WriteLine("cpuTempsRampDownBytes (Hexadecimal):");
Console.WriteLine(string.Join(", ", cpuTempsRampDownBytes.Select(b => $"0x{b:X}")));

Console.WriteLine("gpuTempsRampUpBytes (Hexadecimal):");
Console.WriteLine(string.Join(", ", gpuTempsRampUpBytes.Select(b => $"0x{b:X}")));

Console.WriteLine("gpuTempsRampDownBytes (Hexadecimal):");
Console.WriteLine(string.Join(", ", gpuTempsRampDownBytes.Select(b => $"0x{b:X}")));

Console.WriteLine("hstTempsRampUpBytes (Hexadecimal):");
Console.WriteLine(string.Join(", ", hstTempsRampUpBytes.Select(b => $"0x{b:X}")));

Console.WriteLine("hstTempsRampDownBytes (Hexadecimal):");
Console.WriteLine(string.Join(", ", hstTempsRampDownBytes.Select(b => $"0x{b:X}")));*/





// ---------------






// ---------------- Code that was put in a function clean this up later -----

/*// Assuming fanAcclValueByte and fanDecclValueByte are calculated as mentioned before

// Addresses for GEN 5 fan acceleration and deceleration values
UInt16 fan1AccGen5Address = (UInt16)ITE_REGISTER_MAP.FAN1_ACC_GEN5;
UInt16 fan1DecGen5Address = (UInt16)ITE_REGISTER_MAP.FAN1_DEC_GEN5;
UInt16 fan2AccGen5Address = (UInt16)ITE_REGISTER_MAP.FAN2_ACC_GEN5;
UInt16 fan2DecGen5Address = (UInt16)ITE_REGISTER_MAP.FAN2_DEC_GEN5;

// Check if legionGen is equal to 5
if (legionGen == 5)
{
    // Write fan1 acceleration value
    EC.DirectECWrite(ecAddrPort, ecDataPort, fan1AccGen5Address, fanAcclValueByte);
    // Write fan1 deceleration value
    EC.DirectECWrite(ecAddrPort, ecDataPort, fan1DecGen5Address, fanDecclValueByte);

    // Write fan2 acceleration value
    EC.DirectECWrite(ecAddrPort, ecDataPort, fan2AccGen5Address, fanAcclValueByte);
    // Write fan2 deceleration value
    EC.DirectECWrite(ecAddrPort, ecDataPort, fan2DecGen5Address, fanDecclValueByte);

    //Console.WriteLine("GEN 5 Fan Acceleration and Deceleration values written successfully.");
}
else
{
    // Assuming fanAcclValueByte and fanDecclValueByte are calculated as mentioned before

    // Addresses for GEN 6 fan acceleration and deceleration values
    UInt16 fanAccGen6StartAddress = (UInt16)ITE_REGISTER_MAP.FAN_ACC_GEN6;
    UInt16 fanDecGen6StartAddress = (UInt16)ITE_REGISTER_MAP.FAN_DEC_GEN6;

    // Number of addresses to span
    int numberOfAddresses = 10; // 0xC569 - 0xC560 + 1 = 10

    // Create arrays with acceleration and deceleration values
    byte[] fanAccValues = Enumerable.Repeat((byte)fanAcclValueByte, numberOfAddresses).ToArray();
    byte[] fanDecValues = Enumerable.Repeat((byte)fanDecclValueByte, numberOfAddresses).ToArray();

    // Write to the GEN 6 fan acceleration addresses
    EC.DirectECWriteArray(ecAddrPort, ecDataPort, fanAccGen6StartAddress, fanAccValues);

    // Write to the GEN 6 fan deceleration addresses
    EC.DirectECWriteArray(ecAddrPort, ecDataPort, fanDecGen6StartAddress, fanDecValues);

    //Console.WriteLine("GEN 6 Fan Acceleration and Deceleration values written successfully.");
}*/

//

// Hysteresis
internal sealed class TempHysteresis
{
    public int CurrentPoint = 0;

    private DateTime? aboveSince;
    private DateTime? belowSince;

    public int Update(
        int temp,
        int[] rampUp,
        int[] rampDown,
        int hysteresisC)
    {
        // Ramp Up
        if (CurrentPoint < rampUp.Length - 1 &&
            temp >= rampUp[CurrentPoint] + hysteresisC)
        {
            aboveSince ??= DateTime.Now;

            if ((DateTime.Now - aboveSince.Value).TotalMilliseconds >= 1000)
            {
                CurrentPoint++;
                Reset();
            }
        }
        else
        {
            aboveSince = null;
        }

        // Ramp Down
        if (CurrentPoint > 0 &&
            temp <= rampDown[CurrentPoint - 1] - hysteresisC)
        {
            belowSince ??= DateTime.Now;

            if ((DateTime.Now - belowSince.Value).TotalMilliseconds >= 1000)
            {
                CurrentPoint--;
                Reset();
            }
        }
        else
        {
            belowSince = null;
        }

        return CurrentPoint;
    }

    private void Reset()
    {
        aboveSince = null;
        belowSince = null;
    }
}