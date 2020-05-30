using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        struct Enemy
        {
            public Vector3D Position;
            public Vector3D Velocity;
            public Enemy (MyDetectedEntityInfo info)
            {
                Position = info.Position;
                Velocity = info.Velocity;
            }
            public Enemy (Vector3D position, Vector3D velocity)
            {
                Position = position;
                Velocity = velocity;
            }
        }
    }
}