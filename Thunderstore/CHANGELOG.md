| `Version` | `Update Notes`    |
|-----------|-------------------|
| 1.0.8     | - Added 56 `adm_` status effects for Blunt, Pierce, Slash, Fire, Poison, Frost, Lightning, and Spirit modifiers <br> - Added visible status effect icons, readable HUD names, and percent labels for `adm_` effects <br> - Updated modifier icons to show `+1`, `+2`, `+3`, `-1`, `-2`, and `-3` tier markers <br> - Added modifier percent info to damage modifier tooltips, with `MinNet` shown only in the Active effects compendium <br> - Added a client-only General config option to hide non-compendium modifier percent tooltip suffixes <br> - Improved config usability with integer percent/cap sliders while keeping Fall Damage Multiplier at 2-decimal precision |
| 1.0.7     | - Improved calculating damage sum logic |
| 1.0.6     | - Resolved players doing excessive damage in some edge cases |
| 1.0.5     | - Added fall damage multiplier <br> - Now it is easier to type in numbers on config manager |
| 1.0.4     | - Made it so that player would have -100% damage from spirit,chop,pickaxe by default <br> - Added config option for min damage cap player would get from all damage sources(pierce, slash, blunt, poison, lightning, frost, fire) <br> - please regenerate your conifg |
| 1.0.3     | - if sum of frost resistance and weakness is below -15%(configurable), Player gets immune to Freezing and Cold, check `Cold/Freezing Immunity Trigger Frost Delta Percent` |
| 1.0.2     | - Removed lower-cap clamping during intermediate accumulation, which previously caused order-dependent results and incorrect behavior when multiple modifiers were combined <br>- During additive combination, if any modifier is Ignore, the final result is forced to Ignore (0 damage) <br> - Immune is now fixed at -100% (weakness still can be added over it) <br> - Minimum Damage Taken Cap now applies to players only. <br> - Config cleanup |
| 1.0.1     | - `Ignore` overrides other resistance and weakness field into 0% total damage <br> - Make player `Ignore` chop, pickaxe, spirit damage instead of `Immune` |
| 1.0.0     | - Initial Release |
