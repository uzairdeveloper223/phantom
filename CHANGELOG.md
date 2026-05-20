# Changelog

All notable changes to PHANTOM will be documented in this file.

## v0.2.0

### Fixed
- Airstrike explosions now use `ADD_OWNED_EXPLOSION` native with crisp `EXP_TAG_GRENADE` blasts, owned by the player, ensuring 100% reliable damage registration and kill attribution
- Radio call animation now triggers for backup, air support, extraction, and airstrike calls
- Immersive Tactical Aiming HUD & Lock-on Overlay: Replaced the old weapon-fire-to-mark system with an ultra-premium HUD overlay. When entering Kamikaze Targeting state, you now aim at any ped or vehicle and press the **`T`** key (Tactical Lock) to lock on!
- Premium Backup Ground Chase & Escort Engine: Upgraded the following ground backup convoy vehicles to a state-of-the-art chase and steering system. Teleportation threshold is increased from 40m to 150m and routes via `GET_CLOSEST_VEHICLE_NODE` to stay completely out of the player's direct line of sight. When lagging behind, they dynamically activate an engine power boost (up to 5x torque) to catch up with fast sports cars/supercars, flash their high beams, and sound their horn to clear ambient traffic. Furthermore, task reassignment is throttled to 5 seconds to eliminate AI driver steering hesitations/stuttering!
- Real-Time Tactical Stats Board: While aiming at a potential target, a beautifully styled high-tech military overlay displays in the top-right of your screen, showing the target's Class (Infantry or Vehicle), Vehicle Model Name, exact Occupant Count (real-time seat scanning), Distance in meters, and Health percentage.
- Holographic 3D Chevron: Drawing a floating holographic red/blue thick chevron in 3D space directly above the active target's head, which tracks them in real time as they move!
- Snappy Action Menu Transition: Once locked on by pressing `T`, a frontend beep triggers, the target is registered as mission-critical, and the Kamikaze attack selection menu ("Suicide Bomb", "Ram & Explode", "Execute") automatically opens, keeping the HUD locked on screen to display real-time telemetry throughout the attack.
- Persistent Target Lock: Targets of a Kamikaze attack (the ped and their vehicle if occupied) are marked as mission-critical entities (`SET_ENTITY_AS_MISSION_ENTITY`) upon targeting, preventing them from de-spawning at any distance and guaranteeing PHANTOM locks onto and pursues them across the entire map.
- Tactical Gunfight Avoidance: Backup ground units and PHANTOM automatically filter out the active Kamikaze target and their vehicle from gunfighting targeting lists, ensuring friendly units do not prematurely gun down your marked target before PHANTOM can perform the spectacular Kamikaze vehicle explosion.
- Dynamic Abort Option: The "Kamikaze" menu item dynamically transitions to "Abort Kamikaze" when an attack is underway, allowing the player to cancel the operation at any time and safely return PHANTOM to the follow group.
- Bumper-to-Bumper Collision Detonation: Upgraded suicide bomb and ram-explode attacks with high-sensitivity physical collision sensing. Instead of checking simple driver-seat model distance (which was often blocked by hood lengths), the system now checks native physical bumper contact (`Hash.IS_ENTITY_TOUCHING_ENTITY`) and a realistic vehicle-center radius (7.0m for suicide, 7.5m for ramming speed) to detonate the explosion **instantly** as soon as the vehicles touch. This uses a universally supported native that prevents Script Hook V "Can't find native 0x1E0DC1CEF9C8AD41" crash on older/differing game versions.
- Fixed Kamikaze/Ram Task-Spamming Freeze: Fixed a bug where PHANTOM would stand completely frozen/stuck in place when ordered to "Ram & Explode" or "Suicide Bomb" on foot. The tick loop was repeatedly calling the `EnterVehicle` task every 500ms before PHANTOM could even start walking to the vehicle, causing the game's task engine to continuously reset and lock PHANTOM in place. Added an `IsGettingIntoVehicle` guard to ensure the enter task is sent only once and runs smoothly to completion.
- Seamless Vehicle Retention: If PHANTOM is already sitting inside a separate vehicle (hijacked, backup unit, etc.) when a Kamikaze or Ram attack is triggered, they immediately use their current car to charge and eliminate the target, bypassing searching or stealing a new car. For safety, if PHANTOM is inside the player's active vehicle, they will exit it first to avoid detonating the player.
- Tactical Abort Retrieval & Driving Circular Motion Fix: When a Kamikaze operation is aborted, if PHANTOM is too far away from the player (> 45 meters), they will automatically hijack a nearby vehicle (or continue using their current vehicle) and drive back at high speed. Solved a critical bug where PHANTOM would drive in circles when returning close to the player. The driving task was being spammed in the game loop on every frame, resetting the AI's steering commands continuously. Added a dynamic 2.0-second task execution throttle (`lastReturnDriveTime`) to give the driving AI uninterrupted time to navigate flawlessly.
- Dynamic Menu Telemetry & Ranges: Added real-time read-only distance monitors directly inside the LemonUI main menu. "PHANTOM Range" dynamically tracks the current distance from the player to PHANTOM, and "Target Range" tracks the distance from PHANTOM to the active Kamikaze target. These items dynamically appear when PHANTOM is summoned and when an attack is underway, and disappear when offline to keep the menu interface clean.
- Tactical Foot-Based Kamikaze Bypass: If PHANTOM is ordered to execute a Kamikaze attack (e.g. Suicide Bomb) and the target vehicle is already close (within 40 meters) and has no driver or the driver is dead, PHANTOM will completely bypass finding or hijacking a vehicle. Instead, they will sprint directly to the target on foot and detonate the explosion instantly upon physical contact, saving precious time!
- Context-Aware Return Exit & Passenger Entry Fix: When driving back to the player, as soon as PHANTOM gets close (within 15 meters):
  * If the player is **on foot**, PHANTOM stops, exits the vehicle, and joins the player on foot.
  * If the player is **in a vehicle**, PHANTOM stays inside their current vehicle to follow/escort the player seamlessly.
  * **Player Car Passenger Entry:** Solved a bug where PHANTOM on foot would stand outside your car refusing to get in when you entered a vehicle. Built a dedicated companion entry handler. If you get into any vehicle and PHANTOM is on foot following you, they will immediately identify the closest empty passenger seat using `FindFreeSeat` and enter your car as a passenger, ensuring perfect co-op gameplay!
