# SoloTweaker

A V Rising server mod that provides stat buffs to solo players, helping balance the game for those playing alone or when clan members are offline.

> **Fork info**: This is a rewrite of [Chimll/SoloTweaker](https://github.com/Chimll/SoloTweaker) with a new buff engine, event-driven architecture, and hot reload support.

## Features

- **Native Buff System** — Uses V Rising's `ModifyUnitStatBuff_DOTS` pipeline instead of direct stat manipulation. Buffs survive gear swaps, death, and are properly recalculated by the game engine.
- **Event-Driven Detection** — HookDOTS hooks on `ServerBootstrapSystem` and `ClanSystem_Server` react to connect/disconnect/clan changes instantly, with a 10s safety-net scan as fallback. No per-frame polling.
- **Configurable Stats** — 15 stat types, each with a value and modification type (Multiply or flat Add).
- **Clan Offline Threshold** — Configurable timer (default 30min) before buffs activate when clan members go offline.
- **Anti-Exploit Protection** — Leaving a clan applies a cooldown. Timers survive server reboots using `TimeLastConnected`. Players already solo keep their buff when leaving a clan.
- **Chat Notifications** — Players receive messages when buffs are applied, removed, or when a timer is pending.
- **Bloodpebble Hot Reload** — Full `Unload()` support: buffs cleared, state reset, hooks disposed, commands unregistered.
- **Opt-Out System** — Players can toggle buffs on/off per-character with `.solo t`.

## Stat Buffs

When playing solo, players receive the following buffs (all configurable):

| Stat | Default | Type | Description |
|------|---------|------|-------------|
| Physical Damage | +10% | Multiply | Weapon and physical ability damage |
| Spell Damage | +10% | Multiply | Magic ability damage |
| Attack Speed | +10% | Multiply | Primary and ability attack speed |
| Max Health | +10% | Multiply | Maximum HP |
| Movement Speed | +10% | Multiply | Walking/running speed |
| Critical Chance | +10% | Multiply | Physical and spell crit chance |
| Critical Damage | +10% | Multiply | Physical and spell crit damage |
| Physical Lifesteal | +10% | Add | Life gained from physical damage |
| Spell Lifesteal | +10% | Add | Life gained from spell damage |
| Physical Resistance | +10% | Add | Physical damage reduction |
| Spell Resistance | +10% | Add | Spell damage reduction |
| Resource Yield | +10% | Multiply | Resources from gathering |

Each stat can be set to **Multiply** (scales with gear, `0.10` = +10%) or **Add** (flat value). Set any stat to `0` to disable it.

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) IL2CPP for V Rising
2. Install [VampireCommandFramework](https://github.com/decaprime/VampireCommandFramework)
3. Install [HookDOTS](https://github.com/iZastic/HookDOTS)
4. Download `SoloTweaker.dll` and place it in `BepInEx/plugins/`
5. Start the server — config file is generated at `BepInEx/config/SoloTweaker.cfg`

## Commands

| Command | Shortcut | Admin | Description |
|---------|----------|-------|-------------|
| `.solo` | | No | Show mod info and available commands |
| `.solo status` | | No | Show your solo/buff status (enabled, eligible, active, timer) |
| `.solo eligible` | `.solo e` | No | Diagnostic: show why you are or aren't eligible |
| `.solo toggle` | `.solo t` | No | Toggle solo buffs on/off for yourself |
| `.solo reload` | | Yes | Reload config from disk (no restart needed) |
| `.solo scan` | | Yes | Force rescan all players |
| `.solo debug` | | Yes | Show native buff entity and stat modifiers |

### Status Output

```
[SoloTweaker] Status
Clan : 3 total, 1 online
SoloTweaker enabled | Eligible : NO | Buff : INACTIVE
Timer before application of buff : 12m 30s
```

### Eligible Output

```
[SoloTweaker] Eligibility: NOT ELIGIBLE
[BLOCKING] A member recently left this clan — cooldown 12m 30s remaining.
Clan has 3 member(s) in snapshot.
[BLOCKING] PlayerTwo disconnected 17m 30s ago — 12m 30s left.
[OK] PlayerThree offline 2h 15m (threshold passed).
```

## Configuration

After first run, edit `BepInEx/config/SoloTweaker.cfg`:

```ini
[1. Combat Stats]
# Multiply: 0.10 = +10%. Add: flat value. Set to 0 to disable.
AttackSpeedValue = 0.1
AttackSpeedType = 0           # 0 = Multiply, 1 = Add

PhysicalDamageValue = 0.1
PhysicalDamageType = 0

SpellDamageValue = 0.1
SpellDamageType = 0

CritChanceValue = 0.1
CritChanceType = 0

CritDamageValue = 0.1
CritDamageType = 0

[2. Survivability]
HealthValue = 0.1
HealthType = 0

PhysicalLeechValue = 0.1
PhysicalLeechType = 1         # Add (flat lifesteal)

SpellLeechValue = 0.1
SpellLeechType = 1

PhysicalResistanceValue = 0.1
PhysicalResistanceType = 1    # Add (flat resistance)

SpellResistanceValue = 0.1
SpellResistanceType = 1

[3. Mobility & Utility]
MoveSpeedValue = 0.1
MoveSpeedType = 0

ResourceYieldValue = 0.1
ResourceYieldType = 0

[4. Clan Settings]
# Minutes clan members must be offline before solo buffs activate (0 = instant)
ClanOfflineThresholdMinutes = 30
```

Use `.solo reload` to apply config changes without restarting the server.

## How It Works

1. **Solo Detection** — An O(n) snapshot builds a clan map each scan. For each user, eligibility is checked against clan members' online status and disconnect times.

2. **Buff Application** — A lightweight carrier buff (`ModifyUnitStatBuff_DOTS`) is applied via V Rising's `DebugEventsSystem`. The game engine handles stat recalculation naturally.

3. **Event-Driven Scanning** — HookDOTS postfix hooks detect connect/disconnect events and clan changes. A precise timer fires exactly when the next offline threshold expires.

4. **Anti-Exploit** — Leaving a clan starts a cooldown (same as offline threshold). If you were already solo in the clan, no cooldown is applied. Timers use `TimeLastConnected` to survive server reboots.

## Dependencies

- [BepInEx 6](https://github.com/BepInEx/BepInEx) (IL2CPP)
- [VampireCommandFramework](https://github.com/decaprime/VampireCommandFramework)
- [HookDOTS](https://github.com/iZastic/HookDOTS)
- [Bloodpebble](https://thunderstore.io/c/v-rising/p/cheesasaurus/Bloodpebble/) (optional, for hot reload)

## License

MIT License — Feel free to modify and redistribute.
