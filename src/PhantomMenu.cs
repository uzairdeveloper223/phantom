using System;
using LemonUI;
using LemonUI.Menus;

namespace PHANTOM
{
    public sealed class PhantomMenu
    {
        private readonly ObjectPool pool;
        private readonly NativeMenu mainMenu;
        private readonly NativeMenu kamikazeMenu;
        private readonly NativeMenu backupMenu;
        private readonly NativeMenu airSupportMenu;

        private readonly NativeItem summonItem;
        private readonly NativeItem dismissItem;
        private readonly NativeItem followItem;
        private readonly NativeItem waitItem;
        private readonly NativeItem combatItem;
        private readonly NativeItem vehicleHijackItem;
        private readonly NativeItem driveToItem;
        private readonly NativeItem kamikazeItem;
        private readonly NativeItem callBackupItem;
        private readonly NativeItem dismissBackupItem;
        private readonly NativeItem airSupportItem;
        private readonly NativeItem chaseItem;
        private readonly NativeItem cruiseItem;
        private readonly NativeItem airstrikeItem;

        private readonly NativeItem attackChoppersItem;
        private readonly NativeItem extractionHeliItem;
        private readonly NativeItem dropAtWaypointItem;
        private readonly NativeItem cancelAirItem;

        private readonly NativeItem suicideBombItem;
        private readonly NativeItem ramExplodeItem;
        private readonly NativeItem executeItem;
        private readonly NativeItem cancelKamikazeItem;

        private readonly NativeListItem<int> unitCountItem;
        private readonly NativeCheckboxItem inVehicleItem;
        private readonly NativeItem deployBackupItem;
        private readonly NativeItem cancelBackupItem;

        private readonly NativeItem distFromPlayerItem;
        private readonly NativeItem distFromPhantomItem;

        public event Action OnSummon;
        public event Action OnDismiss;
        public event Action OnFollow;
        public event Action OnWait;
        public event Action OnCombat;
        public event Action OnVehicleHijack;
        public event Action OnDriveTo;
        public event Action OnKamikazeStart;
        public event Action OnSuicideBomb;
        public event Action OnRamExplode;
        public event Action OnExecuteTarget;
        public event Action OnCancelKamikaze;
        public event Action<int, bool> OnDeployBackup;
        public event Action OnDismissBackup;
        public event Action OnAirSupport;
        public event Action OnExtraction;
        public event Action OnDropAtWaypoint;
        public event Action OnChase;
        public event Action OnCruise;
        public event Action OnAirstrike;

