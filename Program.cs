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
using VRage;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // TODO: 
        // - Compare to old version, remote commands?
        // - 

        // ============= Settings ==============
        // The number of seconds to keep chasing the target after the ship looses sight
        readonly double timeout = 10;

        // The name of the group that contains all of the guns on the ship. 
        // Set to "" to select all guns on the ship.
        readonly string gunsGroup = "";

        // The name of the remote control block.
        // Set to "" to select the first block on the ship.
        readonly string rcName = "";

        // The name of the group of gyros to use for orienting the ship.
        // Set to "" to select all gyros on the ship.
        readonly string gyroGroup = "";

        // The name of the group of turrets to use for detecting enemies.
        // Set to "" to select all turrets on the ship.
        readonly string turretGroup = "";

        // This is the max angle in degrees that the ship can be facing relative to the target.
        // This prevents the ship from firing while it is rotating.
        double maxAngle = 20;

        // This is the velocity of the bullet. Only constant velocities supported for now. (So Gatling Guns only)
        // https://spaceengineerswiki.com/Weapons
        // Not used if the ship is firing rockets.
        const double muzzleVelocity = 400;

        // When the ship is firing rockets, the ship will lead based on the rockets instead of the muzzle velocity. 
        // (Rockets have a more complex flight path)
        // Values: None, LargeGrid, All
        RocketMode rocketMode = RocketMode.LargeGrid;

        // The time in seconds to wait before starting the script.
        readonly double startDelay = 0;

        // Minimum orbit radius
        readonly double minRadius = 400;

        // Maximum orbit radius
        readonly double maxRadius = 700;

        // The speed to orbit around the target.
        readonly double orbitVelocity = 20;

        // When gravity is above this value, the orbit pattern will be flat aligned with gravity.
        readonly double useGravityValue = 0.5;

        // If true, the script will call help when it sees an enemy.
        readonly bool callHelp = true;

        // If true, the script will respond to calls for help if not busy.
        readonly bool receiveHelpMsgs = true;

        // The tag to use when calling for help.
        // Only drones with the same tag will respond to calls.
        const string helpTag = "Enemy";

        // The script will ignore all help calls outside of this range.
        const double replyToHelpRange = 4000;

        // Whether to return to origin after enemy contact has been lost.
        // If an enemy is detected during navigation, navigation will be stopped.
        readonly bool returnToOrigin = true;

        // If the drone is farther than this distance from the origin, it will not attempt to return.
        // Set to 'double.PositiveInfinity' for the drone to always return.
        readonly double maxOriginDistance = double.PositiveInfinity;

        // Specify a GPS coordinate here to use as the initial drone origin.
        // If not specified, origin will be set to the current position.
        readonly string gpsOrigin = "";

        // This is the frequency that the script is running at. 
        // Change at your own risk.
        // Values:
        // Update1 - Runs the script every tick
        // Update10 - Runs the script every 10th tick
        // Update100 - Runs the script every 100th tick
        readonly UpdateFrequency frequency = UpdateFrequency.Update1;
        // =====================================

        // ============= Commands ==============
        // stop - stops the script and releases control to you
        // start - starts the script after a stop command
        // origin;x;y;z - set the origin to a specific coordinate and navigate to it.
        // id;newId - sets a new id on this drone.
        // enemy;x;y;z;vx;vy;vz - position and velocity of the enemy to navigate to.
        // return - navigates to the origin point.
        // =====================================

        List<IMyLargeTurretBase> turrets;
        Enemy? lastEnemy;
        int contactTime = 0;
        IMyRemoteControl rc;
        ThrusterControl thrust;
        GyroControl gyros;
        bool detected = false;
        readonly Random r = new Random();
        bool fire = false;
        bool useRockets = false;
        bool prevFire = false;
        bool prevUseRockets = false;
        int startRuntime = 0;
        Vector3D origin;
        List<IMyUserControllableGun> guns;
        readonly List<IMySmallMissileLauncher> rockets = new List<IMySmallMissileLauncher>();
        IMyBroadcastListener helpListener;
        readonly Clock clock;
        public static Program prg;

        enum RocketMode
        {
            None, LargeGrid, All
        }

        // Orbiting
        Vector3D v1;
        Vector3D v2;
        double theta;
        double radius;
        double orbitStep;
        bool useGravity = false;

        Program ()
        {
            prg = this;
            clock = new Clock(frequency);
            startRuntime = clock.GetTick(startDelay);
            if (startRuntime == 0)
                Start();
            Runtime.UpdateFrequency = frequency;
        }

        void Start ()
        {
            turrets = GetBlocks<IMyLargeTurretBase>(turretGroup);

            rc = GetBlock<IMyRemoteControl>(rcName);
            if (rc == null)
                throw new Exception("Remote control block not found.");
            
            gyros = new GyroControl(rc, frequency, GetBlocks<IMyGyro>(gyroGroup));

            MyWaypointInfo temp;
            if (!string.IsNullOrEmpty(gpsOrigin) && MyWaypointInfo.TryParse(gpsOrigin, out temp))
                origin = temp.Coords;
            else
                origin = rc.GetPosition();

            guns = new List<IMyUserControllableGun>();
            foreach (IMyUserControllableGun g in GetBlocks<IMyUserControllableGun>(gunsGroup))
            {
                if (!(g is IMyLargeTurretBase))
                {
                    g.SetValueBool("Shoot", false);
                    IMySmallMissileLauncher m = g as IMySmallMissileLauncher;
                    if (m != null)
                        rockets.Add(m);
                    else
                        guns.Add(g);
                }
            }

            if (guns.Count == 0 && rockets.Count > 0)
                rocketMode = RocketMode.All;
            if (rockets.Count == 0 && guns.Count > 0)
                rocketMode = RocketMode.None;

            thrust = new ThrusterControl(rc, frequency, GetBlocks<IMyThrust>(), ThrusterControl.Mode.OnOff);
            maxAngle *= Math.PI / 180;
            startRuntime = -1;

            if (receiveHelpMsgs)
            {
                helpListener = IGC.RegisterBroadcastListener(helpTag);
                helpListener.SetMessageCallback("");
            }
        }

        void InitializeOrbit ()
        {
            if (!lastEnemy.HasValue)
                return;

            v1 = rc.GetPosition() - lastEnemy.Value.Position;
            radius = MathHelper.Clamp(v1.Length(), minRadius, maxRadius);
            v1.Normalize();

            if (!useGravity)
            {
                Vector3D temp = RandomVector();
                v2 = v1.Cross(temp);
            }
            else
            {
                Vector3D gravity = rc.GetNaturalGravity();
                gravity.Normalize();
                Vector3D temp = gravity.Dot(v1) * gravity;
                v1 -= temp;
                v2 = v1.Cross(gravity);
            }


            v2.Normalize();

            theta = 0;

            double angularVelocity = orbitVelocity / radius;
            orbitStep = angularVelocity * (1f / 60);
        }

        Vector3D Orbit ()
        {
            Vector3D point = (radius * Math.Cos(theta) * v1) + (radius * Math.Sin(theta) * v2);
            theta += orbitStep;
            if (theta > (2 * Math.PI))
                theta = 0;
            return point;
        }

        Vector3D RandomVector ()
        {
            double x = (r.NextDouble() * 2) - 1;
            double y = (r.NextDouble() * 2) - 1;
            double z = (r.NextDouble() * 2) - 1;
            return new Vector3D(x, y, z);
        }

        void Detect ()
        {
            if (turrets.Count == 0)
            {
                thrust.Reset();
                gyros.Reset();
                throw new Exception("No turrets remain for detection.");
            }

            foreach (IMyLargeTurretBase t in turrets)
            {
                if (t.HasTarget)
                {
                    MyDetectedEntityInfo info = t.GetTargetedEntity();
                    lastEnemy = new Enemy(info);

                    if (rocketMode == RocketMode.All)
                        useRockets = true;
                    else if (rocketMode == RocketMode.LargeGrid)
                        useRockets = info.Type == MyDetectedEntityType.LargeGrid;

                    contactTime = clock.Runtime;
                    detected = true;
                    return;
                }
            }
            detected = false;
        }

        void Main (string argument, UpdateType updateSource)
        {
            if (updateSource == UpdateType.Update1 || updateSource == UpdateType.Update10 || updateSource == UpdateType.Update100)
            {
                // Main code
                clock.Update();
                if (clock.Runtime == startRuntime)
                {
                    Echo("Starting.");
                    Start();
                }
                else if (startRuntime != -1)
                {
                    Echo("Waiting to start:");
                    double sec = startDelay - clock.GetSeconds(0);
                    Echo(sec.ToString("0.0") + 's');
                    return;
                }

                Echo("Running.");
                Detect();
                Echo("Has target: " + detected);
                Echo("Known location: " + lastEnemy.HasValue);
                Echo("Firing: " + fire);
                Echo($"{useRockets} {guns.Count} {rockets.Count} {rocketMode}");
                double secSince = clock.GetSeconds(contactTime);
                if (secSince > 0 && secSince < timeout)
                    Echo("Time since contact: " + secSince.ToString("0.00"));

                double temp = rc.GetNaturalGravity().Length();
                bool newValue = temp > useGravityValue;
                if (newValue != useGravity)
                {
                    useGravity = newValue;
                    InitializeOrbit();
                }

                Move();
                FireState();
            }
            else if (updateSource == UpdateType.IGC)
            {
                if (helpListener.HasPendingMessage && receiveHelpMsgs && !detected && !rc.IsAutoPilotEnabled)
                {
                    MyTuple<Vector3D, Vector3D> pos = (MyTuple<Vector3D, Vector3D>)helpListener.AcceptMessage().Data;
                    if (Vector3D.Distance(pos.Item1, rc.GetPosition()) > replyToHelpRange)
                        return;

                    lastEnemy = null;
                    rc.SetAutoPilotEnabled(false);
                    rc.ClearWaypoints();
                    rc.AddWaypoint(new MyWaypointInfo("Enemy", pos.Item1));
                    rc.SetAutoPilotEnabled(true);
                }
            }
            else
            {
                // Remote command
                Command(argument);
            }
        }

        void FireState ()
        {
            if (fire != prevFire || useRockets != prevUseRockets)
            {
                if (fire)
                {
                    // Starting to fire
                    InitializeOrbit();
                    if (callHelp && lastEnemy.HasValue)
                        IGC.SendBroadcastMessage<MyTuple<Vector3D, Vector3D>>(helpTag, new MyTuple<Vector3D, Vector3D>(lastEnemy.Value.Position, lastEnemy.Value.Velocity));
                    foreach (IMyUserControllableGun g in guns)
                        g.SetValueBool("Shoot", !useRockets);
                    foreach (IMySmallMissileLauncher m in rockets)
                        m.SetValueBool("Shoot", useRockets);
                }
                else
                {
                    // Stopping fire
                    foreach (IMyUserControllableGun g in guns)
                        g.SetValueBool("Shoot", false);
                    foreach (IMySmallMissileLauncher m in rockets)
                        m.SetValueBool("Shoot", false);
                }
                prevFire = fire;
                prevUseRockets = useRockets;
            }
        }


        private void Move ()
        {
            if (!lastEnemy.HasValue)
                return;

            Vector3D up = new Vector3D();
            if (useGravity)
                up = Vector3D.Normalize(-rc.GetNaturalGravity());

            Vector3D myPos = rc.GetPosition();
            if (detected)
            {
                Vector3D enemyPos = lastEnemy.Value.Position;

                if (returnToOrigin && Vector3D.Distance(enemyPos, myPos) > maxOriginDistance)
                {
                    lastEnemy = null;
                    if (rc.IsAutoPilotEnabled)
                        return;
                    thrust.Reset();
                    gyros.Reset();
                    ReturnOrigin();
                    return;
                }

                rc.SetAutoPilotEnabled(false);

                // Target is in range, no prediction needed
                Vector3D enemyVel = lastEnemy.Value.Velocity;
                Echo(enemyPos.ToString());
                if (useRockets)
                    enemyPos = GetRocketLead(enemyPos, enemyVel);
                else
                    enemyPos = GetLeadPosition(enemyPos, enemyVel);
                Echo(myPos.ToString());
                Vector3D meToTarget = Vector3D.Normalize(enemyPos - myPos);
                gyros.FaceVectors(meToTarget, up);

                Fire(meToTarget);


                Vector3D accel;
                if (fire)
                    accel = thrust.ControlPosition(enemyPos + Orbit(), enemyVel);
                else
                    accel = thrust.ControlVelocity(enemyVel);
                thrust.ApplyAccel(accel);

            }
            else
            {
                // Move towards predicted enemy position
                fire = false;
                double sec = clock.GetSeconds(contactTime);
                if (sec > timeout)
                {
                    lastEnemy = null;
                    thrust.Reset();
                    gyros.Reset();
                    if (returnToOrigin)
                        ReturnOrigin();
                    return;
                }
                else
                {

                    Vector3D enemyVel = lastEnemy.Value.Velocity;
                    Vector3D enemyPos = lastEnemy.Value.Position + enemyVel * sec;
                    Vector3D meToTarget = Vector3D.Normalize(enemyPos - myPos);
                    gyros.FaceVectors(meToTarget, Vector3D.Zero);

                    thrust.ApplyAccel(thrust.ControlPosition(enemyPos, enemyVel));
                }

            }
        }

        private void Fire (Vector3D targetToMe)
        {
            Vector3D forward = Vector3D.Normalize(rc.WorldMatrix.Forward);
            Vector3D normalized = Vector3D.Normalize(targetToMe);
            double dot = Vector3D.Dot(normalized, forward);
            double angle = Math.Acos(dot);
            fire = angle < maxAngle;
        }

        void Command (string command)
        {
            string [] args = command.Split(';');
            switch (args [0])
            {
                case "stop":
                    thrust.Reset();
                    gyros.Reset();
                    foreach (IMyUserControllableGun g in guns)
                        g.SetValueBool("Shoot", false);
                    fire = false;
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    break;
                case "start":
                    Runtime.UpdateFrequency = frequency;
                    break;
                case "origin": // origin;x;y;z
                    if (args.Length != 4)
                        return;
                    Vector3D temp;
                    if (!StringToVector(args [1], args [2], args [3], out temp))
                        return;
                    origin = temp;
                    ReturnOrigin();
                    break;
                case "return":
                    ReturnOrigin();
                    break;

            }
        }

        bool StringToVector (string x, string y, string z, out Vector3D output)
        {
            try
            {
                double x2 = double.Parse(x);
                double y2 = double.Parse(y);
                double z2 = double.Parse(z);
                output = new Vector3D(x2, y2, z2);
                return true;
            }
            catch (Exception)
            {
                output = new Vector3D();
                return false;
            }
        }

        void ReturnOrigin ()
        {
            thrust.Reset();
            gyros.Reset();
            rc.ClearWaypoints();
            rc.AddWaypoint(new MyWaypointInfo("Origin", origin));
            rc.FlightMode = FlightMode.OneWay;
            rc.SetAutoPilotEnabled(true);
        }

        Vector3D GetLeadPosition (Vector3D targetPos, Vector3D targetVel)
        {
            Vector3D ownPos = rc.GetPosition();
            Vector3D ownVelocity = rc.GetShipVelocities().LinearVelocity;
            double oldTime = 0.01;
            Vector3D predictedPos = new Vector3D();
            for (int i = 0; i < 20; i++)
            {
                predictedPos = targetPos + targetVel * oldTime;
                Vector3D path = predictedPos - ownPos;
                double newTime = path.Length() / muzzleVelocity;
                if (Math.Abs(newTime - oldTime) < 0.000001)
                    break;
                oldTime = newTime;
            }
            return predictedPos - ownVelocity * oldTime;
        }

        // The exact algorithm used by keen's rocket turrets.
        // MAYBE if SOMEONE implemented rockets that don't defy the laws of physics this wouldn't be necessary.
        Vector3D GetRocketLead (Vector3D targetPos, Vector3D targetVel)
        {
            double muzzleVelocity = 400;
            double maxTrajectory = 804;
            Vector3D deltaPos = targetPos - rc.GetPosition();

            double num3 = (muzzleVelocity < 1E-05f) ? 1E-06f : (maxTrajectory / muzzleVelocity);
            Vector3D myVelocity = rc.GetShipVelocities().LinearVelocity;
            double num4 = MathHelper.Clamp(RocketIntercept(deltaPos, targetVel - myVelocity, muzzleVelocity), 0.0, num3);
            targetPos += num4 * targetVel;
            return targetPos - ((num4 / num3) * myVelocity);
        }

        // The exact algorithm used by keen's rocket turrets.
        double RocketIntercept (Vector3D deltaPos, Vector3D deltaVel, double projectileVel)
        {
            double num = Vector3D.Dot(deltaVel, deltaVel) - (projectileVel * projectileVel);
            double num2 = 2.0 * Vector3D.Dot(deltaVel, deltaPos);
            double num3 = Vector3D.Dot(deltaPos, deltaPos);
            double d = (num2 * num2) - ((4.0 * num) * num3);
            return (d > 0.0) ? ((2.0 * num3) / (Math.Sqrt(d) - num2)) : -1.0;
        }
        //
    }
}