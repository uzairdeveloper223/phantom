using System;
using GTA;
using GTA.Math;
using GTA.Native;

namespace PHANTOM
{
    public sealed class PhantomPed : IDisposable
    {
        private readonly ScriptSettings config;
        private PedGroup group;
        private Blip blip;
        private bool isDisposed;

        public Ped Ped { get; private set; }
        public bool IsActive => Ped != null && Ped.Exists() && Ped.IsAlive;

        public PhantomPed(ScriptSettings config)
        {
            this.config = config
                ?? throw new ArgumentNullException(nameof(config));
        }

        public bool Spawn()
        {
            if (IsActive)
                return false;

            Ped player = Game.Player.Character;
            string modelName = config.GetValue(
                "General", "PedModel", "s_m_y_blackops_01"
            );
            float spawnDistance = config.GetValue(
                "General", "SpawnDistance", 5.0f
            );

            var model = new Model(modelName);
            model.Request(5000);

            if (!model.IsLoaded)
            {
                GTA.UI.Notification.Show(
                    "~r~PHANTOM: Failed to load ped model."
                );
                return false;
            }

            Vector3 spawnPosition = player.Position
                + player.ForwardVector * spawnDistance;

            Ped = World.CreatePed(model, spawnPosition, player.Heading);
            model.MarkAsNoLongerNeeded();

            if (Ped == null || !Ped.Exists())
            {
                GTA.UI.Notification.Show(
                    "~r~PHANTOM: Failed to spawn ped."
                );
                return false;
            }

            ConfigurePed();
            ConfigureWeapons();
            ConfigureBlip();
            RejoinGroup();

            return true;
        }

        private void ConfigurePed()
        {
            int maxHealth = config.GetValue("Health", "MaxHealth", 500);
            int maxArmor = config.GetValue("Health", "MaxArmor", 200);

            Ped.MaxHealth = maxHealth;
            Ped.Health = maxHealth;
            Ped.Armor = maxArmor;

            Ped.RelationshipGroup = Game.Player.Character.RelationshipGroup;
            Ped.NeverLeavesGroup = true;
            Ped.CanBeTargetted = false;
            Ped.BlockPermanentEvents = false;

            int combatAbility = config.GetValue(
                "Behavior", "CombatAbility", 2
            );
            int combatMovement = config.GetValue(
                "Behavior", "CombatMovement", 2
            );
            int combatRange = config.GetValue(
                "Behavior", "CombatRange", 2
            );

            Function.Call(
                Hash.SET_PED_COMBAT_ABILITY, Ped.Handle, combatAbility
            );
            Function.Call(
                Hash.SET_PED_COMBAT_MOVEMENT, Ped.Handle, combatMovement
            );
            Function.Call(
                Hash.SET_PED_COMBAT_RANGE, Ped.Handle, combatRange
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES, Ped.Handle, 46, true
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES, Ped.Handle, 5, true
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES, Ped.Handle, 2, true
            );
            Function.Call(
                Hash.SET_PED_COMBAT_ATTRIBUTES, Ped.Handle, 20, true
            );
            Function.Call(Hash.SET_PED_ACCURACY, Ped.Handle, 90);
            Function.Call(
                Hash.SET_PED_FIRING_PATTERN,
                Ped.Handle,
                (uint)FiringPattern.FullAuto
            );
            Function.Call(
                Hash.SET_PED_SHOOT_RATE, Ped.Handle, 1000
            );
        }

        public void RejoinGroup()
        {
            if (Ped == null || !Ped.Exists())
                return;

            Ped player = Game.Player.Character;
            group = player.PedGroup;

            if (group == null)
            {
                group = new PedGroup();
                group.Add(player, true);
            }

            if (!Ped.IsInGroup)
            {
                group.Add(Ped, false);
            }

            int formationType = config.GetValue(
                "Behavior", "FollowFormation", 4
            );
            group.Formation = (Formation)formationType;

            float separationRange = config.GetValue(
                "Behavior", "SeparationRange", 150.0f
            );
            group.SeparationRange = separationRange;

            Ped.NeverLeavesGroup = true;
            Ped.BlockPermanentEvents = false;
        }

        public void LeaveGroupSafe()
        {
            if (Ped == null || !Ped.Exists())
                return;

            if (Ped.IsInGroup)
                Ped.LeaveGroup();
        }

        public void CleanupDeadBlip()
        {
            if (Ped == null)
                return;

            if (Ped.Exists() && Ped.IsAlive)
                return;

            if (blip != null && blip.Exists())
            {
                blip.Delete();
                blip = null;
            }
        }

        private void ConfigureWeapons()
        {
            string primary = config.GetValue(
                "Weapons", "Primary", "WEAPON_CARBINERIFLE"
            );
            string secondary = config.GetValue(
                "Weapons", "Secondary", "WEAPON_SMG"
            );
            string melee = config.GetValue(
                "Weapons", "Melee", "WEAPON_KNIFE"
            );
            int primaryAmmo = config.GetValue(
                "Weapons", "PrimaryAmmo", 9999
            );
            int secondaryAmmo = config.GetValue(
                "Weapons", "SecondaryAmmo", 9999
            );

            WeaponHash primaryHash = (WeaponHash)Game.GenerateHash(primary);
            WeaponHash secondaryHash = (WeaponHash)Game.GenerateHash(secondary);
            WeaponHash meleeHash = (WeaponHash)Game.GenerateHash(melee);

            Ped.Weapons.Give(primaryHash, primaryAmmo, true, true);
            Ped.Weapons.Give(secondaryHash, secondaryAmmo, false, true);
            Ped.Weapons.Give(meleeHash, 1, false, true);

            WeaponHash microSmg =
                (WeaponHash)Game.GenerateHash("WEAPON_MICROSMG");
            Ped.Weapons.Give(microSmg, 9999, false, true);
        }

        private void ConfigureBlip()
        {
            blip = Ped.AddBlip();

            int spriteId = config.GetValue("Blip", "Sprite", 303);
            int colorId = config.GetValue("Blip", "Color", 1);
            string blipName = config.GetValue("Blip", "Name", "PHANTOM");

            blip.Sprite = (BlipSprite)spriteId;
            blip.Color = (BlipColor)colorId;
            blip.Name = blipName;
            blip.Scale = 0.8f;
        }

        public void Dismiss()
        {
            if (Ped == null || !Ped.Exists())
                return;

            if (blip != null && blip.Exists())
            {
                blip.Delete();
                blip = null;
            }

            if (Ped.IsInGroup)
                Ped.LeaveGroup();

            Ped.Task.ClearAll();
            Ped.MarkAsNoLongerNeeded();
            Ped.Delete();
            Ped = null;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            Dismiss();

            if (group != null && group.Exists())
            {
                group.Dispose();
                group = null;
            }

            isDisposed = true;
        }
    }
}
