using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        public class ThrusterControl
        {
            private List<IMyThrust> thrusters = new List<IMyThrust>();
            IMyShipController rc;

            /// <summary>
            /// World velocity
            /// </summary>
            public Vector3D Velocity = new Vector3D();

            public void Reset ()
            {
                for (int i = 0; i < thrusters.Count; i++)
                {
                    IMyThrust t = thrusters [i];
                    if (t == null)
                    {
                        thrusters.RemoveAtFast(i);
                        continue;
                    }

                    t.Enabled = true;
                    t.ThrustOverride = 0;
                }
            }

            public ThrusterControl (IMyShipController rc, List<IMyThrust> thrusters)
            {
                this.rc = rc;
                this.thrusters = thrusters;
                Reset();
            }

            public void Update ()
            {
                // Calculate the needed thrust to get to velocity
                Vector3D myVel = rc.GetShipVelocities().LinearVelocity;
                Vector3D deltaV = myVel - Velocity;

                if (Vector3D.IsZero(deltaV))
                    return;

                Vector3D gravity = rc.GetNaturalGravity();
                Vector3D thrust = GetShipMass() * (2 * deltaV + gravity);

                // Apply the thrust
                for (int i = 0; i < thrusters.Count; i++)
                {
                    IMyThrust t = thrusters [i];
                    if (t == null)
                    {
                        thrusters.RemoveAtFast(i);
                        continue;
                    }


                    if (!t.IsFunctional)
                        continue;

                    if (Vector3D.Dot(t.WorldMatrix.Forward, thrust) > 0)
                    {
                        t.Enabled = true;
                        double outputThrust = Vector3D.Dot(thrust, t.WorldMatrix.Forward);
                        double outputProportion = MathHelper.Clamp(outputThrust / t.MaxEffectiveThrust, 0, 1);
                        t.ThrustOverridePercentage = (float)outputProportion;
                        thrust -= t.WorldMatrix.Forward * outputProportion * t.MaxEffectiveThrust;

                    }
                    else
                    {
                        t.ThrustOverride = 0;
                        t.Enabled = false;
                    }
                }
            }

            double GetShipMass ()
            {
                double totalMass = rc.CalculateShipMass().TotalMass; // ship total mass including cargo mass
                double baseMass = rc.CalculateShipMass().BaseMass; // mass of the ship without cargo
                double cargoMass = totalMass - baseMass; // mass of the cargo
                return baseMass + (cargoMass / inventoryMultiplier); // the mass the game uses for physics calculation
            }

            /// <summary>
            /// Distance required to bring velocity to zero using the thrusters in direction
            /// </summary>
            public Vector3D StopDistance (double accuracy, Vector3D finalVelocity)
            {
                double shipMass = GetShipMass();
                Vector3D currentVelocity = rc.GetShipVelocities().LinearVelocity;
                Vector3D gravity = rc.GetNaturalGravity();
                Vector3D thrust = shipMass * (2 * -currentVelocity + gravity);
                Vector3D output = new Vector3D();
                for (int i = 0; i < thrusters.Count; i++)
                {
                    IMyThrust t = thrusters [i];
                    if (t == null)
                    {
                        thrusters.RemoveAtFast(i);
                        continue;
                    }

                    if (!t.IsFunctional)
                        continue;

                    if (Vector3D.Dot(t.WorldMatrix.Forward, thrust) > 0)
                    {
                        t.Enabled = true;
                        double outputThrust = Vector3D.Dot(thrust, t.WorldMatrix.Forward);
                        double outputProportion = MathHelper.Clamp(outputThrust / t.MaxEffectiveThrust, 0, 1);
                        Vector3D value = t.WorldMatrix.Forward * outputProportion * t.MaxEffectiveThrust;
                        output += value;
                        thrust -= value;
                    }
                }

                Vector3D accel = output / shipMass;
                Vector3D result = (currentVelocity * currentVelocity - finalVelocity * finalVelocity) / (2 * accel) + new Vector3D(accuracy, accuracy, accuracy);
                return result;
            }
        }
    }
}