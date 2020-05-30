using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System.Collections.Generic;
using System;
using VRageMath;
using System.Linq;
using System.Security.Cryptography;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        // ============= Settings ==============
        // The number of seconds to keep chasing the target after the ship looses sight
        const double timeout = 10;

        // Whether to return to origin after enemy contact has been lost.
        // If an enemy is detected during navigation, navigation will be stopped.
        readonly bool returnToOrigin = true;

        // The time in seconds to wait before starting the script.
        const double startDelay = 0;

        // When the distance to the target is less than this distance, the ship will stop moving.
        // This should be less than your turret range.
        // The ship should also have enough room to stop from full autopilot speed within this distance.
        const double minDistance = 500;

        // The maximum distance to the target that the autopilot will be set to navigate.
        const double maxDistance = 1000;

        // Call help settings
        const bool callHelp = true;
        const bool receiveHelpMsgs = true;

        // Used to protect commands transmitted via antenna.
        const string commsId = "drones";
        const double replyToHelpRange = 3000;

        // The name of the group of turrets to use for detection.
        // Leave black to allow the script to use all turrets on the ship.
        readonly string turretGroup = "";

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
        bool detected = false;
        int startRuntime = 0;
        Vector3D origin;
        const string commsTag = "AttackDrone_" + commsId;
        IMyBroadcastListener helpListener;

        enum RocketMode
        {
            None, LargeGrid, All
        }

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
            origin = rc.GetPosition();

            if (receiveHelpMsgs)
            {
                helpListener = IGC.RegisterBroadcastListener("AttackDrone_" + commsId);
                helpListener.SetMessageCallback("");
            }

            startRuntime = -1;
        }

        void Detect ()
        {
            if (turrets.Count == 0)
            {
                Stop();
                throw new Exception("No turrets remain for detection.");
            }

            foreach (IMyLargeTurretBase t in turrets)
            {
                if (t.HasTarget)
                {
                    MyDetectedEntityInfo info = t.GetTargetedEntity();
                    lastEnemy = new Enemy(info);
                    contactTime = Clock.Runtime;
                    if (!detected && callHelp)
                        IGC.SendBroadcastMessage<Vector3D>(commsTag, info.Position);
                    detected = true;
                    return;
                }
            }
            detected = false;
        }

        private void GoTo(Vector3D pos, string label)
        {
            Stop();
            rc.AddWaypoint(new MyWaypointInfo(label, pos));
            rc.FlightMode = FlightMode.OneWay;
            rc.SetAutoPilotEnabled(true);
        }

        private void Stop ()
        {
            if (rc.IsAutoPilotEnabled)
            {
                rc.SetAutoPilotEnabled(false);
                rc.ClearWaypoints();
            }
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
                double secSince = Clock.GetSeconds(contactTime);
                if (secSince > 0 && secSince < timeout)
                    Echo("Time since contact: " + secSince.ToString("0.00"));

                Move();
            }
            else if (updateSource == UpdateType.IGC)
            {
                if (helpListener != null && receiveHelpMsgs && !detected && helpListener.HasPendingMessage && !rc.IsAutoPilotEnabled)
                {
                    MyIGCMessage msg = helpListener.AcceptMessage();
                    if (msg.Data is Vector3D)
                    {
                        Vector3D pos = (Vector3D)msg.Data;
                        double dist2 = Vector3D.DistanceSquared(rc.GetPosition(), pos);
                        if (dist2 < replyToHelpRange * replyToHelpRange)
                        {
                            lastEnemy = null;
                            GoTo(pos, "HelpCall");
                        }
                    }
                }
            }
            else
            {
                Command(argument);
            }
        }

        private void Move ()
        {
            if (!lastEnemy.HasValue)
                return;

            Vector3D me = rc.GetPosition();
            Vector3D target;
            if(detected)
            {
                // Target is in range, no prediction needed
                target = lastEnemy.Value.Position;
                if(Vector3D.DistanceSquared(target, me) < minDistance * minDistance)
                {
                    Stop();
                    return;
                }
            }
            else
            {
                // Target is out of range
                double sec = Clock.GetSeconds(contactTime);
                if (sec > timeout)
                {
                    // Give up
                    lastEnemy = null;
                    if (returnToOrigin)
                        GoTo(origin, "Origin");
                    else
                        Stop();
                    return;
                }
                else
                {
                    // Predict the target
                    target = lastEnemy.Value.Position + lastEnemy.Value.Velocity * sec;
                }
            }

            // There is an enemy to get to
            if(rc.IsAutoPilotEnabled)
            {
                MyWaypointInfo destination = rc.CurrentWaypoint;
                if (!destination.IsEmpty())
                {
                    if (destination.Name != "Enemy")
                        GoTo(target, "Enemy");

                }
            }
            else
            {
                Echo("Target pos: " + target);
                Vector3D meToTarget = target - me;
                double dist2 = meToTarget.LengthSquared();
                Echo("Target dist: " + Math.Sqrt(dist2));
                if (dist2 > minDistance * minDistance)
                {
                    if(dist2 > maxDistance * maxDistance)
                        target = ((meToTarget / Math.Sqrt(dist2)) * maxDistance) + me;
                    GoTo(target, "Enemy");
                }
            }
        }

        void Command (string command)
        {
            string [] args = command.Split(';');
            switch (args [0])
            {
                case "stop":
                    Stop();
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
                    GoTo(origin, "Origin");
                    break;
                case "return":
                    GoTo(origin, "Origin");
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

    }
}