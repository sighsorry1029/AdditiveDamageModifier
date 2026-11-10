# AdditiveDamageModifier

AdditiveDamageModifier makes Valheim resistance and weakness modifiers stack together for players and other Characters instead of letting only the strongest modifier win.

When PathOfValheiman is loaded, ADM automatically leaves resistance handling to it and keeps its fall-damage settings and `adm_` status-effect catalog. See [PathOfValheiman compatibility](#pathofvalheiman-compatibility) for the features disabled in that mode. The resistance values and examples below describe normal ADM mode.

The project references original Valheim game assemblies and requires BepInExPack Valheim **5.4.2360**. Building against a game installation and passing managed checks do not replace live client, host and dedicated-server verification. Existing configuration keys and translation formats are retained; separate non-player settings are described below.

The 1.0 compatibility patch updates the six-argument item tooltip hook (including appended tooltips), processes the new `NonPlayer` damage channel without adding a player minimum setting or new `adm_` effects, and repairs three obsolete constant-field accesses in the bundled ServerSync. See [ServerSync repair details](Libs/ServerSync-compatibility.md).

In vanilla, several sources of resistance and weakness do not cancel each other out. The game chooses one final modifier by priority. This mod changes Character resistance calculations into an additive calculation. Trees, logs, rocks, structures and other non-Character targets retain their existing game/other-mod damage handling.

![](https://i.ibb.co/29vbBGG/Screenshot-2026-06-02-100656.png) <br>
Adds visible `adm_` status effects for testing, events, commands, and other mods. You can use `addstatus` command for testing. <br>
![](https://i.ibb.co/MyDPH8F0/Screenshot-2026-06-02-101136.png) <br>
![](https://i.ibb.co/RT9Rh32Q/Screenshot-2026-06-02-100447.png) <br>
Shows the effective modifier percent in tooltips and active effect UI. <br>

![](https://i.ibb.co/DHxbZTWb/Screenshot-2026-06-02-102122.png) <br>
CheckOut this mod to use training dummy for damage and resistance testing. https://thunderstore.io/c/valheim/p/sighsorry/TouchGrass/ <br>
Above is an example of RootArmor(+30%) + FeatherCape(+45%) + FireResistMead(-30%) = SUM(+45%) of fire damage with the mod's default setting.<br>

## Main Features

- Stacks resistance and weakness modifiers additively per damage type.
- Affects players and non-player Characters, including Character-based animals, tamed creatures and NPCs; it does not require `MonsterAI`.
- Lets servers configure each modifier tier, including Immune, separately for players and non-player Characters.
- Preserves the player's default Spirit, Chop and Pickaxe protection as Ignore, independently of the Immune percentage.
- Adds player minimum damage caps so very strong resistance cannot always reduce player damage to 0.
- Adds visible `adm_` status effects for testing, events, commands, and other mods.
- Shows the effective modifier percent in tooltips and active effect UI.
- Adds a Compendium page that explains the current server values, calculation, player minimums, and vanilla differences.
- Adds configurable fall damage cap and fall damage multiplier options.
- Keeps ServerSync version enforcement and synced gameplay config.

## Additive Damage Examples

Fresh-install default modifier values for both Character profiles:

- Very Weak: `+45%`
- Weak: `+30%`
- Slightly Weak: `+15%`
- Slightly Resistant: `-15%`
- Resistant: `-30%`
- Very Resistant: `-45%`
- Immune: `-100%`

The existing `2 - Additive Damage` tier settings now apply to players. The new `2 - Non-Player Character Damage` tier settings initially copy the existing player values when first created, then remain independent. Existing configured values are not reset. For example, player Immune can be set to `-60%` and non-player Immune to `-75%`; those are optional settings, not new defaults. Gameplay values remain server-synchronized and administrator-lockable.

There is no Other profile: this mod does not change non-Character resistance selection or percentages. Without other mods, those targets use vanilla values such as Resistant `-50%` and Weak `+50%`, rather than this mod's `-30%` and `+30%` defaults.

## Example player modifiers:
![](https://i.ibb.co/1GRVQyBC/Screenshot-2026-02-25-024101.png)
```text
Root Armor pierce resistance (-30%)
+ Berserker Mead pierce weakness (+30%)
+ Bonemass pierce resistance (-15%)
= pierce -15%
```

The player takes `85%` pierce damage.

```text
Feather Cape fire weakness (+45%)
+ Barley Wine fire resistance (-30%)
= fire +15%
```

The player takes `115%` fire damage.

Creature resistances and weaknesses are additive too. A creature that is weak to pierce by `+30%` takes `130%` pierce damage; a creature resistant by `-30%` takes `70%`.

## Player Minimum Damage Caps

Player damage has configurable minimum damage taken caps for these damage types:

- Blunt
- Pierce
- Slash
- Fire
- Poison
- Frost
- Lightning

The config option is named `Player Minimum Damage Taken Percent - <damage type>`.
The default value is `10`, so an immune or heavily resistant player still takes at least `10%` of the original damage from those capped types.
In the Active effects compendium, this same cap is shown on the additive modifier scale as `MinTotal -90%`.

At player initialization, the existing base Immune values for Spirit, Chop and Pickaxe are changed to Ignore on that player instance. Other base values, non-player Characters, item/effect templates and game asset files are not changed. NonPlayer's existing Ignore is left alone.

Ignore is not a percentage: ordinary resistance and weakness contributions cannot offset it, and it bypasses minimum damage caps. Thus changing player Immune to `-60%` does not enable the player's default Spirit, Chop or Pickaxe damage. Unlike the old additive Immune rule, adding Spirit weakness no longer makes that default player protection vulnerable. These three types still have no player minimum setting. A later direct change by another mod, a replacement WeakSpot resistance set or damage that bypasses resistance calculations is outside this base-value protection.

## Status Effects

The mod registers 56 status effects named:

```text
adm_<damage_type>_<modifier>
```

Supported damage types:

```text
blunt, pierce, slash, fire, poison, frost, lightning, spirit
```

Supported modifiers:

```text
very_weak, weak, slightly_weak, slightly_resistant, resistant, very_resistant, immune
```

Examples:

```text
adm_blunt_very_weak
adm_fire_immune
adm_spirit_resistant
```

These effects can be added with commands such as:

```text
addstatus adm_blunt_resistant
```

Each `adm_` effect has a generated status icon, a readable HUD name without the `adm_` prefix, and a percent label. Modifier icons use simple tier markers:

```text
+1, +2, +3, -1, -2, -3
```

## Tooltips And Compendium

Item damage modifier tooltip lines show the player-profile percentage, including when the item is not equipped, for example:

```text
Damage modifier: Resistant VS Fire (-30%)
```

When the exact passive armor item is equipped, or the displayed status effect is currently active on the local player, the same line also shows the combined modifier for that damage type:

```text
Damage modifier: Resistant VS Fire (-30% / Net -45%)
```

`Net` is the raw sum before the player minimum damage cap. Direct weapon and shield damage modifiers are block-only and are not included in passive Net; an Equip or Set status effect from those items is included while that effect is active.

Net is calculated at most once per tooltip generation and shared by its nested effect descriptions. Valheim can regenerate an item tooltip every frame while the inventory is open; this is not a cache for the entire time the tooltip is visible.

This non-compendium percent suffix is controlled by a client-only config option:

```text
[1 - General]
Show Modifier Percent in Tooltips Outside Compendium = On
```

The Active effects compendium always shows fuller information, including the current `Net` for active effects and `MinTotal` when applicable:

```text
Damage modifier: Resistant VS Fire (-30% / Net -45% / MinTotal -90%)
```

The Active effects page shares one Net snapshot when the page is generated. Reopen the Compendium to refresh effects or values that changed while it was open.

If the combined result is Valheim's special `Ignore` modifier, it is shown as `Net Ignore` and `MinTotal` is omitted because that cap does not apply. An active Spirit effect can therefore show its own percentage alongside `Net Ignore`: the source exists, but it cannot offset the player's default Ignore protection.

The Compendium also includes an `Additive Damage Modifiers` page. It reads the current server configuration and explains:

- Separate current player and non-player Character modifier values
- The damage formula and a calculated player-profile example
- Player minimum damage and `MinTotal`
- Vanilla modifier values and selection behavior
- Spirit, Immune, Ignore, and Cold/Freezing rules

## Localization

The Compendium page includes English and Korean translations in the mod DLL. The Thunderstore package also includes `AdditiveDamageModifier.English.yml` as an editable translation template.

Additional client-side translations can be placed anywhere under the `BepInEx` folder. Name the file after the language selected in Valheim:

```text
AdditiveDamageModifier.<Language>.yml
```

Example:

```text
AdditiveDamageModifier.Turkish.yml
```

Use the [English translation file](https://github.com/sighsorry1029/AdditiveDamageModifier/blob/main/Resources/Translations/English.yml) as the template. Missing or blank keys fall back to English, and unknown keys are ignored. A malformed external file is skipped and reported in the BepInEx log.

## Fall Damage

Fall damage has two synced config options:

- `Maximum Fall Damage`: raises the maximum base fall damage before status effects.
- `Fall Damage Multiplier`: controls how fast fall damage grows.

Useful examples:

- Vanilla reaches `100` fall damage at `20m`.
- `Maximum Fall Damage = 200` and `Fall Damage Multiplier = 1.00` reaches `200` damage at `36m`.
- `Maximum Fall Damage = 200` and `Fall Damage Multiplier = 2.00` reaches `200` damage at `20m`.
- With multiplier `2.00`, `100` damage happens at `12m`.

`Fall Damage Multiplier` keeps 2-decimal precision. Most other numeric config values are integer sliders.

## Cold And Freezing

The frost modifier can control Cold and Freezing immunity through:

```text
Cold/Freezing Immunity Trigger Frost Delta Percent
```

Default value is `-15%`. If the effective additive frost delta, using the player profile, is less than or equal to this threshold, vanilla Cold and Freezing effects are blocked or cleared.

The same threshold applies to a single resistance tier and an equal total from multiple sources. For example, with a `-50%` threshold, a single `-15%` resistance no longer grants environmental immunity. Vanilla warmth and shelter rules are unchanged.

## Config Sections

`1 - General`

- `Lock Configuration`
- `Show Modifier Percent in Tooltips Outside Compendium`

`2 - Additive Damage`

- Player modifier tier values, including Immune (existing tier keys are retained)
- Player minimum damage taken caps
- Cold/freezing immunity threshold

`2 - Non-Player Character Damage`

- Independent non-player Character modifier tier values, including Immune

`3 - Fall Damage`

- Maximum fall damage
- Fall damage multiplier

## Character Scope And Mod Compatibility

Additive list handling is limited to the known Character-owned paths: player armor, `SE_Stats.ModifyDamageMods`, and the blocking weapon/shield in `Humanoid.BlockAttack`. The Character damage and block paths enter a short, exact-HitData resistance scope; unrelated `HitData.ApplyResistance` calls retain their original handling. There is no global additive replacement of `DamageModifiers.ApplyIfBetter`.

Effects that call the base `SE_Stats.ModifyDamageMods` use the owning Character's profile. An external effect that overrides this method without calling the base implementation, performs its own `DamageModifiers.Apply`, or supplies an entirely separate damage pipeline is not automatically made additive. Such integrations need individual verification. WeakSpot selection, ownership checks, PVP validation and the game's damage pipeline are not replaced wholesale.

### PathOfValheiman Compatibility

At startup, ADM detects the loaded BepInEx plugin `treextr.pathofvalheiman` and disables only its own resistance handling. It does not remove or alter PathOfValheiman's patches. Detection uses the plugin identity, not an exact version or DLL hash; this prevents duplicate resistance handling but does not certify every PathOfValheiman version or other mod combination.

In this mode, ADM does not apply:

- Player or non-player Character modifier percentages and additive stacking, including armor, status-effect and blocking paths.
- Player minimum damage caps.
- The player's default Spirit, Chop and Pickaxe conversion from Immune to Ignore. PathOfValheiman's own Spirit policy is no longer overridden by ADM.
- The additive Frost threshold for Cold/Freezing immunity.
- ADM source-percentage, `Net` or `MinTotal` additions to tooltips, status-effect UI or the Active effects page. PathOfValheiman and other mods may still display their own resistance information.

The ADM Compendium page instead shows a localized compatibility notice and the active fall-damage settings. Fall-damage cap/multiplier handling, ServerSync and all 56 `adm_` status-effect registrations remain active. The status effects retain their names and native modifier tiers for commands and other mods; their actual damage behavior follows the game/PathOfValheiman rules, not ADM's configured percentages.

Existing configuration keys and saved values are preserved. Inactive resistance settings remain available but do not affect damage or environmental immunity while this mode is active. Removing or disabling PathOfValheiman and restarting restores ADM's normal resistance handling with those saved settings. Detection is fixed for the process; runtime plugin replacement is not supported. Keep the relevant mod set consistent on clients, hosts and dedicated servers: configuration synchronization does not install plugins or guarantee matching compatibility modes.

Cold and Freezing protection remains available through the game's native Frost resistance tiers, such as those supplied by suitable equipment and frost resistance mead. PathOfValheiman's numeric Frost resistance bonuses alone do not create environmental immunity. Its separate `ColdResistPct` reduces penalties of an already applied Cold/Freezing effect rather than preventing that status. These are distinct from Frost damage and the Frost/Chill movement-slow effect. ADM's inactive Frost threshold is not used to reinterpret PathOfValheiman's resistance points.

## Building Packages

The project references the original Valheim assemblies from `CorlibPath`; publicized assemblies are not required. Set `GamePath` in your build arguments when using a different game installation. Reflection and Harmony field injection handle the required non-public game members.

Set `ModVersion` in `Plugin.cs`, then run a Release build:

```powershell
dotnet build AdditiveDamageModifier.csproj -c Release
```

After ILRepack completes, the Release build updates `Thunderstore/manifest.json` to the DLL version and creates both `Thunderstore/AdditiveDamageModifier_v<version>.zip` and `Nexus/AdditiveDamageModifier_v<version>.zip`. Rebuilding the same version replaces those archives. Debug builds do not update the manifest or create packages. Release packaging currently requires Windows PowerShell. Update the changelog manually when changing versions.

## Regression Checks

Build the current code and run the actual-DLL checks with PowerShell 7:

```powershell
dotnet build AdditiveDamageModifier.csproj -c Debug -p:DeployToGame=true
pwsh -NoProfile -File .\Test-Mod.ps1
```

Use `-AssemblyPath` for a different build output and `-GamePath` for a different Valheim installation. The script does not launch the game or save game/plugin configuration; it creates and removes an empty temporary BepInEx core-config fixture for isolated loader checks. It checks managed calculations, context cleanup, PoV detection/patch selection, both UI text modes, translation contracts, and game patch targets; it does not load PoV or replace live Unity or multiplayer testing.

The Debug command also copies the final merged DLL into the game's `BepInEx/plugins` folder. Use `-p:DeployToGame=false` to explicitly skip that copy. For dedicated-server assembly checks without an installed BepInEx, pass `-GamePath` pointing to the server and `-BepInExCorePath` pointing to a compatible client `BepInEx/core` directory. This is an assembly check, not a running server test.

Before releasing, also check these in game:

- HUD slots with no ADM effects, multiple ADM effects, ADM-to-normal replacement at the same effect count, and full slot rebuilds. Verify layout at different UI scales and with HUD mods.
- Cold/Freezing thresholds just below, equal to, and above a resistance value, including one source versus an equal combined value, Immune, Ignore, and warm areas.
- Equipped armor, active Equip/Set/Mead effects, tooltip details Off, and reopening the Active effects page after an effect expires.
- Different player and non-player tier values, single and multiple resistance sources, blocking weapons/shields, and mixed physical/elemental hits.
- Player default Spirit/Chop/Pickaxe Ignore with added weaknesses, `Net Ignore` display, respawning and joining a world. Also verify that explicit non-Immune base values are not overwritten.
- Tree/log Chop and other immune channels, mining, structures and any affected object mods against a mod-disabled baseline; character settings must not change those results.
- Existing and migrated config values across restart, administrator locks, synchronized settings, and matching/mismatched versions on client, host, and dedicated server.
- With and without PathOfValheiman, verify startup detection, absence of duplicate resistance handling, the compatibility Compendium page, hidden ADM percentages/Net/MinTotal, retained fall settings and `adm_` effects, and restoration of saved ADM settings after removing PathOfValheiman and restarting. Check native Frost equipment/mead protection separately from numeric Frost bonuses and Cold resistance.

Use Unity Profiler to compare HUD allocations and CPU time with the inventory closed and open. Runtime hot unload/reload is not a validated lifecycle; the icon cache and registered status effects are intended to last for the process.

## Github

https://github.com/sighsorry1029/AdditiveDamageModifier
