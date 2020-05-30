using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        public class VectorPID
        {
            private PID X;
            private PID Y;
            private PID Z;

            public VectorPID (double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
            {
                X = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
                Y = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
                Z = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
            }

            public VectorPID (double kP, double kI, double kD, double integralDecayRatio, double timeStep)
            {
                X = new PID(kP, kI, kD, integralDecayRatio, timeStep);
                Y = new PID(kP, kI, kD, integralDecayRatio, timeStep);
                Z = new PID(kP, kI, kD, integralDecayRatio, timeStep);
            }

            public Vector3D Control (Vector3D error)
            {
                return new Vector3D(X.Control(error.X), Y.Control(error.Y), Z.Control(error.Z));
            }

            public void Reset ()
            {
                X.Reset();
                Y.Reset();
                Z.Reset();
            }
        }
    }
}