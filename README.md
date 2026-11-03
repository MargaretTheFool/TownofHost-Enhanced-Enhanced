# This readme is outdated. Currently too busy messing up around other things. Will rework on this later.

<h1 align="center">Town of Host: Enhanced (TOHE)</h1>

This branch adapts the TOHE v2.1.1 stable baseline to **Among Us 2026.9.29 (19.0)**.
Build against the target game's generated interop and installed BepInEx core before deploying:

```powershell
dotnet build TOHE.csproj -c Release -p:LocalAmongUsPath="D:\Game\20260929"
```

Start that installation with BepInEx once to generate `BepInEx/interop`. The package-only build provides static compilation references; its older interop generator does not match the tested BepInEx 6.0.0-be.755 / Il2CppInterop 1.5.1 runtime. Deploying that package-only artifact to this runtime caused a CLR crash during Harmony initialization. Use `LocalAmongUsPath` for the plugin you run.

Unity messages are forwarded to the `TOHE` BepInEx log source with timestamps by default. To turn off TOHE's copy, set `Include Unity Logs = false` under `[Logging]` in the TOHE plugin config. BepInEx's `UnityLogListening = true` is required. The release config leaves BepInEx's `WriteUnityLog` setting unchanged. The release log level filter excludes Debug messages.

The retired TOHE API and its login dependency have been removed. Gameplay migration and runtime verification are still in progress.

At startup, `TOHE-DATA/Default_Teamplate.txt` is refreshed from the built-in default. Existing `TOHE-DATA/template.txt` files are not rewritten; edit them manually to update their links. If no active template exists, the mod copies a legacy root `template.txt` or creates one from the default.

### Host-local player tags

TOHE's `TOHE-DATA/Tags/<friendcode>.txt` files have first priority for player tags and command/GM grants. The host can also use `/addtag <player id> <RRGGBB> <tag text>` to save a lower-priority appearance tag in `TOHE-DATA/tags.txt`; `/deletetag <player id>` removes that fallback in the lobby. Edit either format and use `/tags reload` to refresh a lobby. See the player-tag guide ([English](docs/player-tags.en.md) / [中文](docs/player-tags.md)) for fields, examples, and precedence.

The obsolete `TOHE-DATA/LocalPlayerTags.json`, `Moderators.txt`, and `VIP-List.txt` are no longer read or created. Existing files are not automatically migrated or deleted. Only the structured per-player TXT files grant local file-based command access or GM status; `tags.txt` is appearance-only. Host, built-in developer, and global option permissions still apply.

For adding custom sound resources, playback authority, and client-local volume controls, see the [custom sounds guide](docs/custom-sounds.md).

To prepare local release assets from a clean committed checkout, use PowerShell 7:

```powershell
./tools/Prepare-Release.ps1 -LocalAmongUsPath "D:\Game\20260929"
```

This creates a standalone `TOHE.dll`, a **Steam / Epic Games / Microsoft Store x64** package, an **itch.io x86** package, corresponding source, release notes, a manifest and SHA256 checksums under `artifacts/releases/`. Both packages pin BepInEx 755, Mini.RegionInstall 1.2.0 and the upstream Remove GC Max Time Slice 1.0.0 DLL with verified download hashes. Only official runtime files and tracked release defaults are packaged; game files, generated interop, user settings and credentials are excluded. Existing release directories are preserved; use `-OutputDirectory` to choose a new destination. This command prepares files locally without publishing a GitHub Release.

<br>

<img align="left" alt="Cover" src="Resources/Background/TOHE-Background-Old.jpg" width="50%" height="300" /> 

<p align="right">
  
> ### Regarding this mod
>
> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are the property of Innersloth LLC.
<br>

</p>
<p align="center">

<center>
<a href="https://au.niko233.top" target="_blank"><img src="https://img.shields.io/badge/Website%20-%231DA1F2.svg?&style=for-the-badge&logo=vuedotjs&logoColor=white&color=3aa675"/></a>
<a href="https://au.niko233.top/discord" target="_blank"><img src="https://img.shields.io/badge/Discord%20-%231DA1F2.svg?&style=for-the-badge&logo=discord&logoColor=white&color=5662f6"/></a>
<a href="https://github.com/Niko-s-Impostor-Server/TownofHost-Enhanced-Enhanced/releases/latest" target="_blank"><img src="https://img.shields.io/badge/Latest Version%20-%231DA1F2.svg?&style=for-the-badge&logo=github&logoColor=white&color=181717"/></a>
</center>



