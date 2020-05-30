using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System.Collections.Generic;
using System;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        // ============= Settings ==============
        // The number of seconds to keep chasing the target after the ship looses sight
        const double timeout = 10;

        // Inventory multiplier setting of the world
        static readonly double inventoryMultiplier = 10;

        // The name of the group that contains all of the guns on the ship.
        // Leave blank to select all guns on the ship.
        readonly string gunsGroup = "";

        // Used when checkFacing is true, this is the max angle in degrees that the ship can be facing relative to the target.
        // This prevents the ship from firing while it is rotating.
        double maxAngle = 20;

        // This is the velocity of the bullet. Only constant velocities supported for now. (So Gatling Guns only)
        // https://spaceengineerswiki.com/Weapons
        // Not used if the ship is firing rockets.
        const double muzzleVelocity = 400;

        // When the ship is firing rockets, the ship will lead based on the rockets instead of the muzzle velocity.
        // (Rockets have a more complex flight path)
        // Values: None, LargeGrid, All
        readonly RocketMode rocketMode = RocketMode.LargeGrid;

        // Whether to return to origin after enemy contact has been lost.
        // If an enemy is detected during navigation, navigation will be stopped.
        const bool returnToOrigin = true;

        // The time in seconds to wait before starting the script.
        const double startDelay = 0;

        // The range of the turret used for detection.
        const double turretRange = 800;

        // Orbit settings
        const double minRadius = 400;
        const double maxRadius = 700;
        const double orbitVelocity = 20;

        // When gravity is above this value, the orbit pattern will be flat aligned with gravity.
        const double useGravityValue = 0.5;

        // Call help settings
        const bool callHelp = true;
        const bool receiveHelpMsgs = true;

        // Used to protect commands transmitted via antenna.
        const string commsId = "drones";
        const double replyToHelpRange = turretRange * 3;

        // The name of the group of turrets to use for detection.
        // Leave black to allow the script to use all turrets on the ship.
        const string turretGroup = "";

        // The name of the remote control block.
        // Leave black to have the script use a random remote control block on the ship.
        const string rcName = "";

        // If true, the script will be able to see blocks attached to subgrids. (rotors, pistons)
        const bool useSubgrids = true;
        // =====================================

        // ============= Commands ==============
        // stop - stops the script and releases control to you
        // start - starts the script after a stop command
        // origin;x;y;z - set the origin to a specific coordinate and navigate to it.
        // id;newId - sets a new id on this drone.
        // enemy;x;y;z;vx;vy;vz - position and velocity of the enemy to navigate to.
        // return - navigates to the origin point.
        // If transmitting via antenna, commands must be prefixed by <password>:
        // =====================================

        const UpdateFrequency frequency = UpdateFrequency.Update1;
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
        const string commsTag = "AttackDrone_" + commsId;
        IMyBroadcastListener helpListener;

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
            Clock.Start(frequency);
            startRuntime = Clock.GetTick(startDelay);
            if (startRuntime == 0)
                Start();
            Runtime.UpdateFrequency = frequency;
            //instance = this;
        }

        void Start ()
        {
            gridSystem = GridTerminalSystem;
            gridId = Me.CubeGrid.EntityId;
            if (string.IsNullOrWhiteSpace(turretGroup))
                turrets = GetBlocks<IMyLargeTurretBase>(useSubgrids);
            else
                turrets = GetBlocks<IMyLargeTurretBase>(turretGroup, useSubgrids);
            if (string.IsNullOrWhiteSpace(rcName))
                rc = GetBlock<IMyRemoteControl>();
            else
                rc = GetBlock<IMyRemoteControl>(rcName, useSubgrids);
            if (rc == null)
                throw new Exception("Remote control block not found.");
            gyros = new GyroControl(rc);
            origin = rc.GetPosition();

            this.guns = new List<IMyUserControllableGun>();
            List<IMyUserControllableGun> guns;
            if (gunsGroup == "_" || string.IsNullOrWhiteSpace(gunsGroup))
                guns = GetBlocks<IMyUserControllableGun>(useSubgrids);
            else
                guns = GetBlocks<IMyUserControllableGun>(gunsGroup, useSubgrids);
            foreach (IMyUserControllableGun g in guns)
            {
                if (!(g is IMyLargeTurretBase))
                {
                    g.SetValueBool("Shoot", false);
                    if (rocketMode != RocketMode.None)
                    {
                        IMySmallMissileLauncher m = g as IMySmallMissileLauncher;
                        if (m != null)
                        {
                            rockets.Add(m);
                            continue;
                        }
                    }
                    this.guns.Add(g);
                }
            }

            if (receiveHelpMsgs)
            {
                helpListener = IGC.RegisterBroadcastListener("AttackDrone_" + commsId);
                helpListener.SetMessageCallback("");
            }
                

            thrust = new ThrusterControl(rc);
            maxAngle *= Math.PI / 180;
            startRuntime = -1;
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

                    contactTime = Clock.Runtime;
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
                Clock.Update();
                if (Clock.Runtime == startRuntime)
                {
                    Echo("Starting.");
                    Start();
                }
                else if (startRuntime != -1)
                {
                    Echo("Waiting to start:");
                    double sec = startDelay - Clock.GetSeconds(0);
                    Echo(sec.ToString("0.0") + 's');
                    return;
                }

                Echo("Running.");
                Detect();
                Echo("Has target: " + detected);
                Echo("Known location: " + lastEnemy.HasValue);
                Echo("Firing: " + fire);
                double secSince = Clock.GetSeconds(contactTime);
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
                if(helpListener != null && receiveHelpMsgs && !detected && helpListener.HasPendingMessage && !rc.IsAutoPilotEnabled)
                {
                    MyIGCMessage msg = helpListener.AcceptMessage();
                    if(msg.Data is Vector3D)
                    {
                        Vector3D pos = (Vector3D)msg.Data;
                        double dist2 = Vector3D.DistanceSquared(rc.GetPosition(), pos);
                        if(dist2 < replyToHelpRange * replyToHelpRange)
                        {
                            lastEnemy = null;
                            rc.SetAutoPilotEnabled(false);
                            rc.ClearWaypoints();
                            rc.AddWaypoint(new MyWaypointInfo("Enemy", pos));
                            rc.FlightMode = FlightMode.OneWay;
                            rc.SetAutoPilotEnabled(true);
                        }
                    }
                }
            }
            else
            {
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
                    if (lastEnemy.HasValue)
                        CallHelp(lastEnemy.Value.Position);
                    foreach (IMyUserControllableGun g in guns)
                        g.SetValueBool("Shoot", true);
                    if (useRockets)
                    {
                        foreach (IMySmallMissileLauncher m in rockets)
                            m.SetValueBool("Shoot", true);
                    }
                }
                else
                {
                    // Stoping fire
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

            rc.SetAutoPilotEnabled(false);

            Vector3D up = new Vector3D();
            if (useGravity)
                up = Vector3D.Normalize(-rc.GetNaturalGravity());

            Vector3D myPos = rc.GetPosition();
            if (detected)
            {
                // Target is in range, no prediction needed
                Vector3D enemyVel = lastEnemy.Value.Velocity;
                Vector3D enemyPos = lastEnemy.Value.Position;
                Echo(enemyPos.ToString());
                if (useRockets)
                    enemyPos = GetRocketLead(enemyPos, enemyVel);
                else
                    enemyPos = GetLeadPosition(enemyPos, enemyVel);
                Echo(myPos.ToString());
                Vector3D meToTarget = Vector3D.Normalize(enemyPos - myPos);
                gyros.FaceVectors(meToTarget, up);

                Fire(meToTarget);

                Vector3D velocity = enemyVel;
                if (fire)
                {
                    Vector3D target = enemyPos + Orbit();
                    Vector3D difference = target - myPos;
                    double diffLen = difference.Length();
                    Vector3D stop = thrust.StopDistance(1, enemyVel);
                    double stopLen = stop.Length();
                    if (!double.IsInfinity(stopLen))
                    {
                        if (stopLen > diffLen)
                            difference = Vector3D.Zero;
                        else
                            difference -= stop;
                    }
                    velocity += difference;
                }
                thrust.Velocity = velocity;
            }
            else
            {
                // Move towards predicted enemy position
                fire = false;
                double sec = Clock.GetSeconds(contactTime);
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

                    Vector3D difference = enemyPos - rc.GetPosition();
                    double diffLen = difference.Length();
                    if (diffLen < turretRange)
                        return;

                    // Compensate for the velocity of the ship so the ship doesn't run into the target
                    Vector3D stop = thrust.StopDistance(1, enemyVel);
                    double stopLen = stop.Length();
                    if (!double.IsInfinity(stopLen))
                    {
                        if (stopLen > diffLen)
                            difference = Vector3D.Zero;
                        else
                            difference -= stop;
                    }

                    Vector3D velocity = enemyVel + difference;
                    thrust.Velocity = velocity;
                }

            }
            thrust.Update();
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

        // A recompilation of keen's turret AI
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

        // A recompilation of keen's turret AI
        double RocketIntercept (Vector3D deltaPos, Vector3D deltaVel, double projectileVel)
        {
            double num = Vector3D.Dot(deltaVel, deltaVel) - (projectileVel * projectileVel);
            double num2 = 2.0 * Vector3D.Dot(deltaVel, deltaPos);
            double num3 = Vector3D.Dot(deltaPos, deltaPos);
            double d = (num2 * num2) - ((4.0 * num) * num3);
            return (d > 0.0) ? ((2.0 * num3) / (Math.Sqrt(d) - num2)) : -1.0;
        }

        void CallHelp (Vector3D position)
        {
            if (callHelp)
                IGC.SendBroadcastMessage<Vector3D>(commsTag, position);
        }

    }
}