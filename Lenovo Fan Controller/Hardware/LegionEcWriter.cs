using System;
using System.Linq;

namespace LegionFanController.Hardware
{
    /// <summary>
    /// EC write operations for fan control
    /// </summary>
    internal static class ECWriter
    {
        private const ushort EC_ADDR_PORT = 0x4E;
        private const ushort EC_DATA_PORT = 0x4F;

        public static void WriteECByte(ushort addr, byte value)
        {
            lock (typeof(ECWriter))
            {
                PawnIODriver.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                PawnIODriver.WriteIoPortByte(EC_DATA_PORT, 0x11);
                PawnIODriver.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                PawnIODriver.WriteIoPortByte(EC_DATA_PORT, (byte)((addr >> 8) & 0xFF));

                PawnIODriver.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                PawnIODriver.WriteIoPortByte(EC_DATA_PORT, 0x10);
                PawnIODriver.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                PawnIODriver.WriteIoPortByte(EC_DATA_PORT, (byte)(addr & 0xFF));

                PawnIODriver.WriteIoPortByte(EC_ADDR_PORT, 0x2E);
                PawnIODriver.WriteIoPortByte(EC_DATA_PORT, 0x12);
                PawnIODriver.WriteIoPortByte(EC_ADDR_PORT, 0x2F);
                PawnIODriver.WriteIoPortByte(EC_DATA_PORT, value);
            }
        }

        private static void WriteECByteArray(ushort startAddr, byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                WriteECByte((ushort)(startAddr + i), data[i]);
            }
        }

        public static void WriteFanAcclDeccl(int legionGen, byte acclValue, byte declValue)
        {
            if (legionGen == 5)
            {
                WriteECByte((ushort)ECWriteRegisters.FAN1_ACC_GEN5, acclValue);
                WriteECByte((ushort)ECWriteRegisters.FAN1_DEC_GEN5, declValue);
                WriteECByte((ushort)ECWriteRegisters.FAN2_ACC_GEN5, acclValue);
                WriteECByte((ushort)ECWriteRegisters.FAN2_DEC_GEN5, declValue);
            }
            else
            {
                byte[] acclValues = Enumerable.Repeat(acclValue, 10).ToArray();
                byte[] declValues = Enumerable.Repeat(declValue, 10).ToArray();
                WriteECByteArray((ushort)ECWriteRegisters.FAN_ACC_GEN6, acclValues);
                WriteECByteArray((ushort)ECWriteRegisters.FAN_DEC_GEN6, declValues);
            }
        }

        public static void WriteFanPointCount()
        {
            // Always write 0x0A (10) which gives 9 usable points
            WriteECByte((ushort)ECWriteRegisters.FAN_POINTS_NO, 0x0A);
        }

        public static void WriteFanRpmPoints(byte[] rpmPoints)
        {
            byte[] valuesToWrite = new byte[9];
            Array.Copy(rpmPoints, valuesToWrite, Math.Min(rpmPoints.Length, 9));

            byte lastValue = rpmPoints.Length > 0 ? rpmPoints[rpmPoints.Length - 1] : (byte)0;
            for (int i = rpmPoints.Length; i < 9; i++)
                valuesToWrite[i] = lastValue;

            WriteECByteArray((ushort)ECWriteRegisters.FAN1_RPM_ST_ADDR, valuesToWrite);
            WriteECByteArray((ushort)ECWriteRegisters.FAN2_RPM_ST_ADDR, valuesToWrite);
        }

        public static void WriteTemperatureRamp(byte[] rampUpValues, byte[] rampDownValues,
            ushort rampUpStartAddr, ushort rampDownStartAddr)
        {
            const byte IGNORE_VALUE = 0x7F;  // Lenovo EC ignore/disable marker

            // Ramp Up: fill unused slots with IGNORE_VALUE (0x7F)
            byte[] upValues = new byte[10];
            Array.Copy(rampUpValues, upValues, Math.Min(rampUpValues.Length, 10));
            for (int i = rampUpValues.Length; i < 10; i++)
                upValues[i] = IGNORE_VALUE;  // ✅ 固定用 0x7F，不是最后一个值
            WriteECByteArray(rampUpStartAddr, upValues);

            // Ramp Down: fill unused slots with the last valid value
            byte[] downValues = new byte[10];
            Array.Copy(rampDownValues, downValues, Math.Min(rampDownValues.Length, 10));
            byte fillValueDown = rampDownValues.Length > 0 ? rampDownValues[rampDownValues.Length - 1] : IGNORE_VALUE;
            for (int i = rampDownValues.Length; i < 10; i++)
                downValues[i] = fillValueDown;
            WriteECByteArray(rampDownStartAddr, downValues);
        }

        public static void WriteStopRgbFanWake()
        {
            WriteECByte((ushort)ECWriteRegisters.STOP_RGB_FAN_WAKE, 0x25);
        }

        public static void WriteFanTableChangeCounter(byte value)
        {
            WriteECByte((ushort)ECWriteRegisters.FAN_TABLE_CHG_COUNTER, value);
            WriteECByte((ushort)ECWriteRegisters.FAN_TABLE_CHG_COUNTER_SEC, value);
        }
    }

    internal enum ECWriteRegisters : ushort
    {
        // Gen5 ACC/DEC
        FAN1_ACC_GEN5 = 0xC3DC,
        FAN1_DEC_GEN5 = 0xC3DD,
        FAN2_ACC_GEN5 = 0xC3DE,
        FAN2_DEC_GEN5 = 0xC3DF,

        // Gen6 ACC/DEC
        FAN_ACC_GEN6 = 0xC560,
        FAN_DEC_GEN6 = 0xC570,

        // Fan points
        FAN_POINTS_NO = 0xC535,

        // Fan RPM tables
        FAN1_RPM_ST_ADDR = 0xC551,
        FAN2_RPM_ST_ADDR = 0xC541,

        // Temperature thresholds
        CPU_RAMP_UP = 0xC580,
        CPU_RAMP_DOWN = 0xC591,
        GPU_RAMP_UP = 0xC5A0,
        GPU_RAMP_DOWN = 0xC5B1,
        HST_RAMP_UP = 0xC5C0,
        HST_RAMP_DOWN = 0xC5D1,

        // Misc
        STOP_RGB_FAN_WAKE = 0xC64D,
        FAN_TABLE_CHG_COUNTER = 0xC5FE,
        FAN_TABLE_CHG_COUNTER_SEC = 0xC5FF,
    }
}