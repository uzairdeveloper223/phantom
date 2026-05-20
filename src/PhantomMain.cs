using System;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace PHANTOM
{
    public sealed class PhantomMain : Script
    {
        private const int COMBAT_TICK_INTERVAL = 3000;
        private const int DRIVE_TICK_INTERVAL = 1000;
        private const int HIJACK_TICK_INTERVAL = 1000;
        private const int KAMIKAZE_TICK_INTERVAL = 500;
        private const int EXECUTE_TICK_INTERVAL = 1000;
        private const int BACKUP_TICK_INTERVAL = 3000;
        private const int HIJACK_INITIAL_DELAY = 5000;
        private const int DRIVE_INITIAL_DELAY = 3000;
        private const int AUTO_BACKUP_COOLDOWN = 60000;
        private const int AUTO_BACKUP_HEALTH_THRESHOLD = 150;
        private const int AUTO_BACKUP_STAR_THRESHOLD = 5;
        private const int AUTO_BACKUP_UNITS = 4;
        private const int AUTO_BACKUP_CHOPPERS = 2;

        private readonly PhantomPed phantomPed;
        private readonly PhantomMenu phantomMenu;
        private readonly PhantomTasks phantomTasks;
        private readonly PhantomBackup phantomBackup;
        private readonly Keys menuToggleKey;

        private const int CHASE_TICK_INTERVAL = 2000;
        private const int CRUISE_TICK_INTERVAL = 2000;
        private const int AIRSTRIKE_EXPLOSION_INTERVAL = 400;
        private const int AIRSTRIKE_EXPLOSION_COUNT = 10;
        private const int AIRSTRIKE_INITIAL_DELAY = 5000;
        private const float CHASE_ESCAPE_DISTANCE = 450f;
        private const float CHASE_BOARDING_RANGE = 12f;

        private PhantomState currentState = PhantomState.Inactive;
        public static Entity kamikazeTarget;
        private Entity activeAimTarget = null;
        private int lastReturnDriveTime = 0;
        private Vehicle chaseTarget;
        private Vehicle chaseVehicle;
        private bool hasChaseStarted;
        private GTA.Math.Vector3 airstrikePosition;
        private Vehicle airstrikeJet1;
        private Vehicle airstrikeJet2;
        private Ped airstrikePilot1;
        private Ped airstrikePilot2;
        private int airstrikeExplosionIndex;
        private int healthRegenTimer;
        private readonly int healthRegenDelay;
        private readonly bool shouldRegenerateHealth;
        private int tickCooldown;
        private int backupTickCooldown;
        private int autoBackupCooldown;
        private Vector3 waypointDropDest;

        public PhantomMain()
        {
            string configPath = Path.Combine(
                Path.GetDirectoryName(
                    typeof(PhantomMain).Assembly.Location
                ) ?? "",
                "PHANTOM.ini"
            );

            ScriptSettings config = File.Exists(configPath)
                ? ScriptSettings.Load(configPath)
                : ScriptSettings.Load("scripts\\PHANTOM.ini");

            string keyName = config.GetValue(
                "General", "MenuKey", "F10"
            );

            if (!Enum.TryParse(keyName, true, out menuToggleKey))
                menuToggleKey = Keys.F10;

            healthRegenDelay = config.GetValue(
                "Health", "RegenerateDelay", 30000
            );
            shouldRegenerateHealth = config.GetValue(
                "Health", "RegenerateHealth", true
            );

            phantomPed = new PhantomPed(config);
            phantomTasks = new PhantomTasks(config);
            phantomMenu = new PhantomMenu();
            phantomBackup = new PhantomBackup(config);

            phantomMenu.OnSummon += HandleSummon;
            phantomMenu.OnDismiss += HandleDismiss;
            phantomMenu.OnFollow += HandleFollow;
            phantomMenu.OnWait += HandleWait;
            phantomMenu.OnCombat += HandleCombat;
            phantomMenu.OnVehicleHijack += HandleVehicleHijack;
            phantomMenu.OnDriveTo += HandleDriveTo;
            phantomMenu.OnKamikazeStart += HandleKamikazeStart;
            phantomMenu.OnSuicideBomb += HandleSuicideBomb;
            phantomMenu.OnRamExplode += HandleRamExplode;
            phantomMenu.OnExecuteTarget += HandleExecuteTarget;
            phantomMenu.OnCancelKamikaze += HandleCancelTargeting;
            phantomMenu.OnDeployBackup += HandleDeployBackup;
            phantomMenu.OnDismissBackup += HandleDismissBackup;
            phantomMenu.OnAirSupport += HandleAirSupport;
            phantomMenu.OnExtraction += HandleExtraction;
            phantomMenu.OnDropAtWaypoint += HandleDropAtWaypoint;
            phantomMenu.OnChase += HandleChaseStart;
            phantomMenu.OnCruise += HandleCruise;
            phantomMenu.OnAirstrike += HandleAirstrikeStart;

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;

            Interval = 0;
        }

        private void OnTick(object sender, EventArgs e)
        {
            phantomMenu.Process();

            Ped player = Game.Player.Character;
            Ped phantom = phantomPed.Ped;

            bool isKamikazeActive = currentState == PhantomState.KamikazeSuicide
                || currentState == PhantomState.KamikazeRam
                || currentState == PhantomState.KamikazeExecute
                || currentState == PhantomState.KamikazeTargeting;


            float distFromPlayer = -1f;
            float distFromPhantom = -1f;
            if (player != null && player.Exists() && phantomPed.IsActive)
            {
                if (phantom != null && phantom.Exists())
                {
                    distFromPlayer = player.Position.DistanceTo(phantom.Position);
                    if (kamikazeTarget != null && kamikazeTarget.Exists())
                    {
                        distFromPhantom = phantom.Position.DistanceTo(kamikazeTarget.Position);
                    }
                }
            }

            phantomMenu.UpdateItemStates(phantomPed.IsActive, isKamikazeActive, distFromPlayer, distFromPhantom);

            phantomPed.CleanupDeadBlip();


            if (currentState == PhantomState.KamikazeSuicide ||
                currentState == PhantomState.KamikazeRam ||
                currentState == PhantomState.KamikazeExecute)
            {
                if (kamikazeTarget != null && kamikazeTarget.Exists())
                {
                    DrawTacticalHUD(kamikazeTarget, true);
                }
            }

            if (!phantomPed.IsActive)
            {
                if (currentState != PhantomState.Inactive)
                {
                    kamikazeTarget = null;
                    TransitionTo(PhantomState.Inactive);
                }
                return;
            }

            switch (currentState)
            {
                case PhantomState.Following:
                    TickFollowing(phantom);
                    break;
                case PhantomState.Combat:
                    TickCombat(phantom, player);
                    break;
                case PhantomState.DriveTo:
                    TickDriveTo(phantom, player);
                    break;
                case PhantomState.VehicleHijack:
                    TickVehicleHijack(phantom, player);
                    break;
                case PhantomState.KamikazeTargeting:
                    TickKamikazeTargeting(player);
                    break;
                case PhantomState.KamikazeSuicide:
                    TickKamikazeSuicide(phantom, player);
                    break;
                case PhantomState.KamikazeRam:
                    TickKamikazeRam(phantom, player);
                    break;
                case PhantomState.KamikazeExecute:
                    TickKamikazeExecute(phantom);
                    break;
                case PhantomState.ChaseTargeting:
                    TickChaseTargeting(player);
                    break;
                case PhantomState.ChaseActive:
                    TickChaseActive(phantom);
                    break;
                case PhantomState.Cruise:
                    TickCruiseState(phantom, player);
                    break;
                case PhantomState.AirstrikeActive:
                    TickAirstrikeActive();
                    break;
                case PhantomState.PickupInbound:
                    TickPickupInbound(phantom, player);
                    break;
                case PhantomState.PickupBoarding:
                    TickPickupBoarding(phantom, player);
                    break;
                case PhantomState.PickupFlying:
                    TickPickupFlying(phantom, player);
                    break;
                case PhantomState.PickupDropping:
                    TickPickupDropping(phantom, player);
                    break;
            }

            TickBackup(phantom, player);
            TickAutoBackup(phantom, player);
            TickParachuteSync(phantom, player);
            phantomBackup.TickBackupParachutes(player);
            UpdateHealthRegeneration();
        }

        private void TickFollowing(Ped phantom)
        {
            Ped player = Game.Player.Character;
            if (phantom == null || !phantom.Exists() || player == null || !player.Exists())
                return;

            if (UpdateFollowingReturn(phantom, player))
            {
                return;
            }

            if (phantom.IsSittingInVehicle())
            {
                Vehicle vehicle = phantom.CurrentVehicle;
                if (vehicle != null && vehicle.Exists()
                    && player.IsInVehicle(vehicle))
                {
                    if (phantom.SeatIndex == VehicleSeat.Driver)
                    {
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, vehicle.Handle, true);
                        Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, vehicle.Handle, 0f);
                        phantom.Task.ClearAll();
                        return;
                    }
                }
            }

            if (!phantom.IsInGroup)
                phantomPed.RejoinGroup();
        }

        private bool UpdateFollowingReturn(Ped phantom, Ped player)
        {
            float dist = phantom.Position.DistanceTo(player.Position);


            if (player.IsInVehicle() && !phantom.IsInVehicle())
            {
                Vehicle playerVeh = player.CurrentVehicle;
                if (playerVeh != null && playerVeh.Exists())
                {
                    if (!phantom.IsGettingIntoVehicle)
                    {
                        VehicleSeat freeSeat = PhantomTasks.FindFreeSeat(playerVeh);
                        if (freeSeat != VehicleSeat.None)
                        {
                            phantomPed.LeaveGroupSafe();
                            phantom.Task.EnterVehicle(
                                playerVeh,
                                freeSeat,
                                -1,
                                2.0f,
                                EnterVehicleFlags.None
                            );
                        }
                    }
                    return true;
                }
            }


            if (phantom.IsSittingInVehicle())
            {
                Vehicle vehicle = phantom.CurrentVehicle;
                if (vehicle != null && vehicle.Exists())
                {
                    if (dist > 15f)
                    {

                        phantomPed.LeaveGroupSafe();


                        if (Game.GameTime > lastReturnDriveTime + 2000)
                        {
                            lastReturnDriveTime = Game.GameTime;


                            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, vehicle.Handle, false);


                            phantom.Task.DriveTo(
                                vehicle,
                                player.Position,
                                10f,
                                40f,
                                DrivingStyle.AvoidTrafficExtremely
                            );
                        }
                        return true;
                    }
                    else
                    {


                        if (!player.IsInVehicle())
                        {
                            phantom.Task.LeaveVehicle(vehicle, LeaveVehicleFlags.None);
                            phantomPed.RejoinGroup();
                            return true;
                        }
                        else
                        {

                            phantomPed.RejoinGroup();
                        }
                    }
                }
            }


            if (phantom.IsGettingIntoVehicle)
            {
                phantomPed.LeaveGroupSafe();
                return true;
            }


            if (dist > 45f && !phantom.IsSittingInVehicle())
            {
                Vehicle returnVehicle = phantomTasks.GetNearestReturnVehicle(phantom, player);
                if (returnVehicle != null && returnVehicle.Exists())
                {
                    phantomPed.LeaveGroupSafe();
                    phantom.Task.EnterVehicle(
                        returnVehicle,
                        VehicleSeat.Driver,
                        10000,
                        3.0f,
                        EnterVehicleFlags.None
                    );
                    return true;
                }
            }

            return false;
        }

        private void TickCombat(Ped phantom, Ped player)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + COMBAT_TICK_INTERVAL;

            if (!phantom.IsInGroup)
                phantomPed.RejoinGroup();

            phantomTasks.TickCombat(phantom, player);
        }

        private void TickDriveTo(Ped phantom, Ped player)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + DRIVE_TICK_INTERVAL;

            if (!player.IsSittingInVehicle())
            {
                phantom.Task.ClearAll();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                GTA.UI.Notification.Show(
                    "~y~PHANTOM: Player exited. Returning."
                );
                return;
            }

            Blip waypoint = PhantomTasks.GetWaypointBlip();
            if (waypoint == null)
            {
                StopDrivingAndFollow(phantom);
                GTA.UI.Notification.Show(
                    "~y~PHANTOM: No waypoint. Holding."
                );
                return;
            }

            phantomTasks.TickDriveTo(phantom, player);

            float dist = phantom.Position.DistanceTo(
                waypoint.Position
            );

            if (dist < 15f)
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Arrived."
                );
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void StopDrivingAndFollow(Ped phantom)
        {
            if (phantom == null || !phantom.Exists())
                return;

            Vehicle vehicle = phantom.CurrentVehicle;
            phantom.Task.ClearAll();
            if (vehicle != null && vehicle.Exists())
            {
                Function.Call(
                    Hash.SET_VEHICLE_FORWARD_SPEED,
                    vehicle.Handle,
                    0f
                );
            }

            phantomPed.RejoinGroup();
            TransitionTo(PhantomState.Following);
        }

        private void TickVehicleHijack(Ped phantom, Ped player)
        {
            if (phantom.IsSittingInVehicle()
                && !player.IsInVehicle())
            {
                Vehicle hijacked = phantom.CurrentVehicle;
                if (hijacked != null && hijacked.Exists())
                {
                    float dist = hijacked.Position
                        .DistanceTo(player.Position);

                    if (dist < 10f && hijacked.Speed < 3f)
                    {
                        GTA.UI.Screen.ShowSubtitle(
                            "Press ~b~F~w~ to enter vehicle",
                            100
                        );

                        if (Game.IsControlPressed(
                            GTA.Control.Enter))
                        {
                            VehicleSeat seat =
                                PhantomTasks.FindFreeSeat(
                                    hijacked);
                            if (seat != VehicleSeat.None)
                            {
                                player.SetIntoVehicle(
                                    hijacked, seat
                                );
                                GTA.UI.Notification.Show(
                                    "~g~PHANTOM: Get in!"
                                );
                            }
                        }
                    }
                }
            }

            if (!phantom.IsSittingInVehicle()
                && !phantom.IsGettingIntoVehicle
                && Game.GameTime > tickCooldown + 15000)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Lost it. Heading back."
                );
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + HIJACK_TICK_INTERVAL;

            phantomTasks.TickVehicleHijack(phantom, player);
        }

        private void TickKamikazeTargeting(Ped player)
        {
            if (kamikazeTarget != null && kamikazeTarget.Exists())
            {

                DrawTacticalHUD(kamikazeTarget, true);
                return;
            }


            if (player.IsAiming)
            {
                activeAimTarget = FindAimTarget(player);
                if (activeAimTarget != null && activeAimTarget.Exists())
                {

                    DrawTacticalHUD(activeAimTarget, false);


                    if (Game.IsKeyPressed(Keys.T))
                    {

                        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);


                        kamikazeTarget = activeAimTarget;
                        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, kamikazeTarget.Handle, true, true);
                        if (kamikazeTarget is Ped targetPed && targetPed.IsInVehicle())
                        {
                            Vehicle targetVeh = targetPed.CurrentVehicle;
                            if (targetVeh != null && targetVeh.Exists())
                            {
                                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, targetVeh.Handle, true, true);
                            }
                        }


                        Blip targetBlip = kamikazeTarget.AttachedBlip;
                        if (targetBlip == null || !targetBlip.Exists())
                        {
                            targetBlip = kamikazeTarget.AddBlip();
                            targetBlip.Color = BlipColor.Red;
                            targetBlip.Name = "PHANTOM TARGET";
                            targetBlip.Scale = 0.75f;
                        }

                        GTA.UI.Notification.Show("~r~PHANTOM: Target locked! Pick attack.");


                        phantomMenu.ShowKamikazeOptions(
                            kamikazeTarget is Vehicle || (kamikazeTarget is Ped tp && tp.IsInVehicle())
                        );
                    }
                }
                else
                {

                    DrawScanningIndicator("SCANNING FOR TARGET...");
                }
            }
            else
            {

                DrawScanningIndicator("HOLD AIM (RCLICK) & AIM AT TARGET");
            }
        }

        private Entity FindDamagedEntity(Ped player)
        {

            Entity aimed = Game.Player.TargetedEntity;
            if (aimed != null && aimed.Exists())
            {
                if (aimed is Ped ped && ped.IsAlive && ped != player && ped != phantomPed.Ped)
                {
                    if (ped.IsInVehicle())
                    {
                        Vehicle v = ped.CurrentVehicle;
                        if (v != null && v.Exists() && !v.IsDead)
                        {
                            return ped;
                        }
                    }
                    return ped;
                }
                if (aimed is Vehicle veh && !veh.IsDead)
                {
                    for (int seatIndex = -1; seatIndex < veh.PassengerCapacity; seatIndex++)
                    {
                        Ped occupant = veh.GetPedOnSeat((VehicleSeat)seatIndex);
                        if (occupant != null && occupant.Exists() && occupant.IsAlive && occupant != player && occupant != phantomPed.Ped)
                        {
                            return occupant;
                        }
                    }
                    return veh;
                }
            }


            RaycastResult ray = World.Raycast(
                GameplayCamera.Position,
                GameplayCamera.Direction,
                150f,
                IntersectFlags.Everything,
                player
            );
            if (ray.DidHit && ray.HitEntity != null && ray.HitEntity.Exists())
            {
                Entity hit = ray.HitEntity;
                if (hit is Ped ped && ped.IsAlive && ped != player && ped != phantomPed.Ped)
                {
                    if (ped.IsInVehicle())
                    {
                        Vehicle v = ped.CurrentVehicle;
                        if (v != null && v.Exists() && !v.IsDead)
                        {
                            return ped;
                        }
                    }
                    return ped;
                }
                if (hit is Vehicle veh && !veh.IsDead)
                {
                    for (int seatIndex = -1; seatIndex < veh.PassengerCapacity; seatIndex++)
                    {
                        Ped occupant = veh.GetPedOnSeat((VehicleSeat)seatIndex);
                        if (occupant != null && occupant.Exists() && occupant.IsAlive && occupant != player && occupant != phantomPed.Ped)
                        {
                            return occupant;
                        }
                    }
                    return veh;
                }
            }


            Ped[] nearbyPeds = World.GetNearbyPeds(
                player.Position, 100f
            );

            for (int i = 0; i < nearbyPeds.Length; i++)
            {
                Ped ped = nearbyPeds[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive)
                    continue;
                if (ped == player || ped == phantomPed.Ped)
                    continue;
                if (ped.HasBeenDamagedBy(player))
                    return ped;
            }

            Vehicle[] nearbyVehicles = World.GetNearbyVehicles(
                player.Position, 100f
            );

            for (int i = 0; i < nearbyVehicles.Length; i++)
            {
                Vehicle v = nearbyVehicles[i];
                if (v == null || !v.Exists() || v.IsDead)
                    continue;
                if (v.HasBeenDamagedBy(player))
                {

                    for (int seatIndex = -1; seatIndex < v.PassengerCapacity; seatIndex++)
                    {
                        Ped occupant = v.GetPedOnSeat((VehicleSeat)seatIndex);
                        if (occupant != null && occupant.Exists() && occupant.IsAlive && occupant != player && occupant != phantomPed.Ped)
                        {
                            return occupant;
                        }
                    }

                    return v;
                }
            }

            return null;
        }

        private void TickKamikazeSuicide(Ped phantom, Ped player)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + KAMIKAZE_TICK_INTERVAL;

            if (phantomTasks.TickSuicideBomb(
                phantom, player, kamikazeTarget))
            {
                CleanupKamikazeTarget();
                TransitionTo(PhantomState.Inactive);
            }
        }

        private void TickKamikazeRam(Ped phantom, Ped player)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + KAMIKAZE_TICK_INTERVAL;

            phantomTasks.CommandKamikazePassengerPrompt(
                phantom, player
            );

            if (phantomTasks.TickRamExplode(
                phantom, player, kamikazeTarget))
            {
                CleanupKamikazeTarget();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void TickKamikazeExecute(Ped phantom)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + EXECUTE_TICK_INTERVAL;

            phantomTasks.CommandKamikazePassengerPrompt(
                phantom, Game.Player.Character
            );

            if (phantomTasks.TickExecuteTarget(
                phantom, kamikazeTarget))
            {
                CleanupKamikazeTarget();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void TickBackup(Ped phantom, Ped player)
        {
            if (Game.GameTime < backupTickCooldown)
                return;
            backupTickCooldown =
                Game.GameTime + BACKUP_TICK_INTERVAL;

            phantomBackup.Tick(player, phantom);
        }

        private void TickAutoBackup(Ped phantom, Ped player)
        {
            if (Game.GameTime < autoBackupCooldown)
                return;

            int stars = Game.Player.WantedLevel;
            if (stars < 1)
                return;

            float combinedHealth =
                (float)(phantom.Health + player.Health);
            float combinedMax =
                (float)(phantom.MaxHealth + player.MaxHealth);
            float healthRatio = combinedHealth / combinedMax;

            int unitsToSpawn = 0;
            int choppersToSpawn = 0;

            if (stars >= 5)
            {
                unitsToSpawn = AUTO_BACKUP_UNITS;
                choppersToSpawn = AUTO_BACKUP_CHOPPERS;
            }
            else if (stars == 4 && healthRatio < 0.6f)
            {
                unitsToSpawn = 3;
                choppersToSpawn = 1;
            }
            else if (stars == 3 && healthRatio < 0.5f)
            {
                unitsToSpawn = 2;
                choppersToSpawn = 0;
            }
            else if (stars == 2 && healthRatio < 0.35f)
            {
                unitsToSpawn = 1;
                choppersToSpawn = 0;
            }
            else if (stars == 1 && healthRatio < 0.2f)
            {
                unitsToSpawn = 1;
                choppersToSpawn = 0;
            }

            if (unitsToSpawn == 0 && choppersToSpawn == 0)
                return;

            int existing = phantomBackup.ActiveUnitCount;
            int groundNeeded = unitsToSpawn - existing;

            if (groundNeeded <= 0 && choppersToSpawn == 0)
                return;

            autoBackupCooldown =
                Game.GameTime + AUTO_BACKUP_COOLDOWN;

            GTA.UI.Notification.Show(
                "~r~PHANTOM: Heavy contact! Calling backup!"
            );

            if (groundNeeded > 0)
            {
                phantomBackup.SpawnGroundUnits(
                    player, groundNeeded, true
                );
            }

            if (choppersToSpawn > 0)
            {
                phantomBackup.SpawnAirSupport(
                    player, choppersToSpawn
                );
            }
        }

        private void CleanupKamikazeTarget()
        {
            if (kamikazeTarget != null && kamikazeTarget.Exists())
            {
                Blip b = kamikazeTarget.AttachedBlip;
                if (b != null && b.Exists())
                    b.Delete();

                kamikazeTarget.MarkAsNoLongerNeeded();
                if (kamikazeTarget is Ped targetPed && targetPed.IsInVehicle())
                {
                    Vehicle targetVeh = targetPed.CurrentVehicle;
                    if (targetVeh != null && targetVeh.Exists())
                    {
                        targetVeh.MarkAsNoLongerNeeded();
                    }
                }
            }
            kamikazeTarget = null;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != menuToggleKey)
                return;

            if (currentState == PhantomState.KamikazeTargeting
                || currentState == PhantomState.ChaseTargeting)
            {
                HandleCancelTargeting();
                return;
            }

            phantomMenu.Toggle();
        }

        private void OnAborted(object sender, EventArgs e)
        {
            CleanupKamikazeTarget();
            CleanupAirstrike();
            phantomBackup.Dispose();
            phantomPed.Dispose();
        }

        private void TransitionTo(PhantomState newState)
        {
            currentState = newState;

            string label = currentState switch
            {
                PhantomState.Inactive => "~r~OFFLINE",
                PhantomState.Idle => "~w~IDLE",
                PhantomState.Following => "~g~FOLLOWING",
                PhantomState.Waiting => "~y~HOLDING POSITION",
                PhantomState.Combat => "~r~COMBAT MODE",
                PhantomState.VehicleHijack => "~o~HIJACKING",
                PhantomState.DriveTo => "~b~DRIVING",
                PhantomState.KamikazeTargeting
                    => "~o~MARKING TARGET...",
                PhantomState.KamikazeSuicide
                    => "~r~SUICIDE BOMB",
                PhantomState.KamikazeRam
                    => "~r~RAM & EXPLODE",
                PhantomState.KamikazeExecute
                    => "~r~EXECUTING",
                PhantomState.AirSupport => "~b~AIR SUPPORT",
                PhantomState.ChaseTargeting
                    => "~o~MARKING CHASE TARGET...",
                PhantomState.ChaseActive => "~o~CHASING",
                PhantomState.Cruise => "~b~CRUISING",
                PhantomState.AirstrikeActive
                    => "~r~AIRSTRIKE INBOUND",
                PhantomState.PickupInbound
                    => "~g~EXTRACTION INBOUND",
                PhantomState.PickupBoarding
                    => "~g~BOARD THE HELI",
                PhantomState.PickupFlying
                    => "~g~AIRBORNE",
                PhantomState.PickupDropping
                    => "~y~DROPPING AT WAYPOINT",
                _ => "~w~UNKNOWN"
            };

            GTA.UI.Notification.Show(
                $"~s~PHANTOM Protocol: {label}"
            );
        }

        private void HandleSummon()
        {
            if (phantomPed.IsActive)
                return;

            if (phantomPed.Spawn())
            {
                TransitionTo(PhantomState.Following);
                healthRegenTimer = Game.GameTime;
            }
        }

        private void HandleDismiss()
        {
            if (!phantomPed.IsActive)
                return;

            GTA.UI.Notification.Show(
                "~o~PHANTOM: Copy. Going dark."
            );
            CleanupKamikazeTarget();
            phantomBackup.DismissAll();
            phantomPed.Dismiss();
            TransitionTo(PhantomState.Inactive);
        }

        private void HandleFollow()
        {
            if (!phantomPed.IsActive)
                return;

            GTA.UI.Notification.Show(
                "~o~PHANTOM: Right behind you."
            );
            phantomPed.Ped.Task.ClearAll();
            phantomPed.RejoinGroup();
            TransitionTo(PhantomState.Following);
        }

        private void HandleWait()
        {
            if (!phantomPed.IsActive)
                return;

            GTA.UI.Notification.Show(
                "~o~PHANTOM: Holding position."
            );
            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteWait(phantomPed.Ped);
            TransitionTo(PhantomState.Waiting);
        }

        private void HandleCombat()
        {
            if (!phantomPed.IsActive)
                return;

            GTA.UI.Notification.Show(
                "~o~PHANTOM: Weapons free."
            );
            phantomPed.RejoinGroup();
            phantomPed.Ped.BlockPermanentEvents = false;
            phantomTasks.ExecuteCombat(phantomPed.Ped);
            tickCooldown = Game.GameTime + COMBAT_TICK_INTERVAL;
            TransitionTo(PhantomState.Combat);
        }

        private void HandleVehicleHijack()
        {
            if (!phantomPed.IsActive)
                return;

            Ped player = Game.Player.Character;
            Ped phantom = phantomPed.Ped;

            if (phantomTasks.HasNearbyVehicle(
                phantom, player))
            {
                phantomPed.LeaveGroupSafe();
                phantomTasks.ExecuteVehicleHijack(
                    phantom, player
                );
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: On it. Getting you a ride."
                );
                tickCooldown =
                    Game.GameTime + HIJACK_INITIAL_DELAY;
                TransitionTo(PhantomState.VehicleHijack);
            }
            else
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Nothing to grab around here."
                );
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void HandleDriveTo()
        {
            if (!phantomPed.IsActive)
                return;

            Ped player = Game.Player.Character;

            if (!player.IsSittingInVehicle())
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Get in a vehicle first."
                );
                return;
            }

            if (PhantomTasks.GetWaypointBlip() == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Set a waypoint first."
                );
                return;
            }

            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteDriveTo(
                phantomPed.Ped, player
            );
            tickCooldown = Game.GameTime + DRIVE_INITIAL_DELAY;
            TransitionTo(PhantomState.DriveTo);
        }

        private void HandleKamikazeStart()
        {
            if (!phantomPed.IsActive)
                return;

            if (currentState == PhantomState.KamikazeSuicide
                || currentState == PhantomState.KamikazeRam
                || currentState == PhantomState.KamikazeExecute
                || currentState == PhantomState.KamikazeTargeting)
            {
                HandleCancelTargeting();
                return;
            }

            if (currentState == PhantomState.ChaseActive
                && chaseTarget != null
                && chaseTarget.Exists()
                && !chaseTarget.IsDead)
            {
                kamikazeTarget = chaseTarget;
                phantomMenu.HideAll();
                phantomMenu.ShowKamikazeOptions(true);
                GTA.UI.Notification.Show(
                    "~r~PHANTOM: Choose chase attack."
                );
                return;
            }

            kamikazeTarget = null;
            phantomMenu.HideAll();
            TransitionTo(PhantomState.KamikazeTargeting);
            GTA.UI.Notification.Show(
                "~o~>> Shoot a target to mark it. Press "
                + menuToggleKey.ToString()
                + " to cancel."
            );
        }

        private void HandleSuicideBomb()
        {
            if (!phantomPed.IsActive
                || kamikazeTarget == null)
                return;

            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteSuicideBomb(
                phantomPed.Ped, kamikazeTarget
            );
            CommandBackupAttack(
                shouldExplode: true,
                useRamMission: true,
                useAllUnits: false
            );
            ClearChaseStateOnly();
            tickCooldown = Game.GameTime + KAMIKAZE_TICK_INTERVAL;
            TransitionTo(PhantomState.KamikazeSuicide);
        }

        private void HandleRamExplode()
        {
            if (!phantomPed.IsActive
                || kamikazeTarget == null)
                return;

            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteRamExplode(
                phantomPed.Ped,
                Game.Player.Character,
                kamikazeTarget
            );
            CommandBackupAttack(
                shouldExplode: false,
                useRamMission: true,
                useAllUnits: true
            );
            ClearChaseStateOnly();
            tickCooldown = Game.GameTime + KAMIKAZE_TICK_INTERVAL;
            TransitionTo(PhantomState.KamikazeRam);
        }

        private void HandleExecuteTarget()
        {
            if (!phantomPed.IsActive
                || kamikazeTarget == null)
                return;

            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteExecuteTarget(
                phantomPed.Ped,
                Game.Player.Character,
                kamikazeTarget
            );
            CommandBackupAttack(
                shouldExplode: false,
                useRamMission: false,
                useAllUnits: true
            );
            ClearChaseStateOnly();
            tickCooldown = Game.GameTime + EXECUTE_TICK_INTERVAL;
            TransitionTo(PhantomState.KamikazeExecute);
        }

        private void CommandBackupAttack(
            bool shouldExplode, bool useRamMission,
            bool useAllUnits)
        {
            Vehicle vehicle = null;
            if (kamikazeTarget is Vehicle v)
            {
                vehicle = v;
            }
            else if (kamikazeTarget is Ped p && p.IsInVehicle())
            {
                vehicle = p.CurrentVehicle;
            }

            if (vehicle == null)
                return;

            phantomBackup.AssistChaseTarget(
                vehicle,
                shouldExplode,
                useRamMission,
                useAllUnits
            );
        }

        private void ClearChaseStateOnly()
        {
            if (kamikazeTarget != chaseTarget)
                return;

            chaseTarget = null;
            chaseVehicle = null;
            hasChaseStarted = false;
        }

        private void HandleDeployBackup(
            int unitCount, bool inVehicle)
        {
            if (!phantomPed.IsActive)
                return;

            if (phantomBackup.ActiveUnitCount
                >= AUTO_BACKUP_UNITS)
            {
                GTA.UI.Notification.Show(
                    "~y~PHANTOM: Enough backup already."
                );
                return;
            }

            int remaining = AUTO_BACKUP_UNITS
                - phantomBackup.ActiveUnitCount;
            int toSpawn = Math.Min(unitCount, remaining);

            phantomTasks.PlayRadioCallAnimation(
                phantomPed.Ped
            );

            phantomBackup.SpawnGroundUnits(
                Game.Player.Character, toSpawn, inVehicle
            );
        }

        private void HandleDismissBackup()
        {
            if (!phantomBackup.HasActiveBackup)
            {
                GTA.UI.Notification.Show(
                    "~y~PHANTOM: No backup to dismiss."
                );
                return;
            }

            phantomBackup.DismissAll();
        }

        private void HandleAirSupport()
        {
            if (!phantomPed.IsActive)
                return;

            phantomTasks.PlayRadioCallAnimation(
                phantomPed.Ped
            );

            phantomBackup.SpawnAirSupport(
                Game.Player.Character, 2
            );
        }

        private void UpdateHealthRegeneration()
        {
            if (!shouldRegenerateHealth
                || !phantomPed.IsActive)
                return;

            Ped ped = phantomPed.Ped;

            if (ped.Health >= ped.MaxHealth)
            {
                healthRegenTimer = Game.GameTime;
                return;
            }

            if (Game.GameTime - healthRegenTimer
                < healthRegenDelay)
                return;

            ped.Health = Math.Min(
                ped.Health + 1, ped.MaxHealth
            );
        }

        private void TickChaseTargeting(Ped player)
        {
            if (!player.IsShooting)
                return;

            Vehicle[] nearby = World.GetNearbyVehicles(
                player.Position, 100f
            );

            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle v = nearby[i];
                if (v == null || !v.Exists())
                    continue;
                if (v.HasBeenDamagedBy(player))
                {
                    chaseTarget = v;
                    chaseVehicle = null;
                    hasChaseStarted = false;
                    Blip b = v.AddBlip();
                    b.Color = BlipColor.Red;
                    b.Name = "CHASE TARGET";
                    b.Scale = 0.8f;

                    phantomPed.LeaveGroupSafe();
                    phantomTasks.PrepareChaseVehicle(
                        phantomPed.Ped, player, v
                    );
                    tickCooldown =
                        Game.GameTime + CHASE_TICK_INTERVAL;
                    TransitionTo(PhantomState.ChaseActive);
                    GTA.UI.Notification.Show(
                        "~o~PHANTOM: Boarding chase vehicle."
                    );
                    return;
                }
            }
        }

        private void TickChaseActive(Ped phantom)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + CHASE_TICK_INTERVAL;

            Ped player = Game.Player.Character;

            if (chaseTarget == null
                || !chaseTarget.Exists()
                || chaseTarget.IsDead)
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Target destroyed."
                );
                CleanupChaseTarget();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (!hasChaseStarted && HasChaseTargetEscaped(player))
            {
                GTA.UI.Notification.Show(
                    "~y~PHANTOM: Chase target escaped."
                );
                CleanupChaseTarget();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (!hasChaseStarted)
            {
                TickChaseBoarding(phantom, player);
                return;
            }

            if (chaseVehicle == null
                || !chaseVehicle.Exists()
                || !player.IsInVehicle(chaseVehicle))
            {
                GTA.UI.Notification.Show(
                    "~y~PHANTOM: Waiting for player in chase car."
                );
                return;
            }

            phantomBackup.SupportChase(chaseTarget, player);

            if (phantomTasks.TickChase(phantom, chaseTarget))
            {
                CleanupChaseTarget();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void TickChaseBoarding(Ped phantom, Ped player)
        {
            if (!phantom.IsSittingInVehicle())
            {
                phantomTasks.PrepareChaseVehicle(
                    phantom, player, chaseTarget
                );
                GTA.UI.Screen.ShowSubtitle(
                    "PHANTOM is acquiring a chase vehicle",
                    100
                );
                return;
            }

            chaseVehicle = phantom.CurrentVehicle;
            if (chaseVehicle == null || !chaseVehicle.Exists())
                return;

            PhantomTasks.UnlockVehicleForPlayer(chaseVehicle);

            if (!player.IsInVehicle(chaseVehicle))
            {
                float dist = player.Position.DistanceTo(
                    chaseVehicle.Position
                );
                if (dist <= CHASE_BOARDING_RANGE)
                {
                    GTA.UI.Screen.ShowSubtitle(
                        "Press ~b~F~w~ to start chase",
                        100
                    );

                    if (Game.IsControlPressed(GTA.Control.Enter))
                    {
                        VehicleSeat seat =
                            PhantomTasks.FindFreeSeat(chaseVehicle);
                        if (seat != VehicleSeat.None)
                            player.SetIntoVehicle(chaseVehicle, seat);
                    }
                }
                return;
            }

            hasChaseStarted = true;
            phantomBackup.SupportChase(chaseTarget, player);
            phantomTasks.TickChase(phantom, chaseTarget);
            GTA.UI.Notification.Show(
                "~r~PHANTOM: Chase engaged."
            );
        }

        private bool HasChaseTargetEscaped(Ped player)
        {
            if (chaseTarget == null || !chaseTarget.Exists())
                return true;

            float playerDist = chaseTarget.Position.DistanceTo(
                player.Position
            );
            if (playerDist <= CHASE_ESCAPE_DISTANCE)
                return false;

            if (!phantomPed.IsActive)
                return true;

            float phantomDist = chaseTarget.Position.DistanceTo(
                phantomPed.Ped.Position
            );
            return phantomDist > CHASE_ESCAPE_DISTANCE;
        }

        private void TickCruiseState(Ped phantom, Ped player)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + CRUISE_TICK_INTERVAL;

            if (PhantomTasks.GetWaypointBlip() == null)
            {
                StopDrivingAndFollow(phantom);
                GTA.UI.Notification.Show(
                    "~y~PHANTOM: No waypoint. Holding."
                );
                return;
            }

            if (phantomTasks.TickCruise(phantom, player))
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Arrived at destination."
                );
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void TickAirstrikeActive()
        {
            if (Game.GameTime < tickCooldown)
                return;

            if (airstrikeExplosionIndex
                >= AIRSTRIKE_EXPLOSION_COUNT)
            {
                GTA.UI.Notification.Show(
                    "~g~AIRSTRIKE: Strike complete."
                );
                CleanupAirstrike();
                TransitionTo(PhantomState.Following);
                return;
            }

            phantomTasks.SpawnAirstrikeExplosion(
                airstrikePosition, airstrikeExplosionIndex
            );
            airstrikeExplosionIndex++;
            tickCooldown =
                Game.GameTime + AIRSTRIKE_EXPLOSION_INTERVAL;
        }

        private void HandleChaseStart()
        {
            if (!phantomPed.IsActive)
                return;

            chaseTarget = null;
            phantomMenu.HideAll();
            TransitionTo(PhantomState.ChaseTargeting);
            GTA.UI.Notification.Show(
                "~o~>> Shoot a vehicle to chase. Press "
                + menuToggleKey.ToString()
                + " to cancel."
            );
        }

        private void HandleCruise()
        {
            if (!phantomPed.IsActive)
                return;

            Ped player = Game.Player.Character;
            if (!player.IsSittingInVehicle())
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Get in a vehicle first."
                );
                return;
            }

            if (PhantomTasks.GetWaypointBlip() == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Set a waypoint first."
                );
                return;
            }

            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteCruise(
                phantomPed.Ped, player
            );
            tickCooldown = Game.GameTime + CRUISE_TICK_INTERVAL;
            TransitionTo(PhantomState.Cruise);
        }

        private void HandleAirstrikeStart()
        {
            if (!phantomPed.IsActive)
                return;

            Blip waypoint = PhantomTasks.GetWaypointBlip();
            if (waypoint == null)
            {
                GTA.UI.Notification.Show(
                    "~o~PHANTOM: Set a waypoint first."
                );
                return;
            }

            Ped player = Game.Player.Character;
            Ped phantom = phantomPed.Ped;
            float distToPlayer = waypoint.Position
                .DistanceTo(player.Position);
            float distToPhantom = waypoint.Position
                .DistanceTo(phantom.Position);

            if (distToPlayer < 30f || distToPhantom < 30f)
            {
                GTA.UI.Notification.Show(
                    "~r~PHANTOM: That's way too close. "
                    + "Move the marker further out."
                );
                return;
            }

            phantomTasks.PlayRadioCallAnimation(
                phantomPed.Ped
            );

            airstrikePosition = waypoint.Position;
            airstrikeExplosionIndex = 0;
            tickCooldown = Game.GameTime + AIRSTRIKE_INITIAL_DELAY;

            phantomTasks.SpawnAirstrikeJets(
                airstrikePosition,
                out airstrikeJet1, out airstrikeJet2,
                out airstrikePilot1, out airstrikePilot2
            );

            GTA.UI.Notification.Show(
                "~r~PHANTOM: Airstrike inbound. Get clear."
            );
            TransitionTo(PhantomState.AirstrikeActive);
        }

        private void HandleCancelTargeting()
        {
            CleanupKamikazeTarget();
            CleanupChaseTarget();
            CleanupAirstrike();
            phantomMenu.HideAll();

            if (phantomPed.IsActive)
            {
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
            else
            {
                TransitionTo(PhantomState.Inactive);
            }
        }

        private void CleanupChaseTarget()
        {
            if (chaseTarget != null
                && chaseTarget.Exists())
            {
                Blip b = chaseTarget.AttachedBlip;
                if (b != null && b.Exists())
                    b.Delete();
            }
            chaseTarget = null;
            chaseVehicle = null;
            hasChaseStarted = false;
        }

        private unsafe void CleanupAirstrike()
        {
            if (airstrikePilot1 != null
                && airstrikePilot1.Exists())
            {
                airstrikePilot1.MarkAsNoLongerNeeded();
                airstrikePilot1.Delete();
            }
            airstrikePilot1 = null;

            if (airstrikePilot2 != null
                && airstrikePilot2.Exists())
            {
                airstrikePilot2.MarkAsNoLongerNeeded();
                airstrikePilot2.Delete();
            }
            airstrikePilot2 = null;

            if (airstrikeJet1 != null
                && airstrikeJet1.Exists())
            {
                airstrikeJet1.MarkAsNoLongerNeeded();
                airstrikeJet1.Delete();
            }
            airstrikeJet1 = null;

            if (airstrikeJet2 != null
                && airstrikeJet2.Exists())
            {
                airstrikeJet2.MarkAsNoLongerNeeded();
                airstrikeJet2.Delete();
            }
            airstrikeJet2 = null;
        }

        private void HandleExtraction()
        {
            if (!phantomPed.IsActive)
                return;

            phantomTasks.PlayRadioCallAnimation(
                phantomPed.Ped
            );

            Ped player = Game.Player.Character;

            player.Weapons.Give(
                (WeaponHash)0x497FACC3, 5, false, true
            );

            phantomBackup.SpawnExtractionHeli(player);

            tickCooldown = Game.GameTime + 3000;
            TransitionTo(PhantomState.PickupInbound);

            GTA.UI.Notification.Show(
                "~g~EXTRACTION: Throw a ~o~FLARE~g~ to mark LZ!"
            );
        }

        private void HandleDropAtWaypoint()
        {
            if (currentState != PhantomState.PickupFlying)
            {
                GTA.UI.Notification.Show(
                    "~o~EXTRACTION: Not in extraction heli."
                );
                return;
            }

            Blip waypoint = PhantomTasks.GetWaypointBlip();
            if (waypoint == null)
            {
                GTA.UI.Notification.Show(
                    "~o~EXTRACTION: Set a waypoint first."
                );
                return;
            }

            waypointDropDest = waypoint.Position;
            waypointDropDest.Z = World.GetGroundHeight(
                new Vector2(
                    waypointDropDest.X,
                    waypointDropDest.Y
                )
            );

            phantomBackup.FlyExtractionToWaypoint(
                waypointDropDest
            );

            tickCooldown = Game.GameTime + 5000;
            TransitionTo(PhantomState.PickupDropping);

            GTA.UI.Notification.Show(
                "~g~EXTRACTION: Flying to waypoint."
            );
        }

        private void TickPickupInbound(
            Ped phantom, Ped player)
        {
            if (!phantomBackup.IsExtractionActive)
            {
                TransitionTo(PhantomState.Following);
                return;
            }

            Vehicle heli = phantomBackup.ExtractionHeli;

            if (heli.IsDead || heli.Health <= 0)
            {
                GTA.UI.Notification.Show(
                    "~r~EXTRACTION: Heli destroyed! Call again."
                );
                phantomBackup.CleanupExtraction();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            GTA.UI.Screen.ShowSubtitle(
                "Throw a ~o~FLARE~w~ to mark landing zone",
                100
            );

            uint flareHash = 0x497FACC3;
            bool hasFlareSel =
                (uint)player.Weapons.Current.Hash
                == flareHash;

            if (hasFlareSel && player.IsShooting)
            {
                RaycastResult ray = World.Raycast(
                    GameplayCamera.Position,
                    GameplayCamera.Direction,
                    200f,
                    IntersectFlags.Map,
                    player
                );

                if (ray.DidHit)
                {
                    Vector3 lz = ray.HitPosition;

                    GTA.UI.Notification.Show(
                        "~g~EXTRACTION: LZ marked! Heli inbound."
                    );

                    phantomBackup.LandExtractionAtPosition(
                        lz
                    );

                    phantomBackup.UnlockExtractionForPlayer();
                    phantomPed.LeaveGroupSafe();

                    VehicleSeat ps =
                        PhantomTasks.FindFreeSeat(heli);
                    if (ps != VehicleSeat.None)
                    {
                        phantom.Task.EnterVehicle(
                            heli, ps,
                            10000, 2f,
                            EnterVehicleFlags.None
                        );
                    }

                    tickCooldown = Game.GameTime;
                    TransitionTo(PhantomState.PickupBoarding);
                    return;
                }
            }

            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + 5000;

            phantomBackup.CircleExtractionNearPlayer(
                player
            );
        }

        private void TickPickupBoarding(
            Ped phantom, Ped player)
        {
            if (!phantomBackup.IsExtractionActive)
            {
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            Vehicle heli = phantomBackup.ExtractionHeli;

            if (heli.IsDead || heli.Health <= 0)
            {
                GTA.UI.Notification.Show(
                    "~r~EXTRACTION: Heli destroyed! Call again."
                );
                phantomBackup.CleanupExtraction();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            phantomBackup.UnlockExtractionForPlayer();

            bool playerIn = player.IsInVehicle(heli);
            bool phantomIn = phantom.IsInVehicle(heli);

            if (!playerIn && !player.IsInVehicle())
            {
                float d = heli.Position.DistanceTo(
                    player.Position
                );
                if (d < 15f)
                {
                    GTA.UI.Screen.ShowSubtitle(
                        "Press ~b~F~w~ to board extraction",
                        100
                    );

                    if (Game.IsControlPressed(
                        GTA.Control.Enter))
                    {
                        VehicleSeat ws =
                            PhantomTasks.FindFreeSeat(heli);
                        if (ws != VehicleSeat.None)
                        {
                            player.SetIntoVehicle(
                                heli, ws
                            );
                            playerIn = true;
                        }
                    }
                }
            }

            if (!phantomIn
                && Game.GameTime > tickCooldown + 6000)
            {
                VehicleSeat ws =
                    PhantomTasks.FindFreeSeat(heli);
                if (ws != VehicleSeat.None)
                {
                    phantom.SetIntoVehicle(heli, ws);
                    phantomIn = true;
                }
            }

            if (playerIn && phantomIn)
            {
                phantomBackup.BoardBackupForExtraction(player);
                phantomBackup.FlyExtractionRandom();
                GTA.UI.Notification.Show(
                    "~g~EXTRACTION: All aboard! Flying out."
                );
                tickCooldown = Game.GameTime + 15000;
                TransitionTo(PhantomState.PickupFlying);
            }
        }

        private void TickPickupFlying(
            Ped phantom, Ped player)
        {
            if (!phantomBackup.IsExtractionActive)
            {
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            Vehicle heli = phantomBackup.ExtractionHeli;

            if (heli.IsDead || heli.Health <= 0)
            {
                GTA.UI.Notification.Show(
                    "~r~EXTRACTION: Heli destroyed!"
                );
                phantomBackup.CleanupExtraction();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (!player.IsInVehicle(heli))
            {
                if (IsPlayerDropping(player))
                {
                    CommandPhantomAirdrop(phantom, player);
                    phantomBackup.CommandBackupAirdrop(player);
                    phantomBackup.ReleaseExtractionAircraft();
                    phantomPed.RejoinGroup();
                    TransitionTo(PhantomState.Following);
                    return;
                }

                GTA.UI.Notification.Show(
                    "~y~EXTRACTION: You left the heli."
                );
                phantomBackup.CleanupExtraction();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + 20000;

            phantomBackup.BoardBackupForExtraction(player);
            phantomBackup.FlyBackupExtractionFollowers(
                heli.Position
            );
            phantomBackup.FlyExtractionRandom();
        }

        private void TickPickupDropping(
            Ped phantom, Ped player)
        {
            if (!phantomBackup.IsExtractionActive)
            {
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            Vehicle heli = phantomBackup.ExtractionHeli;

            if (heli.IsDead || heli.Health <= 0)
            {
                GTA.UI.Notification.Show(
                    "~r~EXTRACTION: Heli destroyed!"
                );
                phantomBackup.CleanupExtraction();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (!player.IsInVehicle(heli))
            {
                if (IsPlayerDropping(player))
                {
                    CommandPhantomAirdrop(phantom, player);
                    phantomBackup.CommandBackupAirdrop(player);
                    phantomBackup.ReleaseExtractionAircraft();
                    phantomPed.RejoinGroup();
                    TransitionTo(PhantomState.Following);
                    return;
                }

                GTA.UI.Notification.Show(
                    "~y~EXTRACTION: You left the heli."
                );
                phantomBackup.CleanupExtraction();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            float dist2D = new Vector2(
                heli.Position.X - waypointDropDest.X,
                heli.Position.Y - waypointDropDest.Y
            ).Length();

            GTA.UI.Screen.ShowSubtitle(
                $"~w~Dropping at waypoint: ~b~{dist2D:F0}m",
                100
            );

            if (dist2D < 150f)
            {
                phantomBackup.LandExtractionAtPosition(waypointDropDest);
            }

            if (dist2D < 60f && heli.HeightAboveGround < 6f
                && heli.Speed < 3f)
            {
                GTA.UI.Notification.Show(
                    "~g~EXTRACTION: Arrived! Press ~b~F~g~ to exit."
                );
                phantomPed.RejoinGroup();
                phantomBackup.CleanupExtraction();
                player.Task.LeaveVehicle();
                phantom.Task.LeaveVehicle();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + 8000;

            phantomBackup.BoardBackupForExtraction(player);
            phantomBackup.FlyBackupExtractionFollowers(
                waypointDropDest
            );
            phantomBackup.FlyExtractionToWaypoint(
                waypointDropDest
            );
        }

        private static bool IsPlayerDropping(Ped player)
        {
            return player != null
                && player.Exists()
                && player.IsInAir
                && player.HeightAboveGround > 12f;
        }

        private static void CommandPhantomAirdrop(
            Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists()
                || !phantom.IsAlive)
                return;

            if (!phantom.Weapons.HasWeapon(
                WeaponHash.Parachute))
            {
                phantom.Weapons.Give(
                    WeaponHash.Parachute, 1, false, true
                );
            }

            if (phantom.IsInVehicle())
            {
                phantom.Task.LeaveVehicle(
                    LeaveVehicleFlags.BailOut
                );
                return;
            }

            Vector3 dropPosition = player.Position
                + player.RightVector * 4f;
            dropPosition.Z += 2f;
            phantom.Position = dropPosition;
            phantom.Task.Skydive();
        }

        private void TickParachuteSync(
            Ped phantom, Ped player)
        {
            if (phantom == null || !phantom.Exists())
                return;
            if (!phantom.IsAlive || !player.IsAlive)
                return;

            bool playerFalling =
                player.IsInAir
                && player.HeightAboveGround > 15f;

            if (!playerFalling)
                return;

            if (!phantom.Weapons.HasWeapon(
                WeaponHash.Parachute))
            {
                phantom.Weapons.Give(
                    WeaponHash.Parachute, 1, false, true
                );
            }

            float dist = phantom.Position.DistanceTo(
                player.Position
            );

            if (dist > 20f)
            {
                Vector3 rightOf = player.Position
                    + player.RightVector * 3f;
                rightOf.Z += 2f;
                phantom.Position = rightOf;
            }

            int playerState = Function.Call<int>(
                Hash.GET_PED_PARACHUTE_STATE,
                player.Handle
            );
            int phantomState = Function.Call<int>(
                Hash.GET_PED_PARACHUTE_STATE,
                phantom.Handle
            );

            if (phantomState < 0)
                phantom.Task.Skydive();

            if (playerState >= 1 && phantomState <= 0)
            {
                phantom.Task.UseParachute();
            }

            if (playerState == 2 && phantomState < 2)
            {
                phantom.OpenParachute();
            }

            if (playerState >= 1 && phantomState >= 1)
            {
                Vector3 targetPos = player.Position
                    + player.RightVector * 4f;
                targetPos.Z += 2.5f;

                Vector3 current = phantom.Position;
                Vector3 lerped = new Vector3(
                    current.X + (targetPos.X - current.X) * 0.08f,
                    current.Y + (targetPos.Y - current.Y) * 0.08f,
                    current.Z + (targetPos.Z - current.Z) * 0.08f
                );

                Function.Call(
                    Hash.SET_ENTITY_COORDS,
                    phantom.Handle,
                    lerped.X, lerped.Y, lerped.Z,
                    false, false, false, true
                );
            }
        }

        private Entity FindAimTarget(Ped player)
        {

            Entity aimed = Game.Player.TargetedEntity;
            if (aimed != null && aimed.Exists())
            {
                if (aimed is Ped ped && ped.IsAlive && ped != player && ped != phantomPed.Ped)
                {
                    return ped;
                }
                if (aimed is Vehicle veh && !veh.IsDead)
                {
                    return veh;
                }
            }


            RaycastResult ray = World.Raycast(
                GameplayCamera.Position,
                GameplayCamera.Direction,
                150f,
                IntersectFlags.Everything,
                player
            );
            if (ray.DidHit && ray.HitEntity != null && ray.HitEntity.Exists())
            {
                Entity hit = ray.HitEntity;
                if (hit is Ped ped && ped.IsAlive && ped != player && ped != phantomPed.Ped)
                {
                    return ped;
                }
                if (hit is Vehicle veh && !veh.IsDead)
                {
                    return veh;
                }
            }
            return null;
        }

        private void DrawTacticalHUD(Entity entity, bool locked)
        {
            if (entity == null || !entity.Exists())
                return;


            Ped player = Game.Player.Character;
            float distance = player.Position.DistanceTo(entity.Position);


            float screenX = GTA.UI.Screen.ScaledWidth - 320f;
            float screenY = 150f;


            string typeStr = entity is Ped ? "INFANTRY / PERSON" : "VEHICLE / TRANSPORT";
            string nameStr = "N/A";
            int occupantCount = 0;
            float healthPct = (entity.Health / (float)entity.MaxHealth) * 100f;
            if (healthPct < 0f) healthPct = 0f;

            Vehicle targetVeh = null;
            if (entity is Ped targetPed)
            {
                if (targetPed.IsInVehicle())
                {
                    targetVeh = targetPed.CurrentVehicle;
                }
            }
            else if (entity is Vehicle v)
            {
                targetVeh = v;
            }

            if (targetVeh != null && targetVeh.Exists())
            {
                typeStr = "VEHICLE TARGET";
                nameStr = targetVeh.LocalizedName;
                if (string.IsNullOrEmpty(nameStr) || nameStr == "NULL")
                {
                    nameStr = targetVeh.Model.ToString();
                }


                for (int seat = -1; seat < targetVeh.PassengerCapacity; seat++)
                {
                    Ped occupant = targetVeh.GetPedOnSeat((VehicleSeat)seat);
                    if (occupant != null && occupant.Exists() && occupant.IsAlive)
                    {
                        occupantCount++;
                    }
                }
            }


            System.Drawing.Color panelColor = locked ? System.Drawing.Color.FromArgb(180, 80, 0, 0) : System.Drawing.Color.FromArgb(180, 10, 20, 30);
            System.Drawing.Color borderColor = locked ? System.Drawing.Color.FromArgb(255, 220, 20, 20) : System.Drawing.Color.FromArgb(255, 0, 180, 255);


            DrawRect(screenX, screenY, 6f, 160f, borderColor);
            DrawRect(screenX + 6f, screenY, 284f, 160f, panelColor);


            string statusHeader = locked ? "SYSTEM: PHANTOM LOCKED" : "TARGET ACQUISITION ACTIVE";
            DrawText(statusHeader, screenX + 15f, screenY + 10f, 0.32f, borderColor, true);

            DrawText($"CLASS: {typeStr}", screenX + 15f, screenY + 35f, 0.28f, System.Drawing.Color.White);
            if (targetVeh != null)
            {
                DrawText($"MODEL: {nameStr.ToUpper()}", screenX + 15f, screenY + 55f, 0.28f, System.Drawing.Color.White);
                DrawText($"OCCUPANTS: {occupantCount}", screenX + 15f, screenY + 75f, 0.28f, System.Drawing.Color.White);
            }
            else
            {
                DrawText($"MODEL: ON FOOT", screenX + 15f, screenY + 55f, 0.28f, System.Drawing.Color.White);
                DrawText($"OCCUPANTS: 1", screenX + 15f, screenY + 75f, 0.28f, System.Drawing.Color.White);
            }

            DrawText($"DISTANCE: {distance:F1}m", screenX + 15f, screenY + 95f, 0.28f, System.Drawing.Color.White);
            DrawText($"HEALTH: {healthPct:F0}%", screenX + 15f, screenY + 115f, 0.28f, healthPct > 50f ? System.Drawing.Color.LightGreen : System.Drawing.Color.OrangeRed);

            if (!locked)
            {
                DrawText("[T] KEY - PRESS TO LOCK ON TARGET", screenX + 15f, screenY + 138f, 0.26f, System.Drawing.Color.Yellow, true);
            }
            else
            {
                DrawText("PHANTOM PROTOCOL ENGAGED", screenX + 15f, screenY + 138f, 0.26f, System.Drawing.Color.Orange, true);
            }


            World.DrawMarker(
                MarkerType.ThickChevronUp,
                entity.Position + new Vector3(0, 0, 1.8f),
                Vector3.Zero,
                new Vector3(0f, 180f, 0f),
                new Vector3(0.6f, 0.6f, 0.6f),
                borderColor,
                false,
                false,
                true
            );
        }

        private void DrawRect(float x, float y, float width, float height, System.Drawing.Color color)
        {
            Function.Call(Hash.DRAW_RECT,
                (x + width / 2f) / GTA.UI.Screen.ScaledWidth,
                (y + height / 2f) / 720f,
                width / GTA.UI.Screen.ScaledWidth,
                height / 720f,
                color.R, color.G, color.B, color.A
            );
        }

        private void DrawText(string text, float x, float y, float scale, System.Drawing.Color color, bool bold = false)
        {
            var textElement = new GTA.UI.TextElement(
                text,
                new System.Drawing.PointF(x, y),
                scale,
                color,
                GTA.UI.Font.ChaletLondon,
                GTA.UI.Alignment.Left
            );
            textElement.Draw();
        }

        private void DrawScanningIndicator(string text)
        {
            float screenWidth = GTA.UI.Screen.ScaledWidth;
            float panelWidth = 400f;
            float panelHeight = 35f;
            float panelX = (screenWidth - panelWidth) / 2f;
            float panelY = 620f;

            DrawRect(panelX, panelY, panelWidth, panelHeight, System.Drawing.Color.FromArgb(160, 10, 15, 20));
            DrawRect(panelX, panelY, 4f, panelHeight, System.Drawing.Color.FromArgb(255, 0, 180, 255));

            DrawText(text, panelX + 15f, panelY + 6f, 0.28f, System.Drawing.Color.FromArgb(255, 0, 180, 255), true);
        }
    }
}
