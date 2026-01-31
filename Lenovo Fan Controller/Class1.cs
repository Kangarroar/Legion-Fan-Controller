namespace Lenovo_Fan_Controller
{
    public class FanConfig
    {
        public int LegionGeneration { get; set; }
        public int FanCurvePoints { get; set; }
        public int AccelerationValue { get; set; }
        public int DecelerationValue { get; set; }
        public int[] FanRpmPoints { get; set; }
        public int[] CpuTempsRampUp { get; set; }
        public int[] CpuTempsRampDown { get; set; }
        public int[] GpuTempsRampUp { get; set; }
        public int[] GpuTempsRampDown { get; set; }
        public int[] HstTempsRampUp { get; set; }
        public int[] HstTempsRampDown { get; set; }
        public int[] CpuTemps { get; set; }
        public int[] GpuTemps { get; set; }
        public int[] HstTemps { get; set; }
    }
}