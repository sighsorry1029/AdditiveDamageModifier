# AdditiveDamageModifier

AdditiveDamageModifier makes Valheim resistance and weakness modifiers stack together instead of letting only the strongest modifier win.

In vanilla, several sources of resistance and weakness do not cancel each other out. The game chooses one final modifier by priority. This mod changes that into an additive calculation, so every relevant modifier contributes to the final damage multiplier.

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
- Affects both players and creatures.
- Lets servers configure the percent value of each modifier tier.
- Adds player minimum damage caps so very strong resistance cannot always reduce player damage to 0.
- Adds visible `adm_` status effects for testing, events, commands, and other mods.
- Shows the effective modifier percent in tooltips and active effect UI.
- Adds configurable fall damage cap and fall damage multiplier options.
- Keeps ServerSync version enforcement and synced gameplay config.

## Additive Damage Examples

Default modifier values:

- Very Weak: `+45%`
- Weak: `+30%`
- Slightly Weak: `+15%`
- Slightly Resistant: `-15%`
- Resistant: `-30%`
- Very Resistant: `-45%`
- Immune: `-100%`

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

Spirit intentionally has no player minimum cap because vanilla players are already immune to Spirit damage.

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

Damage modifier tooltip lines can show the configured modifier percent, for example:

```text
Damage modifier: Resistant VS Fire (-30%)
```

This non-compendium percent suffix is controlled by a client-only config option:

```text
[1 - General]
Show Modifier Percent in Tooltips Outside Compendium = On
```

The Active effects compendium always shows fuller information, including `MinTotal`:

```text
Damage modifier: Resistant VS Fire (-30% / MinTotal -90%)
```

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

Default value is `-15%`. If the effective additive frost delta is less than or equal to this threshold, vanilla Cold and Freezing effects are blocked or cleared.

## Config Sections

`1 - General`

- `Lock Configuration`
- `Show Modifier Percent in Tooltips Outside Compendium`

`2 - Additive Damage`

- Modifier tier values
- Player minimum damage taken caps
- Cold/freezing immunity threshold

`3 - Fall Damage`

- Maximum fall damage
- Fall damage multiplier

## Github

https://github.com/sighsorry1029/AdditiveDamageModifier
