using Sandbox.ModAPI.Ingame;
using System;

namespace IngameScript
{
    public partial class Program
    {
        public static class Clock
        {
            public static int Runtime
            {
                get; private set;
            }
            private static double secondsPerTick;

            public static void Start (UpdateFrequency frequency)
            {
                Runtime = 0;
                switch (frequency)
                {
                    case (UpdateFrequency.Update1):
                        secondsPerTick = 1.0 / 60;
                        break;
                    case (UpdateFrequency.Update10):
                        secondsPerTick = 1.0 / 6;
                        break;
                    case (UpdateFrequency.Update100):
                        secondsPerTick = 5.0 / 3;
                        break;
                }
            }

            public static void Update ()
            {
                Runtime++;
                if (Runtime >= int.MaxValue)
                    Runtime = 0;
            }

            public static double GetSeconds (int start)
            {
                int diff = Runtime - start;
                return diff * secondsPerTick;
            }

            public static int GetTick (double seconds)
            {
                double ticks = seconds / secondsPerTick;
                int roundedTicks = (int)Math.Ceiling(ticks);
                return Runtime + roundedTicks;
            }
        }
    }
}