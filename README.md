<p align="center">
  <img src="assets/svg/banner.svg" alt="PHANTOM" width="100%"/>
</p>

<p align="center">
  <strong>Autonomous tactical NPC operator mod for Grand Theft Auto V</strong>
</p>

<p align="center">
  <a href="https://github.com/uzairdeveloper223/phantom/releases/latest"><img src="https://img.shields.io/github/v/release/uzairdeveloper223/phantom?style=flat-square&color=00ff88&label=release" alt="Release"/></a>
  <a href="https://github.com/uzairdeveloper223/phantom/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-00ff88?style=flat-square" alt="License"/></a>
  <img src="https://img.shields.io/badge/.NET_Framework-4.8-blue?style=flat-square" alt=".NET"/>
  <img src="https://img.shields.io/badge/SHVDN-3.3.2-blue?style=flat-square" alt="SHVDN"/>
  <img src="https://img.shields.io/badge/platform-x64-444?style=flat-square" alt="Platform"/>
</p>

---

## About

**PHANTOM** is a Script Hook V .NET mod that deploys an autonomous AI companion into GTA V. The companion operates as a fully independent tactical unit: following the player, engaging hostiles, hijacking vehicles on command, calling in backup squads, coordinating extraction helicopters, and synchronizing parachute drops.

One call. One ghost. Zero witnesses.

---

## Features

### Core Companion
- Persistent AI companion with configurable ped model and loadout
- Follow / Wait / Dismiss command system
- Automatic combat engagement with configurable weapon set
- Health regeneration system
- Drive-by shooting from vehicles

### Vehicle Operations
- **Hijack** -- PHANTOM steals the nearest vehicle and delivers it to the player
- **Cruise** -- Autonomous patrol driving
- **Drive To** -- Navigate to a specified destination
- Player boards hijacked vehicles with `F` key proximity prompt

### Backup Deployment
- Ground backup: foot squads and vehicle convoys (up to 4 units)
- Air support: Buzzard attack helicopters with armed gunners
- Backup vehicles ignore traffic rules for aggressive pursuit
- Auto-backup when player health is critical

### Extraction System
- **Flare-based landing** -- Throw a flare to mark your landing zone
- Annihilator extraction helicopter circles overhead until LZ is marked
- Heli lands precisely on the flare position
- Press `F` to board when within range
- **Drop at waypoint** -- Set a map waypoint and fly there with distance tracking
- Pilot is invincible with max driving ability

### Tactical Operations
- **Airstrike** -- Mark a ground target for aerial bombardment
- **Kamikaze** -- Suicide, Ram, and Execute attack modes
- **Chase** -- Lock onto and pursue a target vehicle

### Parachute Sync
- PHANTOM auto-deploys parachute when the player does
- Position synchronized 4m to the right during descent
- Smooth lerped movement for cinematic freefall

---

## Screenshots

> Screenshots will be added here. Place your screenshots in `assets/images/` and reference them below.

<!--
<p align="center">
  <img src="assets/images/screenshot_01.png" width="45%"/>
  <img src="assets/images/screenshot_02.png" width="45%"/>
</p>
<p align="center">
  <img src="assets/images/screenshot_03.png" width="45%"/>
  <img src="assets/images/screenshot_04.png" width="45%"/>
</p>
-->

---

## Installation

### Requirements

