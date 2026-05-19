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

        private PhantomState currentState = PhantomState.Inactive;
        private Entity kamikazeTarget;
        private Vehicle chaseTarget;
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
            phantomMenu.UpdateItemStates(phantomPed.IsActive);

            phantomPed.CleanupDeadBlip();

            if (!phantomPed.IsActive)
            {
                if (currentState != PhantomState.Inactive)
                {
                    kamikazeTarget = null;
                    TransitionTo(PhantomState.Inactive);
                }
                return;
            }

            Ped phantom = phantomPed.Ped;
            Ped player = Game.Player.Character;

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
                    TickKamikazeSuicide(phantom);
                    break;
                case PhantomState.KamikazeRam:
                    TickKamikazeRam(phantom);
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

            TickBackup(player);
            TickAutoBackup(phantom, player);
            TickParachuteSync(phantom, player);
            UpdateHealthRegeneration();
        }

        private void TickFollowing(Ped phantom)
        {
            if (!phantom.IsInGroup)
                phantomPed.RejoinGroup();
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

            phantomTasks.TickDriveTo(phantom, player);

            Blip waypoint = PhantomTasks.GetWaypointBlip();
            if (waypoint == null)
                return;

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

            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + HIJACK_TICK_INTERVAL;

            phantomTasks.TickVehicleHijack(phantom, player);
        }

        private void TickKamikazeTargeting(Ped player)
        {
            if (!player.IsShooting)
                return;

            Entity marked = FindDamagedEntity(player);
            if (marked == null)
                return;

            if (marked is Ped mp && !mp.IsAlive)
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Target down."
                );
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            if (marked is Vehicle mv && mv.IsDead)
            {
                GTA.UI.Notification.Show(
                    "~g~PHANTOM: Target destroyed."
                );
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
                return;
            }

            kamikazeTarget = marked;

            Blip targetBlip = marked.AttachedBlip;
            if (targetBlip == null || !targetBlip.Exists())
            {
                targetBlip = marked.AddBlip();
                targetBlip.Color = BlipColor.Red;
                targetBlip.Name = "PHANTOM TARGET";
                targetBlip.Scale = 0.7f;
            }

            GTA.UI.Notification.Show(
                "~r~PHANTOM: Target marked. Choose attack."
            );

            phantomMenu.ShowKamikazeOptions(
                marked is Vehicle
            );
        }

        private Entity FindDamagedEntity(Ped player)
        {
            Ped[] nearbyPeds = World.GetNearbyPeds(
                player.Position, 100f
            );

            for (int i = 0; i < nearbyPeds.Length; i++)
            {
                Ped ped = nearbyPeds[i];
                if (ped == null || !ped.Exists())
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
                if (v == null || !v.Exists())
                    continue;
                if (v.HasBeenDamagedBy(player))
                    return v;
            }

            return null;
        }

        private void TickKamikazeSuicide(Ped phantom)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + KAMIKAZE_TICK_INTERVAL;

            if (phantomTasks.TickSuicideBomb(
                phantom, kamikazeTarget))
            {
                CleanupKamikazeTarget();
                TransitionTo(PhantomState.Inactive);
            }
        }

        private void TickKamikazeRam(Ped phantom)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + KAMIKAZE_TICK_INTERVAL;

            if (phantomTasks.TickRamExplode(
                phantom, kamikazeTarget))
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

            if (phantomTasks.TickExecuteTarget(
                phantom, kamikazeTarget))
            {
                CleanupKamikazeTarget();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void TickBackup(Ped player)
        {
            if (Game.GameTime < backupTickCooldown)
                return;
            backupTickCooldown =
                Game.GameTime + BACKUP_TICK_INTERVAL;

            phantomBackup.Tick(player);
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
            if (kamikazeTarget != null
                && kamikazeTarget.Exists())
            {
                Blip b = kamikazeTarget.AttachedBlip;
                if (b != null && b.Exists())
                    b.Delete();
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

            CleanupKamikazeTarget();
            phantomBackup.DismissAll();
            phantomPed.Dismiss();
            TransitionTo(PhantomState.Inactive);
        }

        private void HandleFollow()
        {
            if (!phantomPed.IsActive)
                return;

            phantomPed.Ped.Task.ClearAll();
            phantomPed.RejoinGroup();
            TransitionTo(PhantomState.Following);
        }

        private void HandleWait()
        {
            if (!phantomPed.IsActive)
                return;

            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteWait(phantomPed.Ped);
            TransitionTo(PhantomState.Waiting);
        }

        private void HandleCombat()
        {
            if (!phantomPed.IsActive)
                return;

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

            phantomPed.LeaveGroupSafe();
            phantomTasks.ExecuteVehicleHijack(
                phantomPed.Ped, Game.Player.Character
            );
            tickCooldown = Game.GameTime + HIJACK_INITIAL_DELAY;
            TransitionTo(PhantomState.VehicleHijack);
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
                phantomPed.Ped, kamikazeTarget
            );
            tickCooldown = Game.GameTime + EXECUTE_TICK_INTERVAL;
            TransitionTo(PhantomState.KamikazeExecute);
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
                    Blip b = v.AddBlip();
                    b.Color = BlipColor.Red;
                    b.Name = "CHASE TARGET";
                    b.Scale = 0.8f;

                    phantomPed.LeaveGroupSafe();
                    phantomTasks.ExecuteChase(
                        phantomPed.Ped, v
                    );
                    tickCooldown =
                        Game.GameTime + CHASE_TICK_INTERVAL;
                    TransitionTo(PhantomState.ChaseActive);
                    return;
                }
            }
        }

        private void TickChaseActive(Ped phantom)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + CHASE_TICK_INTERVAL;

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

            if (phantomTasks.TickChase(phantom, chaseTarget))
            {
                CleanupChaseTarget();
                phantomPed.RejoinGroup();
                TransitionTo(PhantomState.Following);
            }
        }

        private void TickCruiseState(Ped phantom, Ped player)
        {
            if (Game.GameTime < tickCooldown)
                return;
            tickCooldown = Game.GameTime + CRUISE_TICK_INTERVAL;

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
                "~r~AIRSTRIKE: Jets inbound! Take cover!"
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

            phantomBackup.FlyExtractionToWaypoint(
                waypointDropDest
            );
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

            if (playerState >= 1 && phantomState == 0)
            {
                Function.Call(
                    Hash.TASK_PARACHUTE,
                    phantom.Handle, true
                );
            }

            if (playerState == 2 && phantomState < 2)
            {
                Function.Call(
                    Hash.FORCE_PED_TO_OPEN_PARACHUTE,
                    phantom.Handle
                );
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
    }
}
