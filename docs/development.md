# Developing AdditiveDamageModifier

Run the commands below from the repository root. The [project](../AdditiveDamageModifier.csproj) targets .NET Framework 4.8 and uses the original Valheim assemblies, not publicized copies. Set the MSBuild property `GamePath` if your Valheim/BepInEx installation is not detected; `CorlibPath` is the directory containing the managed game assemblies.

## Build and run managed checks

```powershell
dotnet build AdditiveDamageModifier.csproj -c Debug -p:DeployToGame=true
pwsh -NoProfile -File .\Test-Mod.ps1
```

Debug deploys the **merged** DLL to the game's `BepInEx/plugins` directory, replacing the installed copy. Use `-p:DeployToGame=false` to skip that copy. Debug does not update the manifest or create release ZIPs.

[Test-Mod.ps1](../Test-Mod.ps1) requires PowerShell 7. It checks damage calculations, hit-context cleanup, translations, PathOfValheiman detection and patch selection, and patch targets in the game assemblies. It uses a temporary BepInEx core-config fixture; it does not save game/plugin configuration, install live Harmony patches, load PathOfValheiman, or start Valheim.

Pass `-AssemblyPath` to test a different built DLL, or `-GamePath` to use another game installation. To check dedicated-server assemblies without a server BepInEx installation, pass the server path as `-GamePath` and a compatible client's `BepInEx/core` directory as `-BepInExCorePath`. A passing assembly check is not a running-server test.

## Patch boundaries

- [Character damage handling](../AdditiveDamagePatches.cs) intercepts modifier lists from player armor, equipped blocking items, and the base `SE_Stats.ModifyDamageMods` method. Only the specific hit being processed by `Character.RPC_Damage` or `Humanoid.BlockAttack` uses ADM's resistance handler. There is no global replacement for `DamageModifiers.ApplyIfBetter`.
- A mod that bypasses the base status-effect method, chooses its own weak-spot modifiers or applies damage directly can follow a different path. Verify that integration separately; ADM does not replace ownership or PvP validation.
- The Valheim 1.0 `NonPlayer` damage **channel** is handled by the calculation but has no new minimum cap or registered status effect. It is distinct from the **non-player Character profile**, which selects configuration values for a target.
- [Startup patch selection](../Plugin.cs) checks the loaded PathOfValheiman plugin once, before installing ADM's patches. It does not change that mod's patches. Runtime plugin replacement is unsupported; restart after changing the mod set.
- [Tooltip handling](../AdditiveDamageStatusEffects.cs) targets the six-argument item tooltip, including appended tooltips. Nested descriptions share the same Net snapshot per generation, not for the whole time an item is hovered.
- ILRepack merges the vendored ServerSync and YamlDotNet into ADM. The three obsolete ServerSync RPC-target field references were fixed in the vendored library; see the [repair and reproduction notes](../Libs/ServerSync-compatibility.md). Do not reapply that repair during a normal build.

## Live regression checklist

| Area | What to verify in Valheim |
| --- | --- |
| Combat | Use different player and non-player settings. Stack armor and status effects, block with a shield or weapon, and try mixed physical/elemental hits. Compare trees, logs, rocks and structures with an ADM-disabled baseline. |
| Player and environment | Test default Spirit/Chop/Pickaxe Ignore, added weaknesses, an explicitly changed base tier, respawn and rejoin. Check Cold/Freezing just below, at and above the Frost threshold with single and combined sources, Immune/Ignore and a warm area. |
| UI | Compare equipped, unequipped, set and active mead effects, tooltip details off, and reopening the Compendium after expiry. Check HUD slot reuse/rebuilds at different UI scales or with a HUD mod. Profile allocations with the inventory both open and closed. |
| Multiplayer | Check client, host and dedicated server: config migration across restart, admin locks, synchronized changes, matching/mismatched versions and reconnects. |
| PathOfValheiman | On matching mod sets, verify the compatibility notice, no ADM resistance numbers or duplicate calculation, continued fall tuning and registered `adm_` effects. Check frost-resistant gear/mead separately from numeric bonuses. Remove PoV, restart, and confirm saved ADM settings take effect again. |

## Package a release

For a new release, update `ModVersion` in [Plugin.cs](../Plugin.cs) and add an entry to the [changelog](../Thunderstore/CHANGELOG.md). Release packaging uses Windows PowerShell:

```powershell
dotnet build AdditiveDamageModifier.csproj -c Release
```

After the DLL is merged, the build synchronizes the manifest version to the DLL and creates `Thunderstore/AdditiveDamageModifier_v<version>.zip` and `Nexus/AdditiveDamageModifier_v<version>.zip`. Rebuilding the same version replaces those local archives. Verify the manifest version and packaged DLL against the Release output; a successful build alone does not confirm publication.
