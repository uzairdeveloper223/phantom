using System;
using GTA;
using GTA.Math;
using GTA.Native;

namespace PHANTOM
{
    public sealed class PhantomTasks
    {
        private const Hash TASK_GUARD_POSITION = (Hash)0x4A58A47A72E3FCB4;
        private const float GUARD_PATROL_RADIUS = 15.0f;
        private const float SUICIDE_DETONATION_RANGE = 4f;
        private const float RAM_DETONATION_RANGE = 6f;
        private const int ENTER_VEHICLE_TIMEOUT = 10000;
        private const float ENTER_VEHICLE_SPEED = 2.0f;
        private const float KAMIKAZE_DRIVE_SPEED = 200f;
        private const int KAMIKAZE_DRIVE_FLAGS = 786603;
        private const float SLOW_FOLLOW_MULTIPLIER = 0.3f;
        private const float HIJACK_DELIVERY_MULTIPLIER = 0.5f;
        private const float HIJACK_DELIVERY_RANGE = 15f;
        private const float ARRIVAL_RANGE = 15f;
        private const float STOP_RANGE = 5f;
        private const int WAYPOINT_BLIP_ID = 8;
        private const int CRUISE_DRIVE_FLAGS = 786468;

        private readonly ScriptSettings config;
        private readonly float drivingSpeed;
        private readonly int drivingFlags;
        private readonly float hijackRange;
        private readonly float combatRadius;
        private readonly float cruiseSpeed;

        public PhantomTasks(ScriptSettings config)
        {
            this.config = config
                ?? throw new ArgumentNullException(nameof(config));

            drivingSpeed = config.GetValue(
                "Vehicle", "DrivingSpeed", 80.0f
            );
            drivingFlags = config.GetValue(
                "Vehicle", "DrivingFlags", 786603
            );
            hijackRange = config.GetValue(
                "Behavior", "HijackRange", 120.0f
            );
            combatRadius = config.GetValue(
                "Behavior", "CombatRadius", 150.0f
            );
            cruiseSpeed = config.GetValue(
                "Vehicle", "CruiseSpeed", 30.0f
            );
        }

        public void PlayRadioCallAnimation(Ped phantom)
        {
            if (phantom == null || !phantom.Exists())
                return;

            string animDict = phantom.IsSittingInVehicle()
                ? "cellphone@in_car@ds"
                : "cellphone@";
            string animName = "cellphone_call_listen_a";

            Function.Call(
                Hash.REQUEST_ANIM_DICT, animDict
            );

            int timeout = Game.GameTime + 2000;
            while (!Function.Call<bool>(
                Hash.HAS_ANIM_DICT_LOADED, animDict))
            {
                if (Game.GameTime > timeout)
                    return;
                Script.Yield();
            }

            Function.Call(
                Hash.TASK_PLAY_ANIM,
                phantom.Handle,
                animDict,
                animName,
                8.0f,
                -8.0f,
                3000,
                49,
                0f,
                false,
                false,
                false
            );
        }

        public void ExecuteWait(Ped phantom)
        {
            if (phantom == null || !phantom.Exists())
                return;

            phantom.Task.ClearAll();
            Function.Call(
                TASK_GUARD_POSITION,
                phantom.Handle,
                GUARD_PATROL_RADIUS,
                GUARD_PATROL_RADIUS,
                true
            );
        }

        public void ExecuteCombat(Ped phantom)
        {
            if (phantom == null || !phantom.Exists())
                return;

            phantom.BlockPermanentEvents = false;
            phantom.Task.FightAgainstHatedTargets(combatRadius);
        }

        public void ExecuteVehicleHijack(Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (player == null || !player.Exists())
                return;

            Vehicle target = FindNearestVehicle(phantom, player);

            if (target == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: No vehicles nearby."
                );
                return;
            }

