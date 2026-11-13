# AdditiveDamageModifier player guide

The numbers below are fresh-install defaults. In game, open **Compendium → Additive Damage Modifiers** to see the settings for your server. If PathOfValheiman is loaded, see [compatibility mode](#pathofvalheiman-compatibility) instead.

## Configuration

After the first launch, edit `BepInEx/config/sighsorry.AdditiveDamageModifier.cfg` in your game or mod-manager profile.

| Section | Settings |
| --- | --- |
| `1 - General` | `Lock Configuration` restricts server setting changes to admins. `Show Modifier Percent in Tooltips Outside Compendium` controls tooltip details on your client only. Both default to `On`. |
| `2 - Additive Damage` | Player tier percentages, per-type minimum damage and the Frost threshold for environmental protection. |
| `2 - Non-Player Character Damage` | Tier percentages for creatures, including animals, tames and NPCs. No minimum damage settings. |
| `3 - Fall Damage` | Maximum base fall damage and how quickly damage grows with distance. |

Both character groups have the same seven tier keys. You can set each group independently; a newly created non-player setting starts at the saved player value, not necessarily the fresh-install default. Gameplay settings are server-synced.

| Tier key | Default change in damage taken |
| --- | ---: |
| Very Weak Percent | +45% |
| Weak Percent | +30% |
| Slightly Weak Percent | +15% |
| Slightly Resistant Percent | −15% |
| Resistant Percent | −30% |
| Very Resistant Percent | −45% |
| Immune Percent | −100% |

Enter whole percentages. Immune accepts `-100` to `0`; the other tiers accept `-100` to `100`.

## Damage rules

Add the signed modifiers for **one damage type on the target** before applying any player minimum. For example, a Feather Cape's fire weakness (+45%) and Barley Wine's fire resistance (−30%) give +15%: at the resistance step, 100 fire damage becomes 115. A −30% pierce resistance, +30% pierce weakness and −15% pierce resistance give −15%, so 100 pierce damage becomes 85. Neither result changes the other damage types in a mixed hit.

- **Minimum damage:** `Player Minimum Damage Taken Percent - <damage type>` accepts `0`–`50` and defaults to `10` for Blunt, Pierce, Slash, Fire, Poison, Frost and Lightning. A net −120% on one of these types still leaves 10% damage at this step. This minimum does not apply to creatures.
- **Immune vs. Ignore:** Immune contributes its configured percentage; weakness can offset it. Ignore makes that damage type deal zero at the resistance step, even with a player minimum or a weakness. On player initialization, ADM converts the base Immune values for Spirit, Chop and Pickaxe to Ignore. It leaves other starting values alone. Another mod that later replaces those values or bypasses resistance handling can change the outcome.
- **Scope:** ADM covers normal character hit and block calculations, passive armor and ordinary `SE_Stats` modifiers. It does not change trees, logs, rocks or structures. An external effect or attack that calculates damage itself is not automatically additive.

## Tooltips and Compendium

An unequipped item previews its **own** modifier, e.g. `(-30%)`. When that exact armor piece is equipped or the displayed status effect is active on you, its line also shows your total for that damage type: `(-30% / Net -45%)`. Direct weapon and shield modifiers apply when blocking, not to passive Net; active equip and set effects do count.

Net is the sum **before** the player minimum. In **Compendium → Active effects**, `MinTotal -90%` means at least 10% of the incoming damage remains at the resistance step. `Net Ignore` has no MinTotal because the minimum does not apply. Reopen the page after changing equipment or effects to refresh it. Hiding percentages in ordinary tooltips does not hide the Active effects details.

## Status effects and commands

Use the console command `addstatus adm_blunt_resistant` to try a visible blunt resistance effect. For other effects, use `adm_<damage_type>_<modifier>`. All 56 combinations below are registered:

- **Damage type:** `blunt`, `pierce`, `slash`, `fire`, `poison`, `frost`, `lightning`, `spirit`
- **Modifier:** `very_weak`, `weak`, `slightly_weak`, `slightly_resistant`, `resistant`, `very_resistant`, `immune`

Examples: `adm_fire_immune`, `adm_spirit_resistant`. Each effect has a HUD name and an icon; weakness and resistance icons use tier markers from +1 to +3 and −1 to −3. In normal ADM mode the icon text also shows the configured percentage.

## Cold and Freezing

`Cold/Freezing Immunity Trigger Frost Delta Percent` defaults to `-15` (range `-100` to `0`). When your net Frost modifier is **at or below** the threshold, the game blocks or clears the environmental Cold and Freezing effects. A single −15% modifier and a total of −15% both qualify at the default, but neither qualifies if you change the threshold to −50%. Warmth and shelter rules still apply.

## Fall damage

`Maximum Fall Damage` sets the base damage ceiling before status effects (default `100`, range `100`–`500`). `Fall Damage Multiplier` changes how quickly damage grows with distance (default `1.00`, range `1.00`–`2.00`, rounded to two decimal places).

At the defaults, a 20 m fall reaches 100 base damage. With a maximum of 200, the same ceiling of 200 is reached at 36 m with a multiplier of 1.00, or at 20 m with a multiplier of 2.00. At 2.00 a 12 m fall already reaches 100 base damage.

## PathOfValheiman compatibility

When ADM detects the loaded plugin `treextr.pathofvalheiman` at startup, it leaves both player and creature resistance handling to the game, PoV and other installed mods. ADM stops applying its tier percentages, minimums, base Ignore conversion and environmental Frost threshold. It also hides its own percentages, Net and MinTotal. It does not disable PoV patches.

Fall settings, configuration sync and all 56 `adm_` effects remain available. Those effects supply their original modifier tiers, not ADM percentages. The ADM Compendium page shows the active mode and fall settings. Removing or disabling PoV and restarting restores the saved ADM resistance settings. Keep the mod set consistent on clients and servers; config sync cannot fix a mismatch.

Frost resistance mead or suitable equipment can still provide native Cold/Freezing protection. In the examined PoV version (6.0.7), numeric Frost resistance alone does not grant environmental immunity; its separate Cold resistance reduces penalties once the status is active.

## Translations

English and Korean text is built into the DLL. To add a client-side translation, copy the [English template](../Resources/Translations/English.yml) or use the copy in the Thunderstore package. Put it anywhere under `BepInEx/` and name it for the language selected in Valheim, e.g. `AdditiveDamageModifier.Turkish.yml`. Keep the keys and format placeholders unchanged. Missing or blank translations fall back to English; unknown keys are ignored. Check the BepInEx log if a translation file cannot be read.
