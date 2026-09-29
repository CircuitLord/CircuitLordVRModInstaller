<p align="center">
  <img src="bigwalkvr_icon.png" alt="Big Walk VR" width="160">
</p>

<h1 align="center">CircuitLord's VR Mod Installer</h1>

### Join the [Discord](https://discord.gg/MTKwud2cCP) if you have questions or feedback!

Hey, I'm CircuitLord! This is a utility to automatically install my full-conversion VR mods, keep them up-to-date, and launch them in VR. It currently supports:
- Big Walk VR
- Titanfall 2 VR

These mods and this installer are community projects, not affiliated with or endorsed by the game developers or publishers. Use at your own risk.

**[Download the installer](https://github.com/CircuitLord/CircuitLordVRModInstaller/releases/latest/download/CircuitLordVRModInstaller.exe)**


## Titanfall 2 VR

Titanfall 2 VR adds full VR support to the Titanfall 2 campaign. It also includes stereo rendering, full body IK, manual reloads, Titan controls, and more!

### What the installer does

1. **Finds Titanfall 2** through your Steam install.
2. **Checks the EA app**, which Titanfall 2 needs you signed into to play. If it's missing, launch Titanfall 2 once from Steam to install it.
3. **Installs Northstar and the mod** into a separate `TF2VR` profile in your game directory.

Launching Titanfall 2 normally through Steam stays unmodded. To play in VR, start SteamVR and use the installer's Launch in VR button.

### Campaign saves

VR launches keep their own saves and settings in `Documents\Respawn\Titanfall2_VR`, so your regular campaign stays untouched. During install you can copy your existing campaign progress over or start fresh. Use the installer's Campaign saves button to check your saves or copy your progress later.

## Big Walk VR

Big Walk VR adds full multiplayer-compatible SteamVR support to the game Big Walk by House House. It includes stereo rendering support, full 6dof motion controls with support for grabbing and throwing objects, and more!

### Do other players need the mod?
The **host and other players** need the mod installed to **see your VR hands**.

Your non-vr friends can install the mod and still play in flatscreen!

### What the installer does

1. **Finds Big Walk** through your Steam install.
2. **Sets up BepInEx**, the mod loader Big Walk VR depends on.
3. **Installs the mod** into your game directory.

Launching Big Walk normally through Steam stays non-VR while showing VR players' tracked movement. To play in VR, start SteamVR and use the installer's Launch in VR button.


## Building from source

Needs the .NET Framework 4.8 SDK.

```
dotnet build src/Installer -c Release
```

Output is a single `src/Installer/bin/Release/net48/CircuitLordVRModInstaller.exe` using only .NET Framework assemblies.

## How it works

`manifest-v2.json` lists the installer version, BepInEx build, and available BepInEx mods with their download URLs and SHA-256 hashes. The app requires schema version 2, compares it against what is installed, and shows Install or Update accordingly. The legacy `manifest.json` exists only to provide an upgrade path from the old MelonLoader version to the new installer with manifest-v2.

A mod package is a zip that mirrors the Big Walk folder, so installing is extract-in-place. Thunderstore metadata at the archive root is ignored. Each entry can declare:

- `core`: the headline mod. Everything else lists under Optional add-ons.
- `preserve`: files left alone if they already exist, so your calibration and configs survive updates.
- `tokenize`: files where `{{GAMEDIR}}` and `{{GAMEDIR_JSON}}` are replaced with your game folder on install, used for the SteamVR app manifest.
- `beta`: an optional unstable release selected when the user enables Beta updates. The mod's top-level release fields stay stable, while `beta` has its own `version`, `url`, `sha256`, and `size`.

```json
"beta": {
  "version": "1.1.0-beta.1",
  "url": "https://example.com/BigWalkVR-1.1.0-beta.1.zip",
  "sha256": "...",
  "size": 20214466
}
```

Mods without a `beta` entry continue to use their stable release when Beta updates are enabled.

Every install records the exact list of files it wrote to `<game>\UserData\BigWalkVRInstaller\<id>.json`, including runtime payload destinations deployed by the preloader. Updates delete files the previous version shipped that the new one no longer does, and uninstall removes exactly what was recorded, nothing else.

## License

MIT, see [LICENSE](LICENSE). Third-party software details are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Supporting

If you've enjoyed something I've made, and want to support my work, see my ko-fi!

https://ko-fi.com/circuitlord 