        public PhantomMenu()
        {
            pool = new ObjectPool();

            mainMenu = new NativeMenu(
                "PHANTOM", "TACTICAL OPERATOR",
                "Select a command."
            );

            summonItem = new NativeItem(
                "Summon", "Deploy PHANTOM."
            );
            dismissItem = new NativeItem(
                "Dismiss", "Send PHANTOM away."
            );
            followItem = new NativeItem(
                "Follow", "PHANTOM follows you."
            );
            waitItem = new NativeItem(
                "Hold Position", "Guard current location."
            );
            combatItem = new NativeItem(
                "Combat Mode", "Engage all hostiles."
            );
            vehicleHijackItem = new NativeItem(
                "Vehicle Hijack", "Commandeer nearest vehicle."
            );
            driveToItem = new NativeItem(
                "Drive To Waypoint", "Drive to your waypoint."
            );
            kamikazeItem = new NativeItem(
                "Kamikaze", "Mark target, choose attack."
            );
            callBackupItem = new NativeItem(
                "Call Backup", "Request ground reinforcements."
            );
            dismissBackupItem = new NativeItem(
                "Dismiss Backup", "Send all backup away."
            );
            airSupportItem = new NativeItem(
                "Air Support",
                "Call Buzzard attack choppers."
            );
            chaseItem = new NativeItem(
                "Chase Vehicle",
                "Mark a vehicle, PHANTOM chases it."
            );
            cruiseItem = new NativeItem(
                "Cruise",
                "PHANTOM drives to waypoint (traffic rules)."
            );
            airstrikeItem = new NativeItem(
                "Airstrike",
                "Mark location for fighter jet bombing run."
            );

            summonItem.Activated += (s, e) =>
                OnSummon?.Invoke();
            dismissItem.Activated += (s, e) =>
                OnDismiss?.Invoke();
            followItem.Activated += (s, e) =>
                OnFollow?.Invoke();
            waitItem.Activated += (s, e) =>
                OnWait?.Invoke();
            combatItem.Activated += (s, e) =>
                OnCombat?.Invoke();
            vehicleHijackItem.Activated += (s, e) =>
                OnVehicleHijack?.Invoke();
            driveToItem.Activated += (s, e) =>
                OnDriveTo?.Invoke();
            kamikazeItem.Activated += (s, e) =>
            {
                mainMenu.Visible = false;
                OnKamikazeStart?.Invoke();
            };
            callBackupItem.Activated += (s, e) =>
            {
                mainMenu.Visible = false;
                backupMenu.Visible = true;
            };
            dismissBackupItem.Activated += (s, e) =>
                OnDismissBackup?.Invoke();
            airSupportItem.Activated += (s, e) =>
            {
                mainMenu.Visible = false;
                airSupportMenu.Visible = true;
            };
            chaseItem.Activated += (s, e) =>
            {
                mainMenu.Visible = false;
                OnChase?.Invoke();
            };
            cruiseItem.Activated += (s, e) =>
                OnCruise?.Invoke();
            airstrikeItem.Activated += (s, e) =>
            {
                mainMenu.Visible = false;
                OnAirstrike?.Invoke();
            };

            mainMenu.Add(summonItem);
            mainMenu.Add(dismissItem);
            mainMenu.Add(followItem);
            mainMenu.Add(waitItem);
            mainMenu.Add(combatItem);
            mainMenu.Add(vehicleHijackItem);
            mainMenu.Add(driveToItem);
            mainMenu.Add(kamikazeItem);
            mainMenu.Add(chaseItem);
            mainMenu.Add(cruiseItem);
            mainMenu.Add(airstrikeItem);
            mainMenu.Add(callBackupItem);
            mainMenu.Add(airSupportItem);
            mainMenu.Add(dismissBackupItem);

            distFromPlayerItem = new NativeItem(
                "PHANTOM Range: --", "Distance from player."
            );
            distFromPhantomItem = new NativeItem(
                "Target Range: --", "Distance from PHANTOM to target."
            );
            distFromPlayerItem.Enabled = false;
            distFromPhantomItem.Enabled = false;

            kamikazeMenu = new NativeMenu(
                "PHANTOM", "ATTACK OPTIONS",
                "Choose how to eliminate."
            );

            suicideBombItem = new NativeItem(
                "Suicide Bomb",
                "Run to target and detonate."
            );
            ramExplodeItem = new NativeItem(
                "Ram & Explode",
                "Ram target vehicle at full speed."
            );
            executeItem = new NativeItem(
                "Execute",
                "Approach and eliminate the target."
            );
            cancelKamikazeItem = new NativeItem(
                "Cancel", "Abort kamikaze operation."
            );

            suicideBombItem.Activated += (s, e) =>
            {
                kamikazeMenu.Visible = false;
                OnSuicideBomb?.Invoke();
            };
            ramExplodeItem.Activated += (s, e) =>
            {
                kamikazeMenu.Visible = false;
                OnRamExplode?.Invoke();
            };
            executeItem.Activated += (s, e) =>
            {
                kamikazeMenu.Visible = false;
                OnExecuteTarget?.Invoke();
            };
            cancelKamikazeItem.Activated += (s, e) =>
            {
                kamikazeMenu.Visible = false;
                OnCancelKamikaze?.Invoke();
            };

            kamikazeMenu.Add(suicideBombItem);
            kamikazeMenu.Add(ramExplodeItem);
            kamikazeMenu.Add(executeItem);
            kamikazeMenu.Add(cancelKamikazeItem);

            backupMenu = new NativeMenu(
                "PHANTOM", "CALL BACKUP",
                "Configure reinforcements."
            );

            unitCountItem = new NativeListItem<int>(
                "Units (5 per unit)",
                "How many units to deploy.",
                1, 2, 3, 4
            );
            inVehicleItem = new NativeCheckboxItem(
                "Arrive in Bison",
                "Units arrive in Bravado Bison.",
                true
            );
            deployBackupItem = new NativeItem(
                "Deploy", "Send backup now."
            );
            cancelBackupItem = new NativeItem(
                "Cancel", "Abort."
            );

            deployBackupItem.Activated += (s, e) =>
            {
                backupMenu.Visible = false;
                OnDeployBackup?.Invoke(
                    unitCountItem.SelectedItem,
                    inVehicleItem.Checked
                );
            };
            cancelBackupItem.Activated += (s, e) =>
            {
                backupMenu.Visible = false;
            };

            backupMenu.Add(unitCountItem);
            backupMenu.Add(inVehicleItem);
            backupMenu.Add(deployBackupItem);
            backupMenu.Add(cancelBackupItem);

            airSupportMenu = new NativeMenu(
                "PHANTOM", "AIR SUPPORT",
                "Choose air operation."
            );

            attackChoppersItem = new NativeItem(
                "Attack Choppers",
                "Deploy 2 Annihilators with gunners."
            );
            extractionHeliItem = new NativeItem(
                "Extraction Heli",
                "Pickup heli lands and extracts you."
            );
            dropAtWaypointItem = new NativeItem(
                "Drop at Waypoint",
                "Fly to waypoint and land for drop-off."
            );
            cancelAirItem = new NativeItem(
                "Cancel", "Close menu."
            );

            attackChoppersItem.Activated += (s, e) =>
            {
                airSupportMenu.Visible = false;
                OnAirSupport?.Invoke();
            };
            extractionHeliItem.Activated += (s, e) =>
            {
                airSupportMenu.Visible = false;
                OnExtraction?.Invoke();
            };
            dropAtWaypointItem.Activated += (s, e) =>
            {
                airSupportMenu.Visible = false;
                OnDropAtWaypoint?.Invoke();
            };
            cancelAirItem.Activated += (s, e) =>
            {
                airSupportMenu.Visible = false;
            };

            airSupportMenu.Add(attackChoppersItem);
            airSupportMenu.Add(extractionHeliItem);
            airSupportMenu.Add(dropAtWaypointItem);
            airSupportMenu.Add(cancelAirItem);

            pool.Add(mainMenu);
            pool.Add(kamikazeMenu);
            pool.Add(backupMenu);
            pool.Add(airSupportMenu);
        }

