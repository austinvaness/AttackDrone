using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class Clock
        {
            public int Runtime
            {
                get; private set;
            }
            private readonly double secondsPerTick;

            public Clock(UpdateFrequency frequency)
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

            public void Update ()
            {
                Runtime++;
                if (Runtime >= int.MaxValue)
                    Runtime = 0;
            }

            public double GetSeconds (int start)
            {
                int diff = Runtime - start;
                return diff * secondsPerTick;
            }

            public int GetTick (double seconds)
            {
                double ticks = seconds / secondsPerTick;
                int roundedTicks = (int)Math.Ceiling(ticks);
                return Runtime + roundedTicks;
            }
        }
    }
}
