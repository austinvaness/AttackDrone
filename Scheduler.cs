using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System;

namespace IngameScript
{
    public partial class Program
    {
        public class Scheduler
        {
            Dictionary<int, Action> actions = new Dictionary<int, Action>();
            public int Count { get; private set; }
            public int Runtime { get; private set; }
            readonly UpdateFrequency frequency;

            public Scheduler (UpdateFrequency frequency)
            {
                this.frequency = frequency;
                Runtime = 0;
                Count = 0;
            }

            public void Update ()
            {
                if (actions.ContainsKey(Runtime))
                {
                    Action a = actions [Runtime];
                    if (a != null)
                    {
                        a.Invoke();
                        actions.Remove(Runtime);
                    }
                }
                Runtime++;
            }
            private void Add (int key, Action action)
            {
                Count++;
                if (actions.ContainsKey(key))
                {
                    Action temp = actions [key];
                    temp += action;
                    actions [key] = temp;
                }
                else
                {
                    actions.Add(key, action);
                }
            }

            public void ScheduleRuntime (Action action, int runtime)
            {
                Add(Runtime + runtime, action);
            }

            public void ScheduleSeconds (Action action, float sec)
            {
                float factor = -1;
                if (frequency == UpdateFrequency.Update1)
                    factor = 1f / 60f;
                else if (frequency == UpdateFrequency.Update10)
                    factor = 1f / 6f;
                else if (frequency == UpdateFrequency.Update100)
                    factor = 5f / 3f;
                int target = Runtime + Convert.ToInt32(sec / factor);
                Add(target, action);
            }

            public float GetSeconds (int start)
            {
                float factor = -1;
                if (frequency == UpdateFrequency.Update1)
                    factor = 1f / 60f;
                else if (frequency == UpdateFrequency.Update10)
                    factor = 1f / 6f;
                else if (frequency == UpdateFrequency.Update100)
                    factor = 5f / 3f;

                int diff = Runtime - start;
                return diff * factor;
            }
        }
    }
}