</p>

---

<br>

**Credit to these mods and their developers for the code and help:**
### :star: [TOHRE](https://github.com/Loonie-Toons/) 
> 
> - Town of Host: Enhanced was forked from Town of Host: Re-Edited.
>
### :star: [TOHE](https://github.com/KARPED1EM/TownOfHostEdited) :
> 
> - Town of Host: Re-Edited was forked from Town of Host: Edited. (TONX)
> 
### :star: [TOH](https://github.com/tukasa0001/TownOfHost) :
> 
> - Our repo is based initially on Town of Host.
> 
### :star: [TOHY](https://github.com/Yumenopai/TownOfHost_Y) :
> 
> - Many role ideas
> - Provided roles: AntiAdminer, CursedWolf, Workaholic, Greedier (Greedy), DarkHide (Stalker)
> - Reference: Modify game announcement
> - Ported new UI for settings (for version AU v2024.6.18)
> - Reference: Role-Basis changer fix
> 
### :star: [TOH:TOR](https://github.com/discus-sions/TownOfHost-TheOtherRoles) :
> 
> - Many role ideas
> - Reference: Exile Confirm
> - Reference: Split RPC Pack
> 
### :star: [SNR](https://github.com/ykundesu/SuperNewRoles) :
> 
> - Reference: Credentials menu
> - Reference: Switch Horse Mode
> - Reference: Search Mod Game
> - Reference: Custom Button
>
### :star: [TOP](https://github.com/tugaru1975/TownOfPlus) :
> 
> - Reference: Zoom
> 
### :star: [RHR](https://github.com/sansaaaaai/Revolutionary-host-roles) :
> 
> - Reference: Modify settings menu (Last used [1.6.0](https://github.com/0xDrMoe/TownofHost-Enhanced/releases/tag/v1.6.0))
> 
### :star: [TOH+](https://github.com/ItzLoonie/TownOfHostPlus)
>
> - Provided roles: Shapeshifter, Engineer, Scientist, Marshall, Poisoner, Necroview, Sidekick
> - Bait and Beartrap as add-ons
>
### :star: [Project: Lotus](https://github.com/Lotus-AU/LotusContinued)
>
> - Ideas for some roles (Alchemist, Crusader, Chameleon.. more)
> 
### :star: [EHR (Formerly TOHE+)](https://github.com/Gurge44/EndlessHostRoles)
>
> - Provided roles: Pyromaniac, Necromancer, Head Hunter (Huntsman), Alchemist, and others
> - Game Mode: FFA
> - Improved Role Summary
> - Dark Theme
> - Improved Role Spawns
> - Improve text box (TextBoxPatch.cs)
> - Some ideas to improve performance
### :star: [TONEX (Formerly TOHEX)](https://github.com/XtremeWave/TownOfNewEpic_Xtreme)
>
> - Provided roles: Swapper, Berserker, Randomizer and Blackmailer
> - Improve chat spam system (ChatManager.cs)
### :star: [Stellar Roles](https://github.com/Mr-Fluuff/StellarRolesAU)
>
> - Provided role: Inspector (Inspector)
### :star: [Mini.RegionInstall](https://github.com/miniduikboot/Mini.RegionInstall)
>
> - Using Mini.RegionInstall to add modded server regions
### :star: [TOU-R](https://github.com/eDonnes124/Town-Of-Us-R)
>
> - Host icon during the meeting
### :star: [MoreGamemodes](https://github.com/Rabek009/MoreGamemodes)
>
> - Changer Role Basis (RoleBasisChanger.cs)
> - Disable use vent for vanilla players (VentSystemPatch.cs)
### :star: [Reactor](https://github.com/NuclearPowered/Reactor)
>
> - Reference: Disable the 5s timeout on custom servers
---

## Licensing Information

This project is licensed under the GNU General Public License version 3.0. For more details, please refer to [LICENSE](LICENSE).

---