        public void Toggle()
        {
            mainMenu.Visible = !mainMenu.Visible;
        }

        public void Process()
        {
            pool.Process();
        }

        public void ShowKamikazeOptions(bool targetIsVehicle)
        {
            suicideBombItem.Enabled = true;
            executeItem.Enabled = true;
            ramExplodeItem.Enabled = targetIsVehicle;
            kamikazeMenu.Visible = true;
        }

        public void HideAll()
        {
            mainMenu.Visible = false;
            kamikazeMenu.Visible = false;
            backupMenu.Visible = false;
            airSupportMenu.Visible = false;
        }

        public void UpdateItemStates(bool isPhantomActive, bool isKamikazeActive, float distFromPlayer, float distFromPhantom)
        {
            summonItem.Enabled = !isPhantomActive;
            dismissItem.Enabled = isPhantomActive;
            followItem.Enabled = isPhantomActive;
            waitItem.Enabled = isPhantomActive;
            combatItem.Enabled = isPhantomActive;
            vehicleHijackItem.Enabled = isPhantomActive;
            driveToItem.Enabled = isPhantomActive;
            kamikazeItem.Enabled = isPhantomActive;
            chaseItem.Enabled = isPhantomActive;
            cruiseItem.Enabled = isPhantomActive;
            airstrikeItem.Enabled = isPhantomActive;
            callBackupItem.Enabled = isPhantomActive;
            dismissBackupItem.Enabled = isPhantomActive;
            airSupportItem.Enabled = isPhantomActive;

            if (isKamikazeActive)
            {
                kamikazeItem.Title = "Abort Kamikaze";
                kamikazeItem.Description = "Cancel the active Kamikaze attack and rejoin group.";
            }
            else
            {
                kamikazeItem.Title = "Kamikaze";
                kamikazeItem.Description = "Mark target, choose attack.";
            }

            if (isPhantomActive && distFromPlayer >= 0f)
            {
                if (!mainMenu.Items.Contains(distFromPlayerItem))
                {
                    mainMenu.Add(distFromPlayerItem);
                }
                distFromPlayerItem.Title = $"PHANTOM Range: {distFromPlayer:F1}m";
            }
            else
            {
                if (mainMenu.Items.Contains(distFromPlayerItem))
                {
                    mainMenu.Remove(distFromPlayerItem);
                }
            }

            if (isPhantomActive && isKamikazeActive && distFromPhantom >= 0f)
            {
                if (!mainMenu.Items.Contains(distFromPhantomItem))
                {
                    mainMenu.Add(distFromPhantomItem);
                }
                distFromPhantomItem.Title = $"Target Range: {distFromPhantom:F1}m";
            }
            else
            {
                if (mainMenu.Items.Contains(distFromPhantomItem))
                {
                    mainMenu.Remove(distFromPhantomItem);
                }
            }
        }
    }
}
