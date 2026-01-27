# SoloTweaker

A V Rising server mod that provides stat buffs to solo players, helping balance the game for those playing alone or when clan members are offline.

## Features

- **Automatic Solo Detection** - Buffs are applied and removed automatically based on your clan status
- **Configurable Stat Boosts** - Customize every buff value to fit your server's balance
- **Clan Offline Threshold** - Optional timer before buffs activate when clan members go offline
- **Anti-Exploit Protection** - Prevents players from leaving/rejoining clans to bypass timers
- **Equipment-Aware** - Buffs properly adjust when you swap weapons or gear

## Stat Buffs

When playing solo, players receive the following buffs (all configurable):

| Stat | Default | Description |
|------|---------|-------------|
| Physical Damage | +10% | Increases weapon and physical ability damage |
| Spell Damage | +10% | Increases magic ability damage |
| Attack Speed | +10% | Faster attacks and ability casts |
| Max Health | +10% | Increases maximum HP |
| Movement Speed | +10% | Faster walking/running speed |
| Critical Chance | +10% | Multiplicative increase to crit chance |
| Critical Damage | +10% | Multiplicative increase to crit damage |
| Physical Lifesteal | +10% | Life gained from physical damage |
| Spell Lifesteal | +10% | Life gained from spell damage |
| Resource Yield | +10% | More resources from gathering |

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) for V Rising
2. Download `SoloTweaker.dll`
3. Place it in your `BepInEx/plugins` folder
4. Start the server to generate the config file

## Configuration

After first run, edit `BepInEx/config/SoloTweaker.cfg`:

```ini
[1. Combat Stats]
# Physical damage bonus (0.10 = +10%)
SoloDamagePercent = 0.1

# Spell damage bonus (0.10 = +10%)
SoloSpellDamagePercent = 0.1

# Attack speed bonus (0.10 = +10%)
SoloAttackSpeedPercent = 0.1

# Physical crit chance bonus - multiplicative (0.10 = 10% increase, so 50% base becomes 55%)
SoloCritChancePercent = 0.1

# Physical crit damage bonus - multiplicative (0.10 = 10% increase)
SoloCritDamagePercent = 0.1

[2. Survivability]
# Max health bonus (0.10 = +10%)
SoloHealthPercent = 0.1

# Physical lifesteal bonus (0.10 = +10% lifesteal)
SoloPhysicalLeechPercent = 0.1

# Spell lifesteal bonus (0.10 = +10% lifesteal)
SoloSpellLeechPercent = 0.1

[3. Mobility & Utility]
# Movement speed bonus (0.10 = +10%)
SoloMoveSpeedPercent = 0.1

# Resource yield bonus (0.10 = +10%)
SoloResourceYieldPercent = 0.1

[4. Clan Settings]
# Minutes a clan member must be offline before solo buffs activate (0 = instant)
SoloClanOfflineThresholdMinutes = 0
```

### Clan Offline Threshold

The `SoloClanOfflineThresholdMinutes` setting controls how long clan members must be offline before you're considered "solo":

- **0** (default): Instant - buffs activate immediately when you're the only online clan member
- **30**: Buffs activate 30 minutes after the last clan member goes offline
- **60**: Buffs activate 1 hour after the last clan member goes offline

This prevents abuse where players could have clan members log off temporarily to gain buffs during raids or boss fights.

The timer also applies when:
- A clan member disconnects from the server
- A clan member leaves the clan
- You leave a clan (you must wait before getting solo buffs)

## Commands

| Command | Description |
|---------|-------------|
| `.solo` | Check your current solo status and buff state |
| `.solooff` | Disable solo buffs for yourself |
| `.soloon` | Re-enable solo buffs for yourself |

### Example Output

```
.solo
> SOLO, BUFF
> You are not in a clan.
> SoloTweaker buff is ACTIVE.
```

```
.solo
> NOT SOLO, NO BUFF
> Clan members: 2 total, 2 online.
> Another clan member is currently online; solo buff in clan is unavailable.
> SoloTweaker buff is NOT ACTIVE.
```

```
.solo
> NOT SOLO, NO BUFF
> Clan members: 2 total, 1 online.
> Solo buff in clan will become available in 25m 30s (if no clanmates log in).
> SoloTweaker buff is NOT ACTIVE.
```

## How It Works

1. **Solo Detection**: The mod checks every frame if you qualify as "solo":
   - No clan: You're solo (unless you recently left one)
   - In a clan: Solo only if all other members are offline past the threshold

2. **Buff Application**: When solo, stat multipliers are applied to your current equipment stats. When you swap gear, buffs automatically adjust to the new base values.

3. **Buff Removal**: When a clan member comes online or you join a clan, buffs are immediately removed.

4. **Anti-Exploit**:
   - Leaving a clan starts a timer (same as offline threshold)
   - Remaining clan members also get a timer when someone leaves
   - Prevents instant buff abuse through clan manipulation

## Compatibility

- **V Rising**: Gloomrot update and later
- **BepInEx**: IL2CPP version for V Rising
- **Server-side only**: No client mod required

## Troubleshooting

**Buffs not applying?**
- Check `.solo` to see your status
- Verify you haven't used `.solooff`
- If in a clan, check the offline threshold timer

**Stats seem wrong after equipment swap?**
- Buffs automatically adjust each frame
- If issues persist, use `.solooff` then `.soloon` to reset

**Config changes not working?**
- Restart the server after editing config
- Check BepInEx logs for errors

## Credits

Special thanks to the hellsing community for testing the mod to make sure it works smoothly, further thanks goes to Odjit for the guidance and help with the mod.
## License

MIT License - Feel free to modify and redistribute.