| Dependency | Version | Link |
|---|---|---|
| GTA V | Latest | -- |
| Script Hook V | Latest | [Download](http://www.dev-c.com/gtav/scripthookv/) |
| Script Hook V .NET | 3.x | [Download](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) |
| .NET Framework | 4.8 | [Download](https://dotnet.microsoft.com/download/dotnet-framework/net48) |
| LemonUI | 1.5+ | Bundled with SHVDN |

### Steps

1. Install **Script Hook V** and **Script Hook V .NET** into your GTA V root directory.
2. Download the latest release from the [Releases](https://github.com/uzairdeveloper223/phantom/releases) page.
3. Extract the archive. Copy `PHANTOM.dll` and `PHANTOM.ini` into your `GTA V/scripts/` folder.
4. Launch GTA V.
5. Press `F10` to open the PHANTOM menu.

### Directory Structure

```
Grand Theft Auto V/
  scripts/
    PHANTOM.dll
    PHANTOM.ini
```

---

## Configuration

All settings are in `PHANTOM.ini`, placed alongside the DLL:

```ini
[General]
PedModel=s_m_y_blackops_01
ActivationKey=F10

[Weapons]
Primary=WEAPON_CARBINERIFLE
Secondary=WEAPON_PISTOL

[Driving]
DrivingSpeed=80
DrivingFlags=786603

[Health]
HealthRegenAmount=5
HealthRegenInterval=3000
HealthRegenDelay=5000
```

Refer to the [GTA V Native Reference](https://nativedb.dotindustries.dev/natives) for valid model and weapon hash names.

---

## Building from Source

> Most users do not need to build from source. Pre-built releases are available as zip archives on the [Releases](https://github.com/uzairdeveloper223/phantom/releases) tab. Download, extract, and copy the contents to your `scripts/` folder.

### Requirements

- .NET SDK 8.0+
- NuGet packages restore automatically

### Build

```bash
git clone https://github.com/uzairdeveloper223/phantom.git
cd phantom
dotnet restore
dotnet build
```

The output DLL is written to the path configured in `PHANTOM.csproj` (`OutputPath`). For development, this points to your local GTA V scripts folder.

### Release Build

```bash
dotnet build -c Release
```

---

## Architecture

```
src/
  PhantomMain.cs      Main script entry, state machine, tick handlers
  PhantomPed.cs       Companion ped lifecycle and configuration
  PhantomTasks.cs     AI task logic (hijack, combat, driving)
  PhantomBackup.cs    Backup squads, air support, extraction heli
  PhantomMenu.cs      LemonUI menu system and user input
  PhantomState.cs     State enum (Following, Combat, Hijack, etc.)
```

| Component | Responsibility |
|---|---|
| `PhantomMain` | Central state machine. Routes each tick to the correct handler based on `PhantomState`. |
| `PhantomPed` | Spawns, configures, and manages the companion ped lifecycle. Handles group membership. |
| `PhantomTasks` | Encapsulates AI task logic: hijacking, vehicle search, combat targeting, radio animations. |
| `PhantomBackup` | Manages backup squads (ground/air), extraction heli spawning, pilot AI, and cleanup. |
| `PhantomMenu` | Builds the NativeUI menu tree and dispatches user selections to handlers. |
| `PhantomState` | Defines all possible companion states for the state machine. |

---

## References

| Resource | URL |
|---|---|
| Script Hook V | [dev-c.com/gtav/scripthookv](http://www.dev-c.com/gtav/scripthookv/) |
| Script Hook V .NET | [github.com/scripthookvdotnet](https://github.com/scripthookvdotnet/scripthookvdotnet) |
| GTA V Native DB | [nativedb.dotindustries.dev](https://nativedb.dotindustries.dev/natives) |
| LemonUI | [github.com/LemonUIbyLemon](https://github.com/LemonUIbyLemon/LemonUI) |
| SHVDN3 API | [scripthookvdotnet docs](https://scripthookvdotnet.github.io/) |

---

## Known Bugs

The following issues are present in the current release and may be addressed in future updates:

- Airstrike explosion occurs but does not cause damage or produce a visible explosion effect
- No radio call animation plays when calling for air support or airstrike
- Kamikaze suicide explosion does not work properly
- Kamikaze modes (Ram, Execute) are not functioning correctly
- PHANTOM walks in circles when idle with no active command
- Additional bugs may exist and will be cataloged as they are discovered

---

## License

This project is licensed under the [MIT License](LICENSE).

---

## Author

**Uzair Mughal**

- GitHub: [uzairdeveloper223](https://github.com/uzairdeveloper223)

---

<p align="center">
  <sub>Built with Script Hook V .NET 3 and native GTA V APIs.</sub>
</p>