- Pre-detonation checks added to suicide and ram attacks to cancel the blast and rejoin the group if the target ped is already assassinated by the player.
- Kamikaze vehicle entry no longer locks doors before PHANTOM enters (was blocking both PHANTOM and player)
- Vehicle driving flags upgraded to full aggressive (`2883632`) with max driver ability and aggressiveness
- Parachute sync uses `GET_PED_PARACHUTE_STATE` with `FORCE_PED_TO_OPEN_PARACHUTE` for reliable deployment
- Extraction heli no longer auto-lands; waits for player flare throw to mark LZ
- Drop at waypoint no longer overridden by random flight timer (new `PickupDropping` state)
- Waypoint drop gets proper ground height via `World.GetGroundHeight`
- Drive and cruise commands stop cleanly when no waypoint is available
- Hijack falls back to Following state when no vehicle is found nearby
- Hijack times out after 15 seconds if PHANTOM can't reach a vehicle
- Chase vehicle search now excludes player's vehicle, PHANTOM's vehicle, and the target vehicle
- Backup vehicle search also excludes PHANTOM's own vehicles
- Unified, robust `PerformSeatTakeover` seat-swapping sequence to prevent physical player/PHANTOM collisions or ejections when taking the driver seat
- Waypoint arrival vehicle freezing: PHANTOM now sets the handbrake, freezes speed, and clears tasks if the player is a passenger, preventing erratic drifting
- Helicopter waypoint drops now transition to Land at Coordinate (`19`) at a distance of 150m, forcing the helicopter to descend and land on the spot
- Dynamic barrier/gate clearance: airport toll gates and security barrier props are automatically opened/cleared when PHANTOM is driving nearby
- Backup ground vehicles now utilize a hybrid physical-driving follow system that locked them to virtual connected slots behind the player. Within a 40m range, the drivers physically steer, accelerate, and brake in native real-time physics (no teleport jitter or position fights). If they ever get flipped, stuck, or fall too far behind, a rubber-band tow-bar fallback seamlessly snaps them back to their slot to ensure they never get lost.
- When the player is airborne (in a personal plane, helicopter, extraction heli, or drive-to-waypoint heli), backup ground vehicles switch to "Ground Escort Tracking" mode. They snap beautifully to the nearest road/vehicle node directly below the player's horizontal flight path and match your flight speed, acting as an elite ground escort trailing you from the streets.
- Backup units now spawn perfectly stacked in a linear queue directly behind the player's back (tightly spaced at 10m intervals, removing wide-arc offsets and facing player's heading)
- Backup peds now utilize all available seats (driver and passenger capacity) of empty backup vehicles, resolving boarding issues when seats are free
- Backup peds now dynamically target and hijack/steal nearby NPC-driven vehicles if there are no free seats in existing convoy units, dragging drivers out automatically


### Added
- Flare-based extraction landing: player receives `WEAPON_FLARE` and throws to mark exact LZ
- Extraction heli circles overhead at 40m altitude until flare is thrown
- Airstrike proximity warning: blocks airstrike if waypoint is within 30m of player or PHANTOM
- Personality dialog for all commands (Follow, Wait, Dismiss, Combat, Hijack, Extraction, Airstrike)
- Hijack timeout with "Lost it. Heading back." fallback message
- Drop at waypoint shows live distance subtitle during flight
- Chase-to-kamikaze transition: can attack chase target directly via kamikaze submenu
- Backup units board extraction heli or spawn transport helis for long-range moves
- Backup units assist kamikaze attacks (suicide sends one unit, ram/execute sends all)
- Suicide assist vehicles track and detonate near target
- `HasNearbyVehicle` pre-check prevents entering VehicleHijack state with nothing to steal
- `SET_DRIVER_ABILITY` and `SET_DRIVER_AGGRESSIVENESS` set to max for kamikaze driving
- `LockVehicleForPlayer` / `UnlockVehicleForPlayer` utility methods

### Changed
- Extraction spawn distance reduced to 100m, altitude 40m
- Backup ground vehicles use driving style `524860` (ignore traffic, go off-road)
- Kamikaze driving flags changed from `786603` to `2883621` (full aggressive, ignore all pathing)
- Parachute lerp speed increased from 5% to 8% per tick for smoother sync
- Vehicle entry for kamikaze always unlocks first, locks only after PHANTOM is seated

## v0.1.0

Initial release.

### Added
- Autonomous AI companion (PHANTOM) with full tactical behavior
- NativeUI menu system with grouped submenus
- Follow, Wait, Combat, Dismiss command set
- Vehicle hijacking with delivery-to-player system
- Ground backup deployment (foot and vehicle units)
- Air support with Buzzard attack choppers
- Extraction helicopter with flare-based landing
- Drop at waypoint with distance tracking
- Parachute synchronization between player and PHANTOM
- Airstrike marking system
- Kamikaze attack modes (Suicide, Ram, Execute)
- Chase targeting system
- Cruise mode for autonomous patrol
- Health regeneration for PHANTOM
- Configurable via PHANTOM.ini
- Drive-by combat support for PHANTOM and backup peds
