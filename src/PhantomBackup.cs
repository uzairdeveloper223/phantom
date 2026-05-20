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
        private const int VEHICLE_CLASS_HELICOPTER = 15;
        private const int VEHICLE_CLASS_PLANE = 16;
        private const int VEHICLE_MISSION_FOLLOW = 7;
        private const int VEHICLE_MISSION_RAM = 2;
        private const int VEHICLE_MISSION_ATTACK = 6;
        private const int HELI_MISSION_FOLLOW = 10;
        private const int HELI_MISSION_ATTACK = 6;
        private const int HELI_MISSION_LAND = 20;
        private const float CHASE_ASSIST_SPEED = 120f;
        private const float SUICIDE_ASSIST_DETONATION_RANGE = 10f;
        private const float AIR_SUICIDE_DETONATION_RANGE = 25f;
        private const float EXTRACT_SPAWN_DIST = 100f;
        private const float EXTRACT_ALTITUDE = 40f;
        private const float LAND_RANGE = 8f;
        private const float SUPPORT_VEHICLE_RANGE = 90f;
        private const float SUPPORT_ENTER_SPEED = 3f;
        private const int SUPPORT_ENTER_TIMEOUT = 8000;
        private const float TRANSPORT_HELI_ALTITUDE = 70f;
        private const int TRANSPORT_PASSENGER_SEATS = 3;

        private readonly ScriptSettings config;
        private readonly List<Ped> groundPeds = new List<Ped>();
        private readonly List<Vehicle> groundVehicles = new List<Vehicle>();
        private readonly List<Ped> airPeds = new List<Ped>();
        private readonly List<Vehicle> airVehicles = new List<Vehicle>();
        private readonly List<Ped> extractionAssignedPeds =
            new List<Ped>();
        private readonly List<Vehicle> suicideAssistVehicles =
            new List<Vehicle>();
        private Vehicle suicideAssistTarget;
        private bool isDismissing;
        private bool isDisposed;
        private int lastGroundConvoyTaskTime;

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
                float spawnDist = inVehicle
                    ? (10f + u * 8f)
                    : (6f + u * 4f);
                Vector3 spawnPos = player.GetOffsetPosition(new Vector3(0f, -spawnDist, 0f));

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
                19,
                40f,
                -1f,
                -1f,
                -1f,
                -1f,
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
            extractionAssignedPeds.Clear();

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
            suicideAssistVehicles.Clear();
            suicideAssistTarget = null;

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
            extractionAssignedPeds.Clear();

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

        public void Tick(Ped player, Ped phantom)
        {
            if (isDismissing)
                return;

            CleanupDead();
            TickSuicideAssist();

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

            Vehicle playerVeh = player.CurrentVehicle;
            Entity targetEntity = (playerVeh != null && playerVeh.Exists()) ? (Entity)playerVeh : (Entity)player;
            float targetSpeed = (playerVeh != null && playerVeh.Exists()) ? playerVeh.Speed : player.Velocity.Length();
            Vector3 targetRotation = (playerVeh != null && playerVeh.Exists()) ? playerVeh.Rotation : player.Rotation;

            bool isInAir = false;
            if (playerVeh != null && playerVeh.Exists())
            {
                int vehicleClass = Function.Call<int>(Hash.GET_VEHICLE_CLASS, playerVeh.Handle);
                if (vehicleClass == 15 || vehicleClass == 16 || playerVeh.HeightAboveGround > 15f)
                {
                    isInAir = true;
                }
            }
            else if (player.HeightAboveGround > 15f)
            {
                isInAir = true;
            }

            bool shouldReassignTask = (Game.GameTime - lastGroundConvoyTaskTime) > 5000;
            if (shouldReassignTask)
            {
                lastGroundConvoyTaskTime = Game.GameTime;
            }

            for (int i = 0; i < groundVehicles.Count; i++)
            {
                Vehicle v = groundVehicles[i];
                if (v == null || !v.Exists() || v.IsDead)
                    continue;

                Ped driver = v.Driver;
                bool hasDriver = driver != null && driver.Exists() && driver.IsAlive;


                float slotDistance = 10f + (i * 10f);

                Vector3 targetPos = Vector3.Zero;
                if (isInAir)
                {

                    targetPos = targetEntity.Position - targetEntity.ForwardVector * slotDistance;

                    OutputArgument outPos = new OutputArgument();
                    if (Function.Call<bool>(
                        Hash.GET_CLOSEST_VEHICLE_NODE,
                        targetPos.X, targetPos.Y, targetPos.Z,
                        outPos,
                        1, 3.0f, 0
                    ))
                    {
                        targetPos = outPos.GetResult<Vector3>();
                    }
                    else
                    {
                        float groundZ = World.GetGroundHeight(new Vector2(targetPos.X, targetPos.Y));
                        if (groundZ != 0f)
                        {
                            targetPos.Z = groundZ;
                        }
                        else
                        {
                            float height = (playerVeh != null && playerVeh.Exists()) ? playerVeh.HeightAboveGround : player.HeightAboveGround;
                            targetPos.Z = targetEntity.Position.Z - height;
                        }
                    }
                }
                else
                {

                    targetPos = targetEntity.GetOffsetPosition(new Vector3(0f, -slotDistance, 0f));
                }

                float dist = v.Position.DistanceTo(targetPos);



                if (dist > 150f || v.IsUpsideDown || (v.Speed < 0.1f && targetSpeed > 5f && dist > 35f))
                {

                    Vector3 spawnPos = targetPos;
                    OutputArgument outNodePos = new OutputArgument();
                    if (Function.Call<bool>(
                        Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                        targetPos.X, targetPos.Y, targetPos.Z,
                        outNodePos,
                        new OutputArgument(),
                        1, 3.0f, 0
                    ))
                    {
                        spawnPos = outNodePos.GetResult<Vector3>();
                    }

                    v.Position = spawnPos;
                    v.Rotation = isInAir ? new Vector3(0f, 0f, targetEntity.Heading) : targetRotation;
                    v.Speed = targetSpeed;
                    v.IsEngineRunning = true;
                    v.AreLightsOn = true;

                    if (hasDriver && !driver.IsInCombat)
                    {
                        driver.Task.ClearAll();
                    }
                }
                else if (hasDriver)
                {
                    v.IsEngineRunning = true;
                    v.AreLightsOn = true;


                    Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 1.0f);
                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, 1.0f);




                    if (dist > 20f)
                    {
                        float powerMultiplier = Math.Min(1.0f + (dist - 20f) / 10f, 5.0f);
                        Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, v.Handle, powerMultiplier);


                        v.AreHighBeamsOn = (Game.GameTime % 1000 < 500);
                        if (dist > 40f && Game.GameTime % 3000 < 500)
                        {
                            Function.Call(
                                Hash.START_VEHICLE_HORN,
                                v.Handle,
                                400,
                                Game.GenerateHash("HELDDOWN"),
                                false
                            );
                        }
                    }
                    else
                    {
                        Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, v.Handle, 1.0f);
                        v.AreHighBeamsOn = false;
                    }


                    if (shouldReassignTask && !driver.IsInCombat)
                    {
                        if (isInAir)
                        {

                            Function.Call(
                                Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                                driver.Handle,
                                v.Handle,
                                targetPos.X, targetPos.Y, targetPos.Z,
                                Math.Max(targetSpeed + 15f, 50f),
                                2883632,
                                5f
                            );
                        }
                        else if (playerVeh != null && playerVeh.Exists())
                        {

                            Function.Call(
                                Hash.TASK_VEHICLE_ESCORT,
                                driver.Handle,
                                v.Handle,
                                playerVeh.Handle,
                                -1,
                                Math.Max(targetSpeed + 25f, 60f),
                                2883632,
                                slotDistance,
                                0,
                                500f
                            );
                        }
                        else
                        {

                            Function.Call(
                                Hash.TASK_VEHICLE_MISSION,
                                driver.Handle,
                                v.Handle,
                                0,
                                player.Handle,
                                0f, 0f, 0f,
                                7,
                                35f,
                                2883632,
                                slotDistance,
                                -1f,
                                0
                            );
                        }
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

                Vehicle playerVehicle = player.CurrentVehicle;
                if (playerVehicle != null && playerVehicle.Exists())
                {
                    VehicleSeat seat = FindFreePassengerSeat(playerVehicle);
                    if (seat != VehicleSeat.None)
                    {
                        p.Task.EnterVehicle(
                            playerVehicle,
                            seat,
                            SUPPORT_ENTER_TIMEOUT,
                            SUPPORT_ENTER_SPEED,
                            EnterVehicleFlags.None
                        );
                        continue;
                    }
                    Vehicle freeBackupVeh = null;
                    VehicleSeat backupSeat = VehicleSeat.None;
                    for (int g = 0; g < groundVehicles.Count; g++)
                    {
                        Vehicle bv = groundVehicles[g];
                        if (bv != null && bv.Exists() && !bv.IsDead)
                        {
                            VehicleSeat fs = FindFreeSeatIncludingDriver(bv);
                            if (fs != VehicleSeat.None)
                            {
                                freeBackupVeh = bv;
                                backupSeat = fs;
                                break;
                            }
                        }
                    }

                    if (freeBackupVeh != null)
                    {
                        p.Task.EnterVehicle(
                            freeBackupVeh,
                            backupSeat,
                            SUPPORT_ENTER_TIMEOUT,
                            SUPPORT_ENTER_SPEED,
                            EnterVehicleFlags.None
                        );
                        continue;
                    }
                    Vehicle supportVehicle = FindNearestSupportVehicle(
                        p, player, phantom
                    );
                    if (supportVehicle != null)
                    {
                        TrackGroundVehicle(supportVehicle);

                        VehicleSeat targetSeat = VehicleSeat.Driver;
                        Ped supportDriver = supportVehicle.Driver;
                        if (supportDriver != null && supportDriver.Exists())
                        {
                            if (supportDriver == player || supportDriver == phantom || IsBackupPed(supportDriver))
                            {
                                targetSeat = FindFreeSeatIncludingDriver(supportVehicle);
                            }
                            else
                            {
                                targetSeat = VehicleSeat.Driver;
                            }
                        }
                        else
                        {
                            targetSeat = FindFreeSeatIncludingDriver(supportVehicle);
                        }

                        if (targetSeat != VehicleSeat.None)
                        {
                            p.Task.EnterVehicle(
                                supportVehicle,
                                targetSeat,
                                SUPPORT_ENTER_TIMEOUT,
                                SUPPORT_ENTER_SPEED,
                                EnterVehicleFlags.None
                            );
                            continue;
                        }
                    }
                }

                float dist = p.Position.DistanceTo(player.Position);
                if (dist > 25f)
                    p.Task.GoTo(player);
            }
        }

        public void BoardBackupForExtraction(Ped player)
        {
            if (!IsExtractionActive)
                return;

            for (int i = 0; i < groundPeds.Count; i++)
            {
                Ped ped = groundPeds[i];
                if (!IsAvailableFootBackup(ped))
                    continue;

                VehicleSeat seat = FindFreePassengerSeat(ExtractionHeli);
                if (seat == VehicleSeat.None)
                    break;

                ped.Task.EnterVehicle(
                    ExtractionHeli,
                    seat,
                    SUPPORT_ENTER_TIMEOUT,
                    SUPPORT_ENTER_SPEED,
                    EnterVehicleFlags.None
                );
                extractionAssignedPeds.Add(ped);
            }

            int remaining = CountAvailableFootBackup();
            if (remaining < 1)
                return;

            int requiredHelis = (int)Math.Ceiling(
                remaining / (float)TRANSPORT_PASSENGER_SEATS
            );
            int spawnedHelis = 0;

            while (spawnedHelis < requiredHelis
                && CountAvailableFootBackup() > 0)
            {
                SpawnBackupTransportHeli(player, spawnedHelis);
                spawnedHelis++;
            }
        }

        public void SupportChase(Vehicle target, Ped player)
        {
            if (target == null || !target.Exists() || target.IsDead)
                return;

            Vehicle playerVehicle = player.CurrentVehicle;

            for (int i = 0; i < groundVehicles.Count; i++)
            {
                Vehicle vehicle = groundVehicles[i];
                if (!HasLivingDriver(vehicle))
                    continue;

                Ped driver = vehicle.Driver;
                if (playerVehicle != null && playerVehicle.Exists())
                {
                    Entity escortTarget = playerVehicle;
                    if (i > 0)
                    {
                        Vehicle frontVehicle = groundVehicles[i - 1];
                        if (frontVehicle != null && frontVehicle.Exists() && !frontVehicle.IsDead)
                        {
                            escortTarget = frontVehicle;
                        }
                    }

                    Function.Call(
                        Hash.TASK_VEHICLE_MISSION,
                        driver.Handle,
                        vehicle.Handle,
                        escortTarget.Handle,
                        VEHICLE_MISSION_FOLLOW,
                        90f,
                        524860,
                        12f,
                        30f,
                        true
                    );
                }
            }

            if (!IsAirTarget(target))
                return;

            for (int i = 0; i < airVehicles.Count; i++)
            {
                CommandAirVehicle(airVehicles[i], target);
            }
        }

        public void AssistChaseTarget(
            Vehicle target, bool shouldExplode,
            bool useRamMission, bool useAllUnits)
        {
            if (target == null || !target.Exists() || target.IsDead)
                return;

            bool targetIsAir = IsAirTarget(target);
            int commanded = targetIsAir
                ? CommandAirAssists(target, shouldExplode, useAllUnits)
                : CommandGroundAssists(
                    target, shouldExplode,
                    useRamMission, useAllUnits
                );

            if (commanded < 1)
            {
                GTA.UI.Notification.Show(
                    "~y~BACKUP: No suitable unit for attack."
                );
            }
        }

        public void FlyBackupExtractionFollowers(Vector3 destination)
        {
            for (int i = 0; i < airVehicles.Count; i++)
            {
                Vehicle heli = airVehicles[i];
                if (heli == null || !heli.Exists() || heli.IsDead)
                    continue;

                Ped pilot = heli.Driver;
                if (pilot == null || !pilot.Exists()
                    || !pilot.IsAlive)
                    continue;

                Function.Call(
                    Hash.TASK_HELI_MISSION,
                    pilot.Handle,
                    heli.Handle,
                    0, 0,
                    destination.X,
                    destination.Y,
                    destination.Z + 40f,
                    HELI_MISSION_FOLLOW,
                    HELI_CRUISE_SPEED,
                    20f,
                    -1f,
                    HELI_FLIGHT_HEIGHT,
                    HELI_MIN_HEIGHT,
                    -1f,
                    0
                );
            }
        }

        public void CommandBackupAirdrop(Ped player)
        {
            for (int i = 0; i < groundPeds.Count; i++)
            {
                Ped ped = groundPeds[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive)
                    continue;

                GiveParachuteAndJump(ped);
            }

            for (int i = 0; i < airPeds.Count; i++)
            {
                Ped ped = airPeds[i];
                if (ped == null || !ped.Exists() || !ped.IsAlive)
                    continue;
                if (ped.SeatIndex == VehicleSeat.Driver)
                    continue;

                GiveParachuteAndJump(ped);
            }
        }

        public void TickBackupParachutes(Ped player)
        {
            if (!IsPlayerDropping(player))
                return;

            SyncParachuteList(groundPeds, player);
            SyncParachuteList(airPeds, player);
        }

        public void ReleaseExtractionAircraft()
        {
            extractionAssignedPeds.Clear();

            if (ExtractionPilot != null
                && ExtractionPilot.Exists())
            {
                ExtractionPilot.MarkAsNoLongerNeeded();
                ExtractionPilot = null;
            }

            if (ExtractionHeli != null
                && ExtractionHeli.Exists())
            {
                Blip blip = ExtractionHeli.AttachedBlip;
                if (blip != null && blip.Exists())
                    blip.Delete();

                ExtractionHeli.MarkAsNoLongerNeeded();
                ExtractionHeli = null;
            }
        }

        private int CommandGroundAssists(
            Vehicle target, bool shouldExplode,
            bool useRamMission, bool useAllUnits)
        {
            int commanded = 0;
            for (int i = 0; i < groundVehicles.Count; i++)
            {
                Vehicle vehicle = groundVehicles[i];
                if (!HasLivingDriver(vehicle))
                    continue;

                Ped driver = vehicle.Driver;
                Function.Call(
                    Hash.TASK_VEHICLE_MISSION,
                    driver.Handle,
                    vehicle.Handle,
                    target.Handle,
                    useRamMission
                        ? VEHICLE_MISSION_RAM
                        : VEHICLE_MISSION_ATTACK,
                    CHASE_ASSIST_SPEED,
                    524860,
                    4f,
                    80f,
                    true
                );

                commanded++;
                if (shouldExplode)
                {
                    suicideAssistTarget = target;
                    suicideAssistVehicles.Add(vehicle);
                }

                if (!useAllUnits)
                    break;
            }

            return commanded;
        }

        private int CommandAirAssists(
            Vehicle target, bool shouldExplode, bool useAllUnits)
        {
            int commanded = 0;
            for (int i = 0; i < airVehicles.Count; i++)
            {
                Vehicle vehicle = airVehicles[i];
                if (!HasLivingDriver(vehicle))
                    continue;

                CommandAirVehicle(vehicle, target);
                commanded++;

                if (shouldExplode)
                {
                    suicideAssistTarget = target;
                    suicideAssistVehicles.Add(vehicle);
                }

                if (!useAllUnits)
                    break;
            }

            return commanded;
        }

        private void CommandAirVehicle(
            Vehicle vehicle, Vehicle target)
        {
            if (!HasLivingDriver(vehicle))
                return;

            Ped pilot = vehicle.Driver;
            Function.Call(
                Hash.TASK_HELI_MISSION,
                pilot.Handle,
                vehicle.Handle,
                target.Handle,
                0,
                0f, 0f, 0f,
                HELI_MISSION_ATTACK,
                CHASE_ASSIST_SPEED,
                30f,
                -1f,
                HELI_FLIGHT_HEIGHT,
                HELI_MIN_HEIGHT,
                -1f,
                0
            );
        }

        private void TickSuicideAssist()
        {
            if (suicideAssistTarget == null
                || !suicideAssistTarget.Exists()
                || suicideAssistTarget.IsDead)
            {
                suicideAssistVehicles.Clear();
                suicideAssistTarget = null;
                return;
            }

            for (int i = suicideAssistVehicles.Count - 1;
                i >= 0; i--)
            {
                Vehicle vehicle = suicideAssistVehicles[i];
                if (vehicle == null || !vehicle.Exists()
                    || vehicle.IsDead)
                {
                    suicideAssistVehicles.RemoveAt(i);
                    continue;
                }

                float range = IsAirTarget(suicideAssistTarget)
                    ? AIR_SUICIDE_DETONATION_RANGE
                    : SUICIDE_ASSIST_DETONATION_RANGE;
                float dist = vehicle.Position.DistanceTo(
                    suicideAssistTarget.Position
                );
                if (dist > range)
                    continue;

                World.AddExplosion(
                    suicideAssistTarget.Position,
                    ExplosionType.Car,
                    15f,
                    2f
                );
                vehicle.Explode();
                suicideAssistVehicles.RemoveAt(i);
            }
        }

        private static bool HasLivingDriver(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists()
                || vehicle.IsDead)
                return false;

            Ped driver = vehicle.Driver;
            return driver != null
                && driver.Exists()
                && driver.IsAlive;
        }

        private static bool IsAirTarget(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return false;

            int vehicleClass = Function.Call<int>(
                Hash.GET_VEHICLE_CLASS,
                vehicle.Handle
            );
            return vehicleClass == VEHICLE_CLASS_HELICOPTER
                || vehicleClass == VEHICLE_CLASS_PLANE
                || vehicle.HeightAboveGround > 20f;
        }

        private void SpawnVehicleUnit(
            Vector3 spawnPos, Ped player, Model pedModel,
            Model vehicleModel, string primary, string secondary)
        {
            Vehicle vehicle = World.CreateVehicle(
                vehicleModel, spawnPos,
                player.Heading
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

        private void SpawnBackupTransportHeli(
            Ped player, int index)
        {
            string pedModel = config.GetValue(
                "General", "PedModel", "s_m_y_blackops_01"
            );

            Model pedMdl = new Model(pedModel);
            pedMdl.Request(5000);
            if (!pedMdl.IsLoaded)
                return;

            Model heliMdl = new Model("buzzard2");
            heliMdl.Request(5000);
            if (!heliMdl.IsLoaded)
            {
                pedMdl.MarkAsNoLongerNeeded();
                return;
            }

            Vector3 spawnPos = player.Position
                + player.RightVector * (35f + index * 25f)
                - player.ForwardVector * 70f;
            spawnPos.Z += TRANSPORT_HELI_ALTITUDE;

            Vehicle heli = World.CreateVehicle(
                heliMdl, spawnPos, player.Heading
            );
            if (heli == null || !heli.Exists())
            {
                pedMdl.MarkAsNoLongerNeeded();
                heliMdl.MarkAsNoLongerNeeded();
                return;
            }

            heli.IsEngineRunning = true;
            airVehicles.Add(heli);

            Blip heliBlip = heli.AddBlip();
            heliBlip.Sprite = (BlipSprite)BLIP_SPRITE_AIR;
            heliBlip.Color = (BlipColor)BLIP_COLOR_BACKUP;
            heliBlip.Name = "PHANTOM TRANSPORT";
            heliBlip.Scale = 0.7f;

            Ped pilot = World.CreatePed(
                pedMdl, spawnPos, player.Heading
            );
            if (pilot != null && pilot.Exists())
            {
                ConfigureBackupPed(
                    pilot, player, "WEAPON_PISTOL", null
                );
                pilot.SetIntoVehicle(heli, VehicleSeat.Driver);
                airPeds.Add(pilot);
            }

            int boarded = 0;
            for (int i = 0; i < groundPeds.Count
                && boarded < TRANSPORT_PASSENGER_SEATS; i++)
            {
                Ped ped = groundPeds[i];
                if (!IsAvailableFootBackup(ped))
                    continue;

                VehicleSeat seat = (VehicleSeat)boarded;
                if (!heli.IsSeatFree(seat))
                    continue;

                ped.Task.EnterVehicle(
                    heli,
                    seat,
                    SUPPORT_ENTER_TIMEOUT,
                    SUPPORT_ENTER_SPEED,
                    EnterVehicleFlags.None
                );
                extractionAssignedPeds.Add(ped);
                boarded++;
            }

            if (pilot != null && pilot.Exists())
            {
                Vector3 destination = IsExtractionActive
                    ? ExtractionHeli.Position
                    : player.Position;
                Function.Call(
                    Hash.TASK_HELI_MISSION,
                    pilot.Handle,
                    heli.Handle,
                    0, 0,
                    destination.X,
                    destination.Y,
                    destination.Z + 40f,
                    HELI_MISSION_FOLLOW,
                    HELI_CRUISE_SPEED,
                    20f,
                    -1f,
                    HELI_FLIGHT_HEIGHT,
                    HELI_MIN_HEIGHT,
                    -1f,
                    0
                );
            }

            pedMdl.MarkAsNoLongerNeeded();
            heliMdl.MarkAsNoLongerNeeded();
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

        private static VehicleSeat FindFreePassengerSeat(
            Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return VehicleSeat.None;

            for (int seat = 0;
                seat < vehicle.PassengerCapacity; seat++)
            {
                VehicleSeat vehicleSeat = (VehicleSeat)seat;
                if (vehicle.IsSeatFree(vehicleSeat))
                    return vehicleSeat;
            }

            return VehicleSeat.None;
        }

        public static VehicleSeat FindFreeSeatIncludingDriver(Vehicle v)
        {
            if (v == null || !v.Exists())
                return VehicleSeat.None;

            if (v.IsSeatFree(VehicleSeat.Driver))
                return VehicleSeat.Driver;
            if (v.IsSeatFree(VehicleSeat.Passenger))
                return VehicleSeat.Passenger;
            if (v.IsSeatFree(VehicleSeat.LeftRear))
                return VehicleSeat.LeftRear;
            if (v.IsSeatFree(VehicleSeat.RightRear))
                return VehicleSeat.RightRear;

            return VehicleSeat.None;
        }

        private Vehicle FindNearestSupportVehicle(
            Ped ped, Ped player, Ped phantom)
        {
            Vehicle[] nearby = World.GetNearbyVehicles(
                ped.Position, SUPPORT_VEHICLE_RANGE
            );

            Vehicle playerVehicle = player.CurrentVehicle;
            Vehicle playerLastVehicle = player.LastVehicle;
            Vehicle phantomVehicle = phantom?.CurrentVehicle;
            Vehicle phantomLastVehicle = phantom?.LastVehicle;

            Vehicle closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle vehicle = nearby[i];
                if (vehicle == null || !vehicle.Exists()
                    || vehicle.IsDead)
                    continue;
                if (vehicle == playerVehicle
                    || vehicle == playerLastVehicle)
                    continue;
                if (vehicle == phantomVehicle
                    || vehicle == phantomLastVehicle)
                    continue;

                Ped driver = vehicle.Driver;
                if (driver != null && driver.Exists())
                {
                    if (driver == player || driver == phantom || IsBackupPed(driver))
                        continue;
                }

                float dist = ped.Position.DistanceTo(
                    vehicle.Position
                );
                if (dist >= closestDist)
                    continue;

                closestDist = dist;
                closest = vehicle;
            }

            return closest;
        }

        private void TrackGroundVehicle(Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists())
                return;

            for (int i = 0; i < groundVehicles.Count; i++)
            {
                if (groundVehicles[i] == vehicle)
                    return;
            }

            vehicle.IsEngineRunning = true;
            groundVehicles.Add(vehicle);
        }

        private int CountAvailableFootBackup()
        {
            int count = 0;
            for (int i = 0; i < groundPeds.Count; i++)
            {
                if (IsAvailableFootBackup(groundPeds[i]))
                    count++;
            }
            return count;
        }

        private bool IsAvailableFootBackup(Ped ped)
        {
            return ped != null
                && ped.Exists()
                && ped.IsAlive
                && !ped.IsInVehicle()
                && !ped.IsInCombat
                && !extractionAssignedPeds.Contains(ped);
        }

        private static void GiveParachuteAndJump(Ped ped)
        {
            if (!ped.Weapons.HasWeapon(WeaponHash.Parachute))
            {
                ped.Weapons.Give(
                    WeaponHash.Parachute, 1, false, true
                );
            }

            if (ped.IsInVehicle())
            {
                ped.Task.LeaveVehicle(LeaveVehicleFlags.BailOut);
                return;
            }

            ped.Task.Skydive();
        }

        private static bool IsPlayerDropping(Ped player)
        {
            if (player == null || !player.Exists())
                return false;

            return player.IsInAir && player.HeightAboveGround > 12f;
        }

        private static void SyncParachuteList(
            List<Ped> peds, Ped player)
        {
            int playerState = Function.Call<int>(
                Hash.GET_PED_PARACHUTE_STATE,
                player.Handle
            );

            for (int i = 0; i < peds.Count; i++)
            {
                Ped ped = peds[i];
                if (ped == null || !ped.Exists()
                    || !ped.IsAlive)
                    continue;
                if (ped.SeatIndex == VehicleSeat.Driver)
                    continue;

                if (!ped.Weapons.HasWeapon(WeaponHash.Parachute))
                {
                    ped.Weapons.Give(
                        WeaponHash.Parachute, 1, false, true
                    );
                }

                int pedState = Function.Call<int>(
                    Hash.GET_PED_PARACHUTE_STATE,
                    ped.Handle
                );

                if (ped.IsInVehicle())
                {
                    ped.Task.LeaveVehicle(
                        LeaveVehicleFlags.BailOut
                    );
                    continue;
                }

                if (pedState < 0)
                    ped.Task.Skydive();

                if (playerState >= 1 && pedState <= 0)
                    ped.Task.UseParachute();

                if (playerState == 2 && pedState < 2)
                    ped.OpenParachute();
            }
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


                if (PhantomMain.kamikazeTarget != null && PhantomMain.kamikazeTarget.Exists())
                {
                    if (p == PhantomMain.kamikazeTarget)
                        continue;
                    if (p.CurrentVehicle == PhantomMain.kamikazeTarget)
                        continue;
                }

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
