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
        private const float KAMIKAZE_ENTER_SPEED = 3.0f;
        private const float KAMIKAZE_DRIVE_SPEED = 220f;
        private const int KAMIKAZE_DRIVE_FLAGS = 2883632;
        private const int VEHICLE_MISSION_RAM = 2;
        private const int VEHICLE_MISSION_ATTACK = 6;
        private const float KAMIKAZE_STRAIGHT_LINE_DIST = 255f;
        private const float KAMIKAZE_TARGET_REACHED_DIST = 2f;
        private const float SPRINT_MOVE_BLEND = 3f;
        private const float SLOW_FOLLOW_MULTIPLIER = 0.3f;
        private const float HIJACK_DELIVERY_MULTIPLIER = 0.5f;
        private const float HIJACK_DELIVERY_RANGE = 15f;
        private const float ARRIVAL_RANGE = 15f;
        private const float STOP_RANGE = 5f;
        private const int WAYPOINT_BLIP_ID = 8;
        private const int CRUISE_DRIVE_FLAGS = 786468;
        private const float AIRSTRIKE_MAX_RADIUS = 35f;
        private const float AIRSTRIKE_MIN_RADIUS = 3f;
        private const float AIRSTRIKE_DAMAGE_SCALE = 50f;
        private const float AIRSTRIKE_CAMERA_SHAKE = 4f;
        private const int AIRSTRIKE_RANDOM_SEED = 92821;

        private readonly ScriptSettings config;
        private readonly float drivingSpeed;
        private readonly int drivingFlags;
        private readonly float hijackRange;
        private readonly float combatRadius;
        private readonly float cruiseSpeed;
        private readonly Random airstrikeRandom =
            new Random(AIRSTRIKE_RANDOM_SEED);

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

            Function.Call(
                Hash.TASK_USE_MOBILE_PHONE_TIMED,
                phantom.Handle,
                3000
            );

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

        public bool HasNearbyVehicle(Ped phantom, Ped player)
        {
            return FindNearestVehicle(phantom, player) != null;
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
                    "~o~PHANTOM: Nothing to grab around here."
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

        public static void PerformSeatTakeover(Ped phantom, Ped player, Vehicle vehicle)
        {
            if (phantom == null || !phantom.Exists() || player == null || !player.Exists() || vehicle == null || !vehicle.Exists())
                return;

            UnlockVehicleForPlayer(vehicle);

            if (player.SeatIndex == VehicleSeat.Driver)
            {
                VehicleSeat freeSeat = FindFreeSeat(vehicle);
                if (freeSeat != VehicleSeat.None)
                {
                    if (phantom.SeatIndex == freeSeat)
                    {
                        Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, phantom.Handle);
                    }
                    player.SetIntoVehicle(vehicle, freeSeat);
                    phantom.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                }
                else
                {
                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, phantom.Handle);
                    phantom.Position = vehicle.Position + vehicle.RightVector * 2.5f + Vector3.WorldUp * 0.5f;
                    player.SetIntoVehicle(vehicle, VehicleSeat.Passenger);
                    phantom.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                }
            }
            else
            {
                phantom.SetIntoVehicle(vehicle, VehicleSeat.Driver);
            }
        }

        public static void ClearStuckDynamicObstacles(Ped phantom)
        {
            if (phantom == null || !phantom.Exists() || !phantom.IsSittingInVehicle())
                return;

            Vehicle vehicle = phantom.CurrentVehicle;
            if (vehicle == null || !vehicle.Exists() || vehicle.Speed > 1.5f)
                return;

            Prop[] nearbyProps = World.GetNearbyProps(phantom.Position, 8f);
            foreach (Prop prop in nearbyProps)
            {
                if (prop == null || !prop.Exists())
                    continue;

                int hash = prop.Model.Hash;
                if (hash == Game.GenerateHash("prop_sec_barrier_01a") ||
                    hash == Game.GenerateHash("prop_sec_barrier_01b") ||
                    hash == Game.GenerateHash("prop_sec_barrier_s") ||
                    hash == Game.GenerateHash("prop_gate_airport_01") ||
                    hash == Game.GenerateHash("prop_gate_airport_02") ||
                    hash == -467554904 ||
                    hash == -459424754 ||
                    hash == 1215160868)
                {
                    prop.IsCollisionEnabled = false;
                    prop.Delete();
                }
            }
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

            PerformSeatTakeover(phantom, player, playerVehicle);

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

            Vehicle targetVehicle = GetTargetVehicle(target);
            if (targetVehicle != null && targetVehicle.Exists())
            {
                CommandEnterKamikazeVehicle(
                    phantom, null, target, false
                );
                return;
            }

            SprintTo(phantom, target.Position, target.Heading);
        }

        public void ExecuteRamExplode(
            Ped phantom, Ped player, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            phantom.Task.ClearAll();

            Vehicle targetVehicle = GetTargetVehicle(target);
            if (targetVehicle == null)
            {
                if (target is Ped targetPed)
                {
                    SprintTo(
                        phantom,
                        targetPed.Position,
                        targetPed.Heading
                    );
                    phantom.Task.FightAgainst(targetPed);
                }
                return;
            }

            if (phantom.IsInVehicle())
            {
                Vehicle curVeh = phantom.CurrentVehicle;
                if (player != null && player.Exists() && curVeh != null && curVeh.Exists() && curVeh == player.CurrentVehicle)
                {
                    phantom.Task.LeaveVehicle(curVeh, LeaveVehicleFlags.None);
                }
                else
                {

                    return;
                }
            }

            Vehicle ramVehicle = FindNearestVehicle(
                phantom, player, targetVehicle
            );

            if (ramVehicle == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: No vehicle to ram with."
                );
                return;
            }

            phantom.Task.EnterVehicle(
                ramVehicle,
                VehicleSeat.Driver,
                ENTER_VEHICLE_TIMEOUT,
                KAMIKAZE_ENTER_SPEED,
                EnterVehicleFlags.None
            );
        }

        public void CommandKamikazePassengerPrompt(
            Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (player == null || !player.Exists())
                return;
            if (!phantom.IsSittingInVehicle() || player.IsInVehicle())
                return;

            Vehicle vehicle = phantom.CurrentVehicle;
            if (vehicle == null || !vehicle.Exists())
                return;

            float dist = vehicle.Position.DistanceTo(player.Position);
            if (dist > 12f)
                return;

            VehicleSeat seat = FindFreeSeat(vehicle);
            if (seat == VehicleSeat.None)
                return;

            UnlockVehicleForPlayer(vehicle);
            GTA.UI.Screen.ShowSubtitle(
                "Press ~b~F~w~ to join PHANTOM",
                100
            );

            if (!Game.IsControlPressed(GTA.Control.Enter))
                return;

            player.SetIntoVehicle(vehicle, seat);
        }

        public void UpdateKamikazeAttack(
            Ped phantom, Ped player, Entity target, bool shouldExplode)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            Vehicle targetVehicle = GetTargetVehicle(target);
            if (targetVehicle == null)
            {
                if (target is Ped targetPed && targetPed.IsAlive)
                {
                    SprintTo(
                        phantom,
                        targetPed.Position,
                        targetPed.Heading
                    );
                    phantom.Task.FightAgainst(targetPed);
                }
                return;
            }


            bool bypassVehicleGetIn = false;
            if (targetVehicle != null && targetVehicle.Exists())
            {
                float targetDist = phantom.Position.DistanceTo(targetVehicle.Position);
                Ped driver = targetVehicle.GetPedOnSeat(VehicleSeat.Driver);
                bool noDriverOrDead = (driver == null || !driver.Exists() || !driver.IsAlive);
                if (targetDist <= 40.0f && noDriverOrDead)
                {
                    bypassVehicleGetIn = true;
                }
            }

            if (!phantom.IsSittingInVehicle())
            {
                if (bypassVehicleGetIn)
                {

                    phantom.Task.RunTo(targetVehicle.Position, true);
                    return;
                }

                if (!phantom.IsGettingIntoVehicle)
                {
                    CommandEnterKamikazeVehicle(
                        phantom, player, target, !shouldExplode
                    );
                }
                return;
            }

            Vehicle attackVehicle = phantom.CurrentVehicle;
            if (attackVehicle == null || !attackVehicle.Exists())
                return;

            if (shouldExplode)
            {
                LockVehicleForPlayer(attackVehicle);
                RemovePlayerFromVehicle(player, attackVehicle);
            }
            else
            {
                UnlockVehicleForPlayer(attackVehicle);
            }

            Function.Call(
                Hash.TASK_VEHICLE_MISSION,
                phantom.Handle,
                attackVehicle.Handle,
                targetVehicle.Handle,
                shouldExplode
                    ? VEHICLE_MISSION_RAM
                    : VEHICLE_MISSION_ATTACK,
                KAMIKAZE_DRIVE_SPEED,
                KAMIKAZE_DRIVE_FLAGS,
                KAMIKAZE_TARGET_REACHED_DIST,
                KAMIKAZE_STRAIGHT_LINE_DIST,
                true
            );

            Function.Call(
                Hash.SET_DRIVER_ABILITY,
                phantom.Handle, 1.0f
            );
            Function.Call(
                Hash.SET_DRIVER_AGGRESSIVENESS,
                phantom.Handle, 1.0f
            );
        }

        private void CommandEnterKamikazeVehicle(
            Ped phantom, Ped player, Entity target,
            bool allowPlayerPassenger)
        {
            if (phantom.IsInVehicle())
            {
                Vehicle curVeh = phantom.CurrentVehicle;
                if (player != null && player.Exists() && curVeh != null && curVeh.Exists() && curVeh == player.CurrentVehicle)
                {
                    phantom.Task.LeaveVehicle(curVeh, LeaveVehicleFlags.None);
                }
                else
                {
                    return;
                }
            }

            Vehicle targetVehicle = GetTargetVehicle(target);
            Vehicle attackVehicle = FindNearestVehicle(
                phantom, player, targetVehicle
            );

            if (attackVehicle == null)
            {
                SprintTo(phantom, target.Position, target.Heading);
                return;
            }

            UnlockVehicleForPlayer(attackVehicle);

            phantom.Task.EnterVehicle(
                attackVehicle,
                VehicleSeat.Driver,
                ENTER_VEHICLE_TIMEOUT,
                KAMIKAZE_ENTER_SPEED,
                EnterVehicleFlags.None
            );
        }

        private static Vehicle GetTargetVehicle(Entity target)
        {
            if (target is Vehicle targetVehicle)
                return targetVehicle;

            if (target is Ped targetPed && targetPed.IsSittingInVehicle())
                return targetPed.CurrentVehicle;

            return null;
        }

        public void ExecuteExecuteTarget(
            Ped phantom, Ped player, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            phantom.Task.ClearAll();

            Vehicle targetVehicle = GetTargetVehicle(target);
            if (targetVehicle != null && targetVehicle.Exists())
            {
                CommandEnterKamikazeVehicle(
                    phantom, player, target, true
                );
                return;
            }

            if (target is Ped targetPed)
            {
                SprintTo(
                    phantom,
                    targetPed.Position,
                    targetPed.Heading
                );
                phantom.Task.FightAgainst(targetPed);
            }
        }

        private static void SprintTo(
            Ped ped, Vector3 position, float heading)
        {
            Function.Call(
                Hash.TASK_GO_STRAIGHT_TO_COORD,
                ped.Handle,
                position.X,
                position.Y,
                position.Z,
                SPRINT_MOVE_BLEND,
                -1,
                heading,
                2f
            );
        }

        public void ExecuteExecuteTarget(Ped phantom, Entity target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            ExecuteExecuteTarget(phantom, null, target);
        }

        public bool TickSuicideBomb(
            Ped phantom, Ped player, Entity target)
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

            if (target is Vehicle tv && tv.IsDead)
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Target destroyed."
                );
                return true;
            }

            if (GetTargetVehicle(target) == null)
            {
                UpdateKamikazeAttack(phantom, player, target, false);
                if (target is Ped targetPed && !targetPed.IsAlive)
                {
                    GTA.UI.Notification.Show(
                        "~g~PHANTOM: Target eliminated."
                    );
                    return true;
                }
                return false;
            }

            UpdateKamikazeAttack(phantom, player, target, true);

            bool isClose = false;
            float dist = phantom.Position.DistanceTo(target.Position);
            Vehicle phVeh = phantom.CurrentVehicle;
            Vehicle tgVeh = GetTargetVehicle(target);

            if (phVeh != null && phVeh.Exists() && tgVeh != null && tgVeh.Exists())
            {
                float vehDist = phVeh.Position.DistanceTo(tgVeh.Position);
                if (vehDist <= 7.0f || Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, phVeh.Handle, tgVeh.Handle))
                {
                    isClose = true;
                }
            }
            else
            {
                if (dist <= 3.0f || Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, phantom.Handle, target.Handle))
                {
                    isClose = true;
                }
            }

            if (!isClose)
                return false;

            World.AddExplosion(
                target.Position,
                ExplosionType.Car,
                10f,
                1.0f
            );

            if (phantom.Exists())
                phantom.Health = 0;

            Vehicle phantomVehicle = phantom.CurrentVehicle;
            if (phantomVehicle != null && phantomVehicle.Exists())
                phantomVehicle.Explode();

            GTA.UI.Notification.Show(
                "~r~PHANTOM: Target eliminated. PHANTOM down."
            );
            return true;
        }

        public bool TickRamExplode(
            Ped phantom, Ped player, Entity target)
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

            if (target is Vehicle tv && tv.IsDead)
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Target destroyed."
                );
                return true;
            }

            UpdateKamikazeAttack(phantom, player, target, true);

            bool isClose = false;
            float dist = phantom.Position.DistanceTo(target.Position);
            Vehicle phVeh = phantom.CurrentVehicle;
            Vehicle tgVeh = GetTargetVehicle(target);

            if (phVeh != null && phVeh.Exists() && tgVeh != null && tgVeh.Exists())
            {
                float vehDist = phVeh.Position.DistanceTo(tgVeh.Position);
                if (vehDist <= 7.5f || Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, phVeh.Handle, tgVeh.Handle))
                {
                    isClose = true;
                }
            }
            else
            {
                if (dist <= 3.0f || Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, phantom.Handle, target.Handle))
                {
                    isClose = true;
                }
            }

            if (!isClose)
                return false;

            World.AddExplosion(
                target.Position,
                ExplosionType.Car,
                15f,
                2.0f
            );

            Vehicle phantomVehicle = phantom.CurrentVehicle;
            if (phantomVehicle != null && phantomVehicle.Exists())
                phantomVehicle.Explode();

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

            UpdateKamikazeAttack(phantom, Game.Player.Character, target, false);

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

            ClearStuckDynamicObstacles(phantom);

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

            UnlockVehicleForPlayer(hijacked);

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

        public Vehicle GetNearestReturnVehicle(Ped phantom, Ped player)
        {
            return FindNearestVehicle(phantom, player);
        }

        private Vehicle FindNearestVehicle(
            Ped phantom, Ped player, Vehicle excludedTarget = null)
        {
            Vehicle[] nearby = World.GetNearbyVehicles(
                phantom.Position, hijackRange
            );

            Vehicle playerCurrent = player?.CurrentVehicle;
            Vehicle playerLast = player?.LastVehicle;
            Vehicle phantomCurrent = phantom.CurrentVehicle;
            Vehicle phantomLast = phantom.LastVehicle;

            Vehicle closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle v = nearby[i];
                if (v == null || !v.Exists())
                    continue;
                if (v == playerCurrent || v == playerLast)
                    continue;
                if (v == phantomCurrent || v == phantomLast)
                    continue;
                if (excludedTarget != null && v == excludedTarget)
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

        public static void UnlockVehicleForPlayer(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return;

            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED,
                vehicle.Handle, 0
            );
            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED_FOR_PLAYER,
                vehicle.Handle,
                Game.Player.Handle,
                false
            );
            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED_FOR_ALL_PLAYERS,
                vehicle.Handle,
                false
            );
        }

        public static void LockVehicleForPlayer(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return;

            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED,
                vehicle.Handle, 2
            );
            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED_FOR_PLAYER,
                vehicle.Handle,
                Game.Player.Handle,
                true
            );
        }

        private static void RemovePlayerFromVehicle(
            Ped player, Vehicle vehicle)
        {
            if (player == null || !player.Exists())
                return;
            if (vehicle == null || !vehicle.Exists())
                return;
            if (!player.IsInVehicle(vehicle))
                return;

            player.Task.LeaveVehicle(LeaveVehicleFlags.BailOut);
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

        public void PrepareChaseVehicle(
            Ped phantom, Ped player, Vehicle target)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (target == null || !target.Exists())
                return;

            if (phantom.IsSittingInVehicle())
            {
                Vehicle myCar = phantom.CurrentVehicle;
                if (myCar != null && myCar.Exists())
                {
                    PerformSeatTakeover(phantom, player, myCar);
                    return;
                }
            }

            Vehicle nearestCar = FindNearestVehicleForChase(
                phantom, player, target
            );
            if (nearestCar == null)
            {
                SprintTo(phantom, target.Position, target.Heading);
                return;
            }

            UnlockVehicleForPlayer(nearestCar);
            phantom.Task.EnterVehicle(
                nearestCar,
                VehicleSeat.Driver,
                ENTER_VEHICLE_TIMEOUT,
                KAMIKAZE_ENTER_SPEED,
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

            Ped player = Game.Player.Character;

            if (!phantom.IsSittingInVehicle())
            {
                Vehicle nearestCar = FindNearestVehicleForChase(
                    phantom, player, target
                );
                if (nearestCar != null)
                {
                    UnlockVehicleForPlayer(nearestCar);
                    phantom.Task.EnterVehicle(
                        nearestCar,
                        VehicleSeat.Driver,
                        ENTER_VEHICLE_TIMEOUT,
                        KAMIKAZE_ENTER_SPEED,
                        EnterVehicleFlags.None
                    );
                }
                else
                {
                    SprintTo(phantom, target.Position, target.Heading);
                }
                return false;
            }

            Vehicle myCar = phantom.CurrentVehicle;
            if (myCar != null && myCar.Exists())
            {
                if (phantom.SeatIndex != VehicleSeat.Driver)
                {
                    PerformSeatTakeover(phantom, player, myCar);
                }
            }

            ClearStuckDynamicObstacles(phantom);

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
            float angle = (float)(airstrikeRandom.NextDouble()
                * Math.PI * 2.0);
            float radius = AIRSTRIKE_MIN_RADIUS
                + (float)airstrikeRandom.NextDouble()
                * AIRSTRIKE_MAX_RADIUS;
            Vector3 pos = new Vector3(
                center.X + (float)Math.Cos(angle) * radius,
                center.Y + (float)Math.Sin(angle) * radius,
                center.Z
            );

            float ground = World.GetGroundHeight(
                new Vector2(pos.X, pos.Y)
            );
            if (ground > 0f)
                pos.Z = ground + 1f;

            Ped player = Game.Player.Character;

            Function.Call(
                Hash.ADD_OWNED_EXPLOSION,
                player.Handle,
                pos.X, pos.Y, pos.Z,
                0,
                AIRSTRIKE_DAMAGE_SCALE,
                true,
                false,
                AIRSTRIKE_CAMERA_SHAKE
            );
        }

        private Vehicle FindNearestVehicleForChase(
            Ped phantom, Ped player = null, Vehicle excludedTarget = null)
        {
            Vehicle[] nearby = World.GetNearbyVehicles(
                phantom.Position, hijackRange
            );

            Vehicle playerVehicle = player?.CurrentVehicle;
            Vehicle playerLastVehicle = player?.LastVehicle;
            Vehicle phantomVehicle = phantom.CurrentVehicle;
            Vehicle phantomLastVehicle = phantom.LastVehicle;

            Vehicle closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle v = nearby[i];
                if (v == null || !v.Exists())
                    continue;
                if (phantom.IsInVehicle(v))
                    continue;
                if (v == playerVehicle || v == playerLastVehicle)
                    continue;
                if (v == phantomVehicle || v == phantomLastVehicle)
                    continue;
                if (excludedTarget != null && v == excludedTarget)
                    continue;
                if (!v.IsSeatFree(VehicleSeat.Driver))
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
