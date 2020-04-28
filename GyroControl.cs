using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        public class GyroControl
        {
            private List<IMyGyro> gyros;
            IMyShipController rc;

            public GyroControl (IMyShipController rc)
            {
                this.rc = rc;

                gyros = GetBlocks<IMyGyro>();

                anglePID = new VectorPID(18, 0, 3, -1000, 1000, 1.0 / 60);

                Reset();
            }
            // In (pitch, yaw, roll)
            VectorPID anglePID;

            public void Reset ()
            {
                for (int i = 0; i < gyros.Count; i++)
                {
                    IMyGyro g = gyros [i];
                    if (g == null)
                    {
                        gyros.RemoveAtFast(i);
                        continue;
                    }
                    g.GyroOverride = false;
                }
                anglePID.Reset();
            }


            Vector3D GetAngles (MatrixD current, Vector3D forward, Vector3D up)
            {
                Vector3D error = new Vector3D();

                if (forward != Vector3D.Zero)
                {
                    Quaternion quat = Quaternion.CreateFromForwardUp(current.Forward, current.Up);
                    Quaternion invQuat = Quaternion.Inverse(quat);
                    Vector3D RCReferenceFrameVector = Vector3D.Transform(forward, invQuat); //Target Vector In Terms Of RC Block

                    //Convert To Local Azimuth And Elevation
                    Vector3D.GetAzimuthAndElevation(RCReferenceFrameVector, out error.Y, out error.X);
                }

                if (up != Vector3D.Zero)
                {
                    Vector3D temp = Vector3D.Normalize(VectorRejection(up, rc.WorldMatrix.Forward));
                    double dot = MathHelper.Clamp(Vector3D.Dot(rc.WorldMatrix.Up, temp), -1, 1);
                    double rollAngle = Math.Acos(dot);
                    double scaler = ScalerProjection(temp, rc.WorldMatrix.Right);
                    if (scaler > 0)
                        rollAngle *= -1;
                    error.Z = rollAngle;
                }

                if (Math.Abs(error.X) < 0.001)
                    error.X = 0;
                if (Math.Abs(error.Y) < 0.001)
                    error.Y = 0;
                if (Math.Abs(error.Z) < 0.001)
                    error.Z = 0;

                return error;
            }

            public void FaceVectors (Vector3D forward, Vector3D up)
            {
                // In (pitch, yaw, roll)
                Vector3D error = -GetAngles(rc.WorldMatrix, forward, up);
                Vector3D angles = new Vector3D(anglePID.Control(error));
                ApplyGyroOverride(rc.WorldMatrix, angles);
            }
            void ApplyGyroOverride (MatrixD current, Vector3D localAngles)
            {
                Vector3D worldAngles = Vector3D.TransformNormal(localAngles, current);
                foreach (IMyGyro gyro in gyros)
                {
                    Vector3D transVect = Vector3D.TransformNormal(worldAngles, MatrixD.Transpose(gyro.WorldMatrix));  //Converts To Gyro Local
                    if (!transVect.IsValid())
                        throw new Exception("Invalid trans vector. " + transVect.ToString());

                    gyro.Pitch = (float)transVect.X;
                    gyro.Yaw = (float)transVect.Y;
                    gyro.Roll = (float)transVect.Z;
                    gyro.GyroOverride = true;
                }
            }

            /// <summary>
            /// Projects a value onto another vector.
            /// </summary>
            /// <param name="guide">Must be of length 1.</param>
            public static double ScalerProjection (Vector3D value, Vector3D guide)
            {
                double returnValue = Vector3D.Dot(value, guide);
                if (double.IsNaN(returnValue))
                    return 0;
                return returnValue;
            }

            /// <summary>
            /// Projects a value onto another vector.
            /// </summary>
            /// <param name="guide">Must be of length 1.</param>
            public static Vector3D VectorPojection (Vector3D value, Vector3D guide)
            {
                return ScalerProjection(value, guide) * guide;
            }

            /// <summary>
            /// Projects a value onto another vector.
            /// </summary>
            /// <param name="guide">Must be of length 1.</param>
            public static Vector3D VectorRejection (Vector3D value, Vector3D guide)
            {
                return value - VectorPojection(value, guide);
            }
        }
    }
}