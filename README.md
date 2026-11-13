# AdditiveDamageModifier

Resistances and weaknesses stack by damage type on players and creatures. For example, fire weakness (+45%) and fire resistance (−30%) add up to +15%: a 100-point fire hit becomes 115 before other defenses.

## In game

**Read an item's strengths and weaknesses.** The Root Harnesk reduces pierce damage but increases fire damage.

<img src="https://i.ibb.co/MyDPH8F0/Screenshot-2026-06-02-101136.png" alt="Root Harnesk tooltip: −30% pierce damage and +30% fire damage" width="380">

**Check active effects.** On the HUD, positive numbers mean more damage taken and negative numbers mean less.

<img src="https://i.ibb.co/29vbBGG/Screenshot-2026-06-02-100656.png" alt="Six lightning effect icons on the HUD, ranging from +45% weakness to −45% resistance" width="620">

Open the Compendium to see which damage types each effect changes. This older screenshot predates the current `Net` and `MinTotal` labels.

<img src="https://i.ibb.co/RT9Rh32Q/Screenshot-2026-06-02-100447.png" alt="Compendium entries for lightning weaknesses and Bonemass resistances" width="430">

**See how the numbers add up.** The [TouchGrass training meter](https://thunderstore.io/c/valheim/p/sighsorry/TouchGrass/) shows how fire weakness and resistance change a hit.

<img src="https://i.ibb.co/DHxbZTWb/Screenshot-2026-06-02-102122.png" alt="Training meter showing a 100-point fire hit against stacked fire modifiers" width="580">

Compare pierce damage with different combinations of armor, mead and Bonemass.

<img src="https://i.ibb.co/1GRVQyBC/Screenshot-2026-02-25-024101.png" alt="Damage comparisons with Root Harnesk, mead and Bonemass active in different combinations" width="620">

## Getting started

Install with a Valheim mod manager. Stacking works automatically. In multiplayer, use the same ADM version on the server and all clients.

- Equip armor or activate an effect, then hover over it. `−30% / Net −45%` means the source contributes −30% and your total is −45% before the player minimum. A shield's block-only resistance is not part of that passive total.
- Open **Compendium → Additive Damage Modifiers** for the current server values. Players and other characters have separate settings. Trees, rocks and buildings keep their existing rules.
- To change modifier values, player minimums or fall damage, edit `BepInEx/config/sighsorry.AdditiveDamageModifier.cfg`. Gameplay settings sync from the server.

## Compatibility

When Path of Valheiman (PoV) is loaded, ADM disables its resistance rules and percentage displays for players and creatures. Fall damage settings and all 56 `adm_*` status effects remain available. See the [PoV guide](https://github.com/sighsorry1029/AdditiveDamageModifier/blob/main/docs/player-guide.md#pathofvalheiman-compatibility) for multiplayer setup and how to restore your saved resistance settings.

## Guides

[Configuration and damage rules](https://github.com/sighsorry1029/AdditiveDamageModifier/blob/main/docs/player-guide.md#configuration) · [Status effects and console commands](https://github.com/sighsorry1029/AdditiveDamageModifier/blob/main/docs/player-guide.md#status-effects-and-commands)

[Build from source (developers)](https://github.com/sighsorry1029/AdditiveDamageModifier/blob/main/docs/development.md)