            phantom.Task.ClearAll();
            phantom.Task.EnterVehicle(
                target,
                VehicleSeat.Driver,
                ENTER_VEHICLE_TIMEOUT,
                ENTER_VEHICLE_SPEED,
                EnterVehicleFlags.None
            );
        }

        public void ExecuteDriveTo(Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (player == null || !player.Exists())
                return;

            Vehicle playerVehicle = player.CurrentVehicle;

            if (playerVehicle == null || !playerVehicle.Exists())
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Get in a vehicle first."
                );
                return;
            }

            Blip waypoint = GetWaypointBlip();

            if (waypoint == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Set a waypoint first."
                );
                return;
            }

            Vector3 destination = waypoint.Position;

            phantom.SetIntoVehicle(playerVehicle, VehicleSeat.Driver);

            if (player.SeatIndex == VehicleSeat.Driver)
            {
                player.SetIntoVehicle(
                    playerVehicle, VehicleSeat.Passenger
                );
            }

            phantom.Task.DriveTo(
                playerVehicle,
                destination,
                ARRIVAL_RANGE,
                drivingSpeed,
                (DrivingStyle)drivingFlags
            );
        }

        public void ExecuteSuicideBomb(Ped phantom, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            phantom.Task.ClearAll();

            if (target is Ped targetPed)
                phantom.Task.GoTo(targetPed);
            else
                phantom.Task.GoTo(target.Position);
        }

        public void ExecuteRamExplode(
            Ped phantom, Ped player, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            Vehicle ramVehicle = FindNearestVehicle(phantom, player);

            if (ramVehicle == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: No vehicle to ram with."
                );
                return;
            }

            phantom.Task.ClearAll();
            phantom.SetIntoVehicle(ramVehicle, VehicleSeat.Driver);

            if (target is Ped targetPed)
            {
                phantom.Task.ChaseWithGroundVehicle(targetPed);
            }
            else
            {
                Vector3 targetPos = target.Position;
                Function.Call(
                    Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                    phantom.Handle,
                    ramVehicle.Handle,
                    targetPos.X,
                    targetPos.Y,
                    targetPos.Z,
                    KAMIKAZE_DRIVE_SPEED,
                    KAMIKAZE_DRIVE_FLAGS,
                    0f
                );
            }
        }

        public void ExecuteExecuteTarget(Ped phantom, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            phantom.Task.ClearAll();

            if (target is Ped targetPed)
            {
                phantom.Task.FightAgainst(targetPed);
                return;
            }

            if (target is Vehicle targetVehicle)
            {
                Ped driver = targetVehicle.Driver;
                if (driver != null && driver.Exists() && driver.IsAlive)
                    phantom.Task.FightAgainst(driver);
                else
                    phantom.Task.ShootAt(
                        targetVehicle.Position, 10000
                    );
            }
        }

        public bool TickSuicideBomb(Ped phantom, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return true;
            if (target == null || !target.Exists())
                return true;

            float dist = phantom.Position.DistanceTo(target.Position);
            if (dist >= SUICIDE_DETONATION_RANGE)
                return false;

            World.AddExplosion(
                target.Position,
                ExplosionType.Car,
                10f,
                1.0f
            );

            if (phantom.Exists())
                phantom.Health = 0;

            GTA.UI.Notification.Show(
                "~r~PHANTOM: Target eliminated. PHANTOM down."
            );
            return true;
        }

        public bool TickRamExplode(Ped phantom, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return true;
            if (target == null || !target.Exists())
                return true;

            float dist = phantom.Position.DistanceTo(target.Position);
            if (dist >= RAM_DETONATION_RANGE)
                return false;

            World.AddExplosion(
                target.Position,
                ExplosionType.Car,
                15f,
                2.0f
            );

            GTA.UI.Notification.Show(
                "~r~PHANTOM: Target destroyed."
            );
            return true;
        }

        public bool TickExecuteTarget(Ped phantom, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return true;

            if (target == null || !target.Exists())
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Target eliminated."
                );
                return true;
            }

            if (target is Ped tp && !tp.IsAlive)
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Target eliminated."
                );
                return true;
            }

            if (target is Vehicle tv)
            {
                Ped driver = tv.Driver;
                bool driverDead = driver == null
                    || !driver.Exists()
                    || !driver.IsAlive;
                if (driverDead)
                {
                    GTA.UI.Notification.Show(
                        "~g~PHANTOM: Target eliminated."
                    );
                    return true;
                }
            }

            return false;
        }

        public void TickDriveTo(Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (player == null || !player.Exists())
                return;

            Vehicle phantomVehicle = phantom.CurrentVehicle;
            if (phantomVehicle == null || !phantomVehicle.Exists())
                return;

            if (player.IsSittingInVehicle(phantomVehicle))
                return;

            phantom.Task.DriveTo(
                phantomVehicle,
                player.Position,
                STOP_RANGE,
                drivingSpeed * SLOW_FOLLOW_MULTIPLIER,
                (DrivingStyle)drivingFlags
            );
        }

        public void TickCombat(Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;

            int wantedLevel = Game.Player.WantedLevel;
            Ped target = FindCombatTarget(
                phantom, player, wantedLevel > 0
            );

            if (target != null)
            {
                phantom.Task.FightAgainst(target);
                return;
            }

            if (!phantom.IsInCombat)
                phantom.Task.FightAgainstHatedTargets(combatRadius);
        }

        private Ped FindCombatTarget(
            Ped phantom, Ped player, bool prioritizeCops)
        {
            Ped[] nearby = World.GetNearbyPeds(
                phantom.Position, combatRadius
            );

            Ped bestCop = null;
            float bestCopDist = float.MaxValue;
            Ped bestAny = null;
            float bestAnyDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Ped ped = nearby[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive)
                    continue;
                if (ped == player || ped == phantom)
                    continue;
                if (ped.IsInGroup)
                    continue;

                float dist = phantom.Position.DistanceTo(
                    ped.Position
                );

                bool isCop = IsCopOrArmy(ped);

                if (isCop && dist < bestCopDist)
                {
                    bestCopDist = dist;
                    bestCop = ped;
                }

                if (dist < bestAnyDist)
                {
                    bestAnyDist = dist;
                    bestAny = ped;
                }
            }

            if (prioritizeCops && bestCop != null)
                return bestCop;

            return prioritizeCops ? bestCop : bestAny;
        }

        private static bool IsCopOrArmy(Ped ped)
        {
            const uint COP_GROUP = 0xA49E591C;
            const uint ARMY_GROUP = 0xE3D976F3;
            const uint SECURITY_GROUP = 0xF50B51B7;

            int groupHash = Function.Call<int>(
                Hash.GET_PED_RELATIONSHIP_GROUP_HASH,
                ped.Handle
            );

            uint pedHash = (uint)groupHash;
            return pedHash == COP_GROUP
                || pedHash == ARMY_GROUP
                || pedHash == SECURITY_GROUP;
        }

        public void TickVehicleHijack(Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (player == null || !player.Exists())
                return;

            if (!phantom.IsSittingInVehicle())
                return;

            Vehicle hijacked = phantom.CurrentVehicle;
            if (hijacked == null || !hijacked.Exists())
                return;

            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED,
                hijacked.Handle, 0
            );
            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED_FOR_PLAYER,
                hijacked.Handle,
                Game.Player.Handle,
                false
            );
            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED_FOR_ALL_PLAYERS,
                hijacked.Handle,
                false
            );

            float distToPlayer = phantom.Position.DistanceTo(
                player.Position
            );

            if (distToPlayer <= HIJACK_DELIVERY_RANGE)
            {
                if (hijacked.Speed > 1f)
                {
                    phantom.Task.DriveTo(
                        hijacked,
                        player.Position,
                        0f,
                        5f,
                        (DrivingStyle)drivingFlags
                    );
                }
                return;
            }

            phantom.Task.DriveTo(
                hijacked,
                player.Position,
                STOP_RANGE,
                drivingSpeed * HIJACK_DELIVERY_MULTIPLIER,
                (DrivingStyle)drivingFlags
            );
        }

        public static VehicleSeat FindFreeSeat(Vehicle v)
        {
            for (int i = 0;
                i < v.PassengerCapacity + 1; i++)
            {
                VehicleSeat seat = (VehicleSeat)i;
                if (v.IsSeatFree(seat))
                    return seat;
            }
            return VehicleSeat.None;
        }

        private Vehicle FindNearestVehicle(Ped phantom, Ped player)
        {
            Vehicle[] nearby = World.GetNearbyVehicles(
                phantom.Position, hijackRange
            );

            Vehicle playerCurrent = player.CurrentVehicle;
            Vehicle playerLast = player.LastVehicle;

            Vehicle closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle v = nearby[i];
                if (v == null || !v.Exists())
                    continue;
                if (v == playerCurrent || v == playerLast)
                    continue;
                if (phantom.IsInVehicle(v))
                    continue;

                float dist = phantom.Position.DistanceTo(v.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = v;
                }
            }

            return closest;
        }

        public static Blip GetWaypointBlip()
        {
            int handle = Function.Call<int>(
                Hash.GET_FIRST_BLIP_INFO_ID, WAYPOINT_BLIP_ID
            );

            if (!Function.Call<bool>(Hash.DOES_BLIP_EXIST, handle))
                return null;

            return new Blip(handle);
        }

        public void ExecuteChase(Ped phantom, Vehicle target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            if (phantom.IsSittingInVehicle())
            {
                Ped driver = target.Driver;
                if (driver != null && driver.Exists())
                    phantom.Task.ChaseWithGroundVehicle(driver);
                else
                    phantom.Task.DriveTo(
                        phantom.CurrentVehicle,
                        target.Position,
                        STOP_RANGE,
                        drivingSpeed,
                        (DrivingStyle)drivingFlags
                    );
                return;
            }

            Vehicle nearestCar = FindNearestVehicleForChase(
                phantom
            );
            if (nearestCar == null)
            {
                phantom.Task.GoTo(target.Position);
                return;
            }

            phantom.Task.EnterVehicle(
                nearestCar,
                VehicleSeat.Driver,
                ENTER_VEHICLE_TIMEOUT,
                ENTER_VEHICLE_SPEED,
                EnterVehicleFlags.None
            );
        }

        public bool TickChase(Ped phantom, Vehicle target)
        {
            if (phantom == null || !phantom.Exists())
                return true;
            if (target == null || !target.Exists()
                || target.IsDead)
                return true;

            if (!phantom.IsSittingInVehicle())
                return false;

            Ped driver = target.Driver;
            if (driver != null && driver.Exists()
                && driver.IsAlive)
            {
                phantom.Task.ChaseWithGroundVehicle(driver);
            }
            else
            {
                phantom.Task.DriveTo(
                    phantom.CurrentVehicle,
                    target.Position,
                    STOP_RANGE,
                    drivingSpeed,
                    (DrivingStyle)drivingFlags
                );
            }

            return false;
        }

        public void ExecuteCruise(Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (player == null || !player.Exists())
                return;

            Vehicle playerVehicle = player.CurrentVehicle;
            if (playerVehicle == null
                || !playerVehicle.Exists())
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Get in a vehicle first."
                );
                return;
            }

            Blip waypoint = GetWaypointBlip();
            if (waypoint == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Set a waypoint first."
                );
                return;
            }

            Vector3 destination = waypoint.Position;

            phantom.SetIntoVehicle(
                playerVehicle, VehicleSeat.Driver
            );
            if (player.SeatIndex == VehicleSeat.Driver)
            {
                player.SetIntoVehicle(
                    playerVehicle, VehicleSeat.Passenger
                );
            }

            phantom.Task.DriveTo(
                playerVehicle,
                destination,
                ARRIVAL_RANGE,
                cruiseSpeed,
                (DrivingStyle)CRUISE_DRIVE_FLAGS
            );
        }

        public bool TickCruise(Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return true;
            if (player == null || !player.Exists())
                return true;

            if (!player.IsSittingInVehicle())
                return true;

            Blip waypoint = GetWaypointBlip();
            if (waypoint == null)
                return false;

            float dist = phantom.Position.DistanceTo(
                waypoint.Position
            );
            return dist < ARRIVAL_RANGE;
        }

        public void SpawnAirstrikeJets(
            Vector3 targetPos,
            out Vehicle jet1, out Vehicle jet2,
            out Ped pilot1, out Ped pilot2)
        {
            jet1 = null;
            jet2 = null;
            pilot1 = null;
            pilot2 = null;

            Model jetModel = new Model("lazer");
            jetModel.Request(5000);
            if (!jetModel.IsLoaded)
            {
                GTA.UI.Notification.Show(
                    "~r~AIRSTRIKE: Failed to load jet."
                );
                return;
            }

            Model pilotModel = new Model(
                "s_m_y_pilot_01"
            );
            pilotModel.Request(5000);
            if (!pilotModel.IsLoaded)
            {
                jetModel.MarkAsNoLongerNeeded();
                return;
            }

            Vector3 spawn1 = new Vector3(
                targetPos.X - 60f,
                targetPos.Y - 500f,
                targetPos.Z + 200f
            );
            Vector3 spawn2 = new Vector3(
                targetPos.X + 60f,
                targetPos.Y - 500f,
                targetPos.Z + 200f
            );

            float heading = 0f;

            jet1 = World.CreateVehicle(
                jetModel, spawn1, heading
            );
            jet2 = World.CreateVehicle(
                jetModel, spawn2, heading
            );

            if (jet1 != null && jet1.Exists())
            {
                jet1.IsEngineRunning = true;
                pilot1 = World.CreatePed(
                    pilotModel, spawn1, heading
                );
                if (pilot1 != null && pilot1.Exists())
                {
                    pilot1.SetIntoVehicle(
                        jet1, VehicleSeat.Driver
                    );
                    Function.Call(
                        Hash.SET_VEHICLE_FORWARD_SPEED,
                        jet1.Handle, 100f
                    );
                }
            }

            if (jet2 != null && jet2.Exists())
            {
                jet2.IsEngineRunning = true;
                pilot2 = World.CreatePed(
                    pilotModel, spawn2, heading
                );
                if (pilot2 != null && pilot2.Exists())
                {
                    pilot2.SetIntoVehicle(
                        jet2, VehicleSeat.Driver
                    );
                    Function.Call(
                        Hash.SET_VEHICLE_FORWARD_SPEED,
                        jet2.Handle, 100f
                    );
                }
            }

            jetModel.MarkAsNoLongerNeeded();
            pilotModel.MarkAsNoLongerNeeded();
        }

        public void SpawnAirstrikeExplosion(
            Vector3 center, int index)
        {
            float angle = index * 36f * (float)Math.PI / 180f;
            float radius = 5f + index * 2f;
            Vector3 pos = new Vector3(
                center.X + (float)Math.Cos(angle) * radius,
                center.Y + (float)Math.Sin(angle) * radius,
                center.Z
            );

            Function.Call(
                Hash.ADD_EXPLOSION,
                pos.X, pos.Y, pos.Z,
                2,
                12f,
                true,
                false,
                2f
            );
        }

        private Vehicle FindNearestVehicleForChase(Ped phantom)
        {
            Vehicle[] nearby = World.GetNearbyVehicles(
                phantom.Position, hijackRange
            );

            Vehicle closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle v = nearby[i];
                if (v == null || !v.Exists())
                    continue;
                if (phantom.IsInVehicle(v))
                    continue;

                float dist = phantom.Position.DistanceTo(
                    v.Position
                );
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = v;
                }
            }

            return closest;
        }
    }
}
