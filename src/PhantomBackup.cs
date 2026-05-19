using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace PHANTOM
{
    public sealed class PhantomBackup : IDisposable
    {
        private const int PEDS_PER_UNIT = 5;
        private const int MAX_UNITS = 4;
        private const float SPAWN_DISTANCE_FOOT = 35f;
        private const float SPAWN_DISTANCE_VEHICLE = 55f;
        private const float DESPAWN_DISTANCE = 200f;
        private const float HELI_ALTITUDE = 60f;
        private const float HELI_CRUISE_SPEED = 40f;
        private const int HELI_FLIGHT_HEIGHT = 50;
        private const int HELI_MIN_HEIGHT = 30;
        private const int BLIP_SPRITE_BACKUP = 304;
        private const int BLIP_COLOR_BACKUP = 3;
        private const int BLIP_SPRITE_AIR = 422;
        private const int HELI_MISSION_FOLLOW = 10;
        private const int HELI_MISSION_LAND = 20;
        private const float EXTRACT_SPAWN_DIST = 100f;
        private const float EXTRACT_ALTITUDE = 40f;
        private const float LAND_RANGE = 8f;

        private readonly ScriptSettings config;
        private readonly List<Ped> groundPeds = new List<Ped>();
        private readonly List<Vehicle> groundVehicles = new List<Vehicle>();
        private readonly List<Ped> airPeds = new List<Ped>();
        private readonly List<Vehicle> airVehicles = new List<Vehicle>();
        private bool isDismissing;
        private bool isDisposed;

        public Vehicle ExtractionHeli { get; private set; }
        public Ped ExtractionPilot { get; private set; }
        public bool IsExtractionActive =>
            ExtractionHeli != null && ExtractionHeli.Exists();

        public int AliveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < groundPeds.Count; i++)
                {
                    if (groundPeds[i] != null
                        && groundPeds[i].Exists()
                        && groundPeds[i].IsAlive)
                        count++;
                }
                for (int i = 0; i < airPeds.Count; i++)
                {
                    if (airPeds[i] != null
                        && airPeds[i].Exists()
                        && airPeds[i].IsAlive)
                        count++;
                }
                return count;
            }
        }

        public bool HasActiveBackup => AliveCount > 0;
        public int ActiveUnitCount => AliveCount / PEDS_PER_UNIT;

        public PhantomBackup(ScriptSettings config)
        {
            this.config = config
                ?? throw new ArgumentNullException(nameof(config));
        }

        public void SpawnGroundUnits(
            Ped player, int unitCount, bool inVehicle)
        {
            if (unitCount < 1)
                unitCount = 1;
            if (unitCount > MAX_UNITS)
                unitCount = MAX_UNITS;

            string pedModel = config.GetValue(
                "General", "PedModel", "s_m_y_blackops_01"
            );
            string primaryWeapon = config.GetValue(
                "Weapons", "Primary", "WEAPON_CARBINERIFLE"
            );
            string secondaryWeapon = config.GetValue(
                "Weapons", "Secondary", "WEAPON_SMG"
            );

            Model ped = new Model(pedModel);
            ped.Request(5000);
            if (!ped.IsLoaded)
            {
                GTA.UI.Notification.Show(
                    "~r~BACKUP: Failed to load ped model."
                );
                return;
            }

            Model vehicleModel = null;
            if (inVehicle)
            {
                vehicleModel = new Model("bison");
                vehicleModel.Request(5000);
                if (!vehicleModel.IsLoaded)
                {
                    GTA.UI.Notification.Show(
                        "~r~BACKUP: Failed to load vehicle."
                    );
                    ped.MarkAsNoLongerNeeded();
                    return;
                }
            }

            Vector3 playerPos = player.Position;
            Vector3 forward = player.ForwardVector;

            for (int u = 0; u < unitCount; u++)
            {
                float angle = (u - unitCount / 2f) * 15f;
                float rad = angle * (float)Math.PI / 180f;
                float spawnDist = inVehicle
                    ? SPAWN_DISTANCE_VEHICLE
                    : SPAWN_DISTANCE_FOOT;
                Vector3 offset = new Vector3(
                    (float)Math.Sin(rad) * spawnDist,
                    (float)Math.Cos(rad) * spawnDist,
                    0f
                );
                Vector3 spawnPos = playerPos - forward
                    * spawnDist + offset;

                if (inVehicle)
                {
                    SpawnVehicleUnit(
                        spawnPos, player, ped,
                        vehicleModel, primaryWeapon,
                        secondaryWeapon
                    );
                }
                else
                {
                    SpawnFootUnit(
                        spawnPos, player, ped,
                        primaryWeapon, secondaryWeapon
                    );
                }
            }

            ped.MarkAsNoLongerNeeded();
            if (inVehicle)
                vehicleModel.MarkAsNoLongerNeeded();

            GTA.UI.Notification.Show(
                $"~b~BACKUP: {unitCount} unit(s) deployed."
            );
        }

        public void SpawnAirSupport(Ped player, int chopperCount)
        {
            if (chopperCount < 1)
                chopperCount = 1;
            if (chopperCount > 2)
                chopperCount = 2;

            string pedModel = config.GetValue(
                "General", "PedModel", "s_m_y_blackops_01"
            );
            string primaryWeapon = config.GetValue(
                "Weapons", "Primary", "WEAPON_CARBINERIFLE"
            );

            Model pedMdl = new Model(pedModel);
            pedMdl.Request(5000);
            if (!pedMdl.IsLoaded)
            {
                GTA.UI.Notification.Show(
                    "~r~AIR SUPPORT: Failed to load ped."
                );
                return;
            }

            Model heliMdl = new Model("buzzard2");
            heliMdl.Request(5000);
            if (!heliMdl.IsLoaded)
            {
                GTA.UI.Notification.Show(
                    "~r~AIR SUPPORT: Failed to load Buzzard."
                );
                pedMdl.MarkAsNoLongerNeeded();
                return;
            }

            Vector3 playerPos = player.Position;

            for (int h = 0; h < chopperCount; h++)
            {
                float xOffset = (h == 0) ? -40f : 40f;
                Vector3 spawnPos = new Vector3(
                    playerPos.X + xOffset,
                    playerPos.Y - 80f,
                    playerPos.Z + HELI_ALTITUDE
                );

                Vehicle heli = World.CreateVehicle(
                    heliMdl, spawnPos, 0f
                );
                if (heli == null || !heli.Exists())
                    continue;

                heli.IsEngineRunning = true;
                airVehicles.Add(heli);

                Blip heliBlip = heli.AddBlip();
                heliBlip.Sprite = (BlipSprite)BLIP_SPRITE_AIR;
                heliBlip.Color = (BlipColor)BLIP_COLOR_BACKUP;
                heliBlip.Name = "PHANTOM AIR";
                heliBlip.Scale = 0.7f;

                Ped pilot = World.CreatePed(
                    pedMdl, spawnPos, 0f
                );
                if (pilot == null || !pilot.Exists())
                    continue;

                ConfigureBackupPed(
                    pilot, player, primaryWeapon, null
                );
                pilot.SetIntoVehicle(
                    heli, VehicleSeat.Driver
                );
                airPeds.Add(pilot);

                Function.Call(
                    Hash.TASK_HELI_MISSION,
                    pilot.Handle,
                    heli.Handle,
                    0,
                    player.Handle,
                    0f, 0f, 0f,
                    HELI_MISSION_FOLLOW,
                    HELI_CRUISE_SPEED,
                    10f,
                    -1f,
                    HELI_FLIGHT_HEIGHT,
                    HELI_MIN_HEIGHT,
                    -1f,
                    0
                );

                int seats = heli.PassengerCapacity;
                int gunners = Math.Min(seats, 3);

                for (int s = 0; s < gunners; s++)
                {
                    Ped gunner = World.CreatePed(
                        pedMdl, spawnPos, 0f
                    );
                    if (gunner == null || !gunner.Exists())
                        continue;

                    ConfigureBackupPed(
                        gunner, player,
                        "WEAPON_MICROSMG", null
                    );
                    gunner.SetIntoVehicle(
                        heli, (VehicleSeat)s
                    );
                    airPeds.Add(gunner);
                }
            }

            pedMdl.MarkAsNoLongerNeeded();
            heliMdl.MarkAsNoLongerNeeded();

            GTA.UI.Notification.Show(
                $"~b~AIR SUPPORT: {chopperCount} Buzzard(s)."
            );
        }

        public void SpawnExtractionHeli(Ped player)
        {
            if (IsExtractionActive)
                return;

            Model heliMdl = new Model("annihilator");
            heliMdl.Request(5000);
            if (!heliMdl.IsLoaded)
            {
                GTA.UI.Notification.Show(
                    "~r~EXTRACTION: Failed to load heli."
                );
                return;
            }

            Model pilotMdl = new Model("s_m_y_pilot_01");
            pilotMdl.Request(5000);

            Vector3 spawnPos = player.Position
                + player.ForwardVector * -EXTRACT_SPAWN_DIST;
            spawnPos.Z += EXTRACT_ALTITUDE;

            ExtractionHeli = World.CreateVehicle(
                heliMdl, spawnPos, player.Heading
            );
            if (ExtractionHeli == null
                || !ExtractionHeli.Exists())
                return;

            ExtractionHeli.IsEngineRunning = true;

            ExtractionPilot = World.CreatePed(
                pilotMdl, spawnPos, 0f
            );
            if (ExtractionPilot != null
                && ExtractionPilot.Exists())
            {
                ExtractionPilot.SetIntoVehicle(
                    ExtractionHeli, VehicleSeat.Driver
                );
                ExtractionPilot.RelationshipGroup =
                    player.RelationshipGroup;
                ExtractionPilot.BlockPermanentEvents = true;
                ExtractionPilot.IsInvincible = true;
                ExtractionPilot.CanBeTargetted = false;
                ExtractionPilot.CanBeDraggedOutOfVehicle
                    = false;

                Function.Call(
                    Hash.SET_PED_COMBAT_ATTRIBUTES,
                    ExtractionPilot.Handle, 1, true
                );
                Function.Call(
                    Hash.SET_PED_COMBAT_ATTRIBUTES,
                    ExtractionPilot.Handle, 3, false
                );
                Function.Call(
                    Hash.SET_DRIVER_ABILITY,
                    ExtractionPilot.Handle, 1.0f
                );
            }

            ExtractionHeli.MaxHealth = 3000;
            ExtractionHeli.Health = 3000;
            Function.Call(
                Hash.SET_VEHICLE_ENGINE_CAN_DEGRADE,
                ExtractionHeli.Handle, false
            );

            Blip b = ExtractionHeli.AddBlip();
            b.Sprite = (BlipSprite)BLIP_SPRITE_AIR;
            b.Color = BlipColor.Green;
            b.Name = "EXTRACTION";
            b.Scale = 0.9f;

            heliMdl.MarkAsNoLongerNeeded();
            pilotMdl.MarkAsNoLongerNeeded();

            GTA.UI.Notification.Show(
                "~g~EXTRACTION: Heli inbound! Stand by."
            );
        }

        public void CircleExtractionNearPlayer(Ped player)
        {
            if (!IsExtractionActive || ExtractionPilot == null)
                return;

            Vector3 pos = player.Position;
            pos.Z += 40f;

            Function.Call(
                Hash.TASK_HELI_MISSION,
                ExtractionPilot.Handle,
                ExtractionHeli.Handle,
                0, 0,
                pos.X, pos.Y, pos.Z,
                10,
                40f,
                30f,
                -1f,
                30,
                30,
                -1f,
                0
            );
        }

        public void LandExtractionAtPosition(Vector3 lz)
        {
            if (!IsExtractionActive || ExtractionPilot == null)
                return;

            Function.Call(
                Hash.TASK_HELI_MISSION,
                ExtractionPilot.Handle,
                ExtractionHeli.Handle,
                0, 0,
                lz.X, lz.Y, lz.Z,
                4,
                40f,
                5f,
                -1f,
                10,
                10,
                -1f,
                32
            );
        }

        public void UnlockExtractionForPlayer()
        {
            if (!IsExtractionActive)
                return;

            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED,
                ExtractionHeli.Handle, 0
            );
            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED_FOR_PLAYER,
                ExtractionHeli.Handle,
                Game.Player.Handle,
                false
            );
            Function.Call(
                Hash.SET_VEHICLE_DOORS_LOCKED_FOR_ALL_PLAYERS,
                ExtractionHeli.Handle,
                false
            );
        }

        public void FlyExtractionRandom()
        {
            if (!IsExtractionActive || ExtractionPilot == null)
                return;

            Random rng = new Random();
            Vector3 dest = ExtractionHeli.Position
                + new Vector3(
                    (float)(rng.NextDouble() * 600 - 300),
                    (float)(rng.NextDouble() * 600 - 300),
                    0f
                );
            dest.Z = ExtractionHeli.Position.Z + 60f;

            Function.Call(
                Hash.TASK_HELI_MISSION,
                ExtractionPilot.Handle,
                ExtractionHeli.Handle,
                0, 0,
                dest.X, dest.Y, dest.Z,
                4,
                60f,
                10f,
                -1f,
                60,
                30,
                -1f,
                0
            );
        }

        public void FlyExtractionToWaypoint(Vector3 dest)
        {
            if (!IsExtractionActive || ExtractionPilot == null)
                return;

            Function.Call(
                Hash.TASK_HELI_MISSION,
                ExtractionPilot.Handle,
                ExtractionHeli.Handle,
                0, 0,
                dest.X, dest.Y, dest.Z,
                4,
                60f,
                5f,
                -1f,
                20,
                15,
                -1f,
                32
            );
        }

        public void CleanupExtraction()
        {
            if (ExtractionPilot != null
                && ExtractionPilot.Exists())
            {
                ExtractionPilot.Delete();
                ExtractionPilot = null;
            }
            if (ExtractionHeli != null
                && ExtractionHeli.Exists())
            {
                if (ExtractionHeli.AttachedBlip != null)
                    ExtractionHeli.AttachedBlip.Delete();
                ExtractionHeli.Delete();
                ExtractionHeli = null;
            }
        }

        public void DismissAll()
        {
            isDismissing = true;

            for (int i = airPeds.Count - 1; i >= 0; i--)
            {
                Ped p = airPeds[i];
                if (p != null && p.Exists())
                {
                    Blip b = p.AttachedBlip;
                    if (b != null && b.Exists())
                        b.Delete();
                    p.MarkAsNoLongerNeeded();
                    p.Delete();
                }
            }
            airPeds.Clear();

            for (int i = airVehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = airVehicles[i];
                if (v != null && v.Exists())
                {
                    Blip b = v.AttachedBlip;
                    if (b != null && b.Exists())
                        b.Delete();
                    v.MarkAsNoLongerNeeded();
                    v.Delete();
                }
            }
            airVehicles.Clear();

            for (int i = groundPeds.Count - 1; i >= 0; i--)
            {
                Ped p = groundPeds[i];
                if (p != null && p.Exists())
                {
                    Blip b = p.AttachedBlip;
                    if (b != null && b.Exists())
                        b.Delete();
                    p.Task.ClearAll();
                    p.MarkAsNoLongerNeeded();
                    p.Delete();
                }
            }
            groundPeds.Clear();

            for (int i = groundVehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = groundVehicles[i];
                if (v != null && v.Exists())
                {
                    Blip b = v.AttachedBlip;
                    if (b != null && b.Exists())
                        b.Delete();
                    v.MarkAsNoLongerNeeded();
                    v.Delete();
                }
            }
            groundVehicles.Clear();

            CleanupExtraction();

            isDismissing = false;

            GTA.UI.Notification.Show(
                "~y~BACKUP: All units dismissed."
            );
        }

        public void Tick(Ped player)
        {
            if (isDismissing)
                return;

            CleanupDead();

            for (int i = 0; i < airVehicles.Count; i++)
            {
                Vehicle heli = airVehicles[i];
                if (heli == null || !heli.Exists())
                    continue;

                Ped pilot = heli.Driver;
                if (pilot == null || !pilot.Exists()
                    || !pilot.IsAlive)
                    continue;

                if (pilot.Position.DistanceTo(player.Position)
                    > 200f)
                {
                    Function.Call(
                        Hash.TASK_HELI_MISSION,
                        pilot.Handle,
                        heli.Handle,
                        0,
                        player.Handle,
                        0f, 0f, 0f,
                        HELI_MISSION_FOLLOW,
                        HELI_CRUISE_SPEED,
                        10f,
                        -1f,
                        HELI_FLIGHT_HEIGHT,
                        HELI_MIN_HEIGHT,
                        -1f,
                        0
                    );
                }
            }

            for (int i = 0; i < airPeds.Count; i++)
            {
                Ped p = airPeds[i];
                if (p == null || !p.Exists() || !p.IsAlive)
                    continue;
                if (p.SeatIndex == VehicleSeat.Driver)
                    continue;

                Ped enemy = FindNearestEnemy(
                    p, player, 200f
                );
                if (enemy != null)
                {
                    p.Task.FightAgainst(enemy);
                    continue;
                }
                if (!p.IsInCombat)
                    p.Task.FightAgainstHatedTargets(150f);
            }

            for (int i = 0; i < groundVehicles.Count; i++)
            {
                Vehicle v = groundVehicles[i];
                if (v == null || !v.Exists() || v.IsDead)
                    continue;

                Ped driver = v.Driver;
                if (driver == null || !driver.Exists()
                    || !driver.IsAlive)
                    continue;

                Vehicle playerVeh =
                    player.CurrentVehicle;
                if (playerVeh != null
                    && playerVeh.Exists())
                {
                    Function.Call(
                        (Hash)0x0FA6E4B75F302400,
                        driver.Handle,
                        v.Handle,
                        playerVeh.Handle,
                        -1,
                        80f,
                        524860,
                        10f,
                        0,
                        15f
                    );
                }
                else
                {
                    float dist = driver.Position
                        .DistanceTo(player.Position);
                    if (dist > 30f)
                    {
                        driver.Task.DriveTo(
                            v,
                            player.Position,
                            8f,
                            80f,
                            (DrivingStyle)524860
                        );
                    }
                }
            }

            for (int i = 0; i < groundPeds.Count; i++)
            {
                Ped p = groundPeds[i];
                if (p == null || !p.Exists() || !p.IsAlive)
                    continue;
                if (p.IsInVehicle())
                    continue;
                if (p.IsInCombat)
                    continue;

                float dist = p.Position.DistanceTo(
                    player.Position
                );
                if (dist > 25f)
                    p.Task.GoTo(player);
            }
        }

        private void SpawnVehicleUnit(
            Vector3 spawnPos, Ped player, Model pedModel,
            Model vehicleModel, string primary, string secondary)
        {
            Vehicle vehicle = World.CreateVehicle(
                vehicleModel, spawnPos,
                player.Heading + 180f
            );
            if (vehicle == null || !vehicle.Exists())
                return;

            vehicle.IsEngineRunning = true;
            groundVehicles.Add(vehicle);

            int seats = Math.Min(
                vehicle.PassengerCapacity + 1,
                PEDS_PER_UNIT
            );

            Ped driver = null;

            for (int s = 0; s < seats; s++)
            {
                Ped unit = World.CreatePed(
                    pedModel, spawnPos, player.Heading
                );
                if (unit == null || !unit.Exists())
                    continue;

                ConfigureBackupPed(
                    unit, player, primary, secondary
                );

                VehicleSeat seat = (s == 0)
                    ? VehicleSeat.Driver
                    : (VehicleSeat)(s - 1);

                unit.SetIntoVehicle(vehicle, seat);
                groundPeds.Add(unit);

                if (s == 0)
                    driver = unit;
            }

            if (driver != null && driver.Exists())
            {
                driver.Task.DriveTo(
                    vehicle,
                    player.Position,
                    10f,
                    80f,
                    (DrivingStyle)524860
                );
            }
        }

        private void SpawnFootUnit(
            Vector3 spawnPos, Ped player, Model pedModel,
            string primary, string secondary)
        {
            for (int s = 0; s < PEDS_PER_UNIT; s++)
            {
                float xOff = (s % 3 - 1) * 2f;
                float yOff = (s / 3) * 2f;
                Vector3 pos = spawnPos + new Vector3(
                    xOff, yOff, 0f
                );

                Ped unit = World.CreatePed(
                    pedModel, pos, player.Heading
                );
                if (unit == null || !unit.Exists())
                    continue;

                ConfigureBackupPed(
                    unit, player, primary, secondary
                );

                Vehicle playerVeh = player.CurrentVehicle;
                if (playerVeh != null
                    && playerVeh.Exists())
                {
                    int freeSeat = -99;
                    for (int seat = 0;
                        seat < playerVeh.PassengerCapacity;
                        seat++)
                    {
                        if (playerVeh.IsSeatFree(
                            (VehicleSeat)seat))
                        {
                            freeSeat = seat;
                            break;
                        }
                    }
                    if (freeSeat != -99)
                    {
                        unit.SetIntoVehicle(
                            playerVeh,
                            (VehicleSeat)freeSeat
                        );
                        groundPeds.Add(unit);
                        continue;
                    }
                }

                unit.Task.GoTo(player);
                groundPeds.Add(unit);
            }
        }

        private void ConfigureBackupPed(
            Ped ped, Ped player, string primary,
            string secondary)
        {
            ped.RelationshipGroup = player.RelationshipGroup;
            ped.BlockPermanentEvents = false;
            ped.CanBeTargetted = false;

            Function.Call(
                Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 2
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES,
                ped.Handle, 46, true
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES,
                ped.Handle, 5, true
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES,
                ped.Handle, 2, true
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES,
                ped.Handle, 20, true
            );
            Function.Call(
                Hash.SET_PED_FIRING_PATTERN,
                ped.Handle,
                (uint)FiringPattern.FullAuto
            );
            Function.Call(
                Hash.SET_PED_SHOOT_RATE, ped.Handle, 800
            );
            Function.Call(
                Hash.SET_PED_ACCURACY, ped.Handle, 70
            );

            WeaponHash primaryHash =
                (WeaponHash)Game.GenerateHash(primary);
            ped.Weapons.Give(primaryHash, 9999, true, true);

            if (secondary != null)
            {
                WeaponHash secondaryHash =
                    (WeaponHash)Game.GenerateHash(secondary);
                ped.Weapons.Give(
                    secondaryHash, 9999, false, true
                );
            }

            Blip blip = ped.AddBlip();
            blip.Sprite = (BlipSprite)BLIP_SPRITE_BACKUP;
            blip.Color = (BlipColor)BLIP_COLOR_BACKUP;
            blip.Name = "BACKUP";
            blip.Scale = 0.6f;
        }

        private void CleanupDead()
        {
            for (int i = groundPeds.Count - 1; i >= 0; i--)
            {
                Ped p = groundPeds[i];
                if (p == null || !p.Exists() || !p.IsAlive)
                {
                    if (p != null && p.Exists())
                    {
                        Blip b = p.AttachedBlip;
                        if (b != null && b.Exists())
                            b.Delete();
                        p.MarkAsNoLongerNeeded();
                    }
                    groundPeds.RemoveAt(i);
                }
            }

            for (int i = airPeds.Count - 1; i >= 0; i--)
            {
                Ped p = airPeds[i];
                if (p == null || !p.Exists() || !p.IsAlive)
                {
                    if (p != null && p.Exists())
                    {
                        Blip b = p.AttachedBlip;
                        if (b != null && b.Exists())
                            b.Delete();
                        p.MarkAsNoLongerNeeded();
                    }
                    airPeds.RemoveAt(i);
                }
            }

            for (int i = groundVehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = groundVehicles[i];
                if (v == null || !v.Exists() || v.IsDead)
                {
                    if (v != null && v.Exists())
                    {
                        Blip b = v.AttachedBlip;
                        if (b != null && b.Exists())
                            b.Delete();
                        v.MarkAsNoLongerNeeded();
                    }
                    groundVehicles.RemoveAt(i);
                }
            }

            for (int i = airVehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = airVehicles[i];
                if (v == null || !v.Exists() || v.IsDead)
                {
                    if (v != null && v.Exists())
                    {
                        Blip b = v.AttachedBlip;
                        if (b != null && b.Exists())
                            b.Delete();
                        v.MarkAsNoLongerNeeded();
                    }
                    airVehicles.RemoveAt(i);
                }
            }
        }

        private Ped FindNearestEnemy(
            Ped from, Ped player, float radius)
        {
            Ped[] nearby = World.GetNearbyPeds(
                from.Position, radius
            );

            Ped best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Ped p = nearby[i];
                if (p == null || !p.Exists() || !p.IsAlive)
                    continue;
                if (p == player || p == from)
                    continue;
                if (IsBackupPed(p))
                    continue;

                float dist = from.Position.DistanceTo(
                    p.Position
                );
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }

            return best;
        }

        private bool IsBackupPed(Ped ped)
        {
            for (int i = 0; i < groundPeds.Count; i++)
            {
                if (groundPeds[i] == ped)
                    return true;
            }
            for (int i = 0; i < airPeds.Count; i++)
            {
                if (airPeds[i] == ped)
                    return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            DismissAll();
            isDisposed = true;
        }
    }
}
