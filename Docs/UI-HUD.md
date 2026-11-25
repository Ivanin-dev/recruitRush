# Recruit Rush: UI / HUD spec

Reference canvas is 1080×1920, portrait, `ScaleWithScreenSize` with match 0.5, everything inside the safe area.
Every number below lives in `UiTheme` (`Assets/_Project/Resources/UiTheme.asset`), so you can tune it without touching code.

## Visual language

| Role | Color | Used for |
|---|---|---|
| Neutral | graphite/slate `#2E3A4F` face, `#111722` lip, `#0A0E15` contour | panels, cards, tracks, pause |
| Positive / buffs | blue `#2D8CFF`, green `#4CCB3A` | squad badge, buff badges, PLAY/COLLECT, affordable price |
| Damage / enemies | red `#F0412F`, orange `#FF8A1E` | boss marker, banners, losses, defeat |
| Rewards | gold `#FFC52E`, text `#FFE27A` | coins, reward, chest, x2 |

- **Blocks:** every element is solid: a 5px dark contour, a darker lip under it for the 3D bottom face, and a face with a baked bevel (light top rim, dark bottom rim). No transparency on panels, no blur, no thin grey borders.
- **Text:** bold, with a thick dark outline and a hard (zero-softness) drop shadow. This is the TMP `UNDERLAY_ON` material `M_TextHud.mat`. Swap in a chunky display font via `UiTheme.font`.
- **Icons:** procedural placeholders (white glyph with dark contour, tinted by the Image). Replace any of them through the `UiTheme` icon slots.

## 1. In-run HUD (≈12% of screen height, center lanes free)

```
┌──────────────────────────────────────────────┐
│ [II]    [LVL 3][████████░░░░](☠)   (●) 1 234 │  ← top bar, y −30
│              [gun T2][bullet x1.3][⚡ x1.3]    │  ← buff badges, y −172 (only active ones)
│                                              │
│                 (lanes: no UI)               │
│                                              │
│              ┌──[👤 32]──┐  ← world-space     │
│                 squad                        │
└──────────────────────────────────────────────┘
```

- **Pause** (top-left, 92²): slate button, kept away from where you drag.
- **Level progress** (top-center): blue `LVL n` badge, then a thin 30px bar (green, turning gold near the end, red in the final phase), then a red skull boss marker that pulses in the final phase.
- **Coins** (top-right): slate pill; the coin icon pokes out of its left edge. The number is a rolling counter. Each coin pickup makes the pill punch, flashes the number white and bounces the icon.
- **Buff badges:** 88² squares with a green multiplier tag under each (`T2`, `x1.3`). A badge only shows once it means something, then flashes white and punches when it levels up. They replace the old `RIFLE RATE x1 DMG x1` text.
- **Banner:** solid colored ribbon at about 74% of screen height (red for FINAL WAVE / BOSS, orange for ELITES). It pops in, then slides up and fades after 1.4 s.

### World-space
- **Squad badge:** a blue pill with a soldier icon, a rolling number and a pointer tail. Its width follows the digit count and it grows gently with squad size (log scale, up to +45%). On a gain it pops and flashes green; on a loss it shrinks, shakes and flashes red.
- **Damage numbers:** pooled. Hits on one target are summed and shown at most every 0.3 s, so 200 riflemen don't spam. White for normal hits; gold with `!` for heavy commander hits. They snap in with overshoot, rise, slow down and fade.
- **Callouts:** over the squad on pickups: `+DMG!` (gold), `RATE UP!` (blue), `GUN UP!` (orange, bigger), `+12` (green) and `-5` (red) on gates.

## 2. Lobby (over the live start scene)

```
                                   (●) 180
          RECRUIT
           RUSH            ← logo at 72% height
        [ LEVEL 4 ]
       DRAG TO STEER       ← levels 1–2 only
   ┌──────────────────┐
   │       PLAY       │    ← 660×188 green, 3D lip, breathing; thumb zone
   └──────────────────┘
 ┌──────┐ ┌──────┐ ┌──────┐
 │ (👤) │ │ (▲)  │ │ (⚡) │ ← 304×330 tactile cards
 │SQUAD │ │DAMAGE│ │ RATE │
 │ Lv.4 │ │ Lv.2 │ │ Lv.1 │
 │[● 95]│ │[●120]│ │[● 60]│ ← green strip + pulse if affordable,
 └──────┘ └──────┘ └──────┘   dark strip + pink price if not
```

- PLAY starts the run. Tapping the field no longer starts it.
- Buying applies right away to the squad waiting on screen (`+1 soldier`, `+10% dmg`, `+8% rate` per level). The coin counter rolls down. Tapping an upgrade you can't afford shakes the card.
- The economy is all in `MetaProgress`: costs, growth, per-level bonuses, chest cadence.

## 3. Result

```
   ┌────────── VICTORY! ──────────┐   ← green ribbon (red DEFEAT)
         LEVEL 3 CLEARED
   ┌──────────────────────────────┐
   │ [👤   250]      [☠    90]    │   ← stats count up
   │      (●)  +275               │   ← reward counts up (gold)
   │ NEXT CHEST                   │
   │ [██████████░░░░░░░]  🎁 +150 │   ← fills; chest pops when full
   └──────────────────────────────┘
        [ ▶  x2  (●) ]               ← gold, only when an ad is ready
     [      COLLECT      ]           ← green, primary
```

- Reveal is staged: ribbon and card pop in, then stats at 0.3 s, reward at 0.7 s, track at 1.15 s, chest after that.
- **COLLECT:** 12 coins arc from the reward row into the counter, which ticks as each one lands. The next level loads automatically, so it's a single tap.
- **x2:** goes through `RewardedAds.Show`. On success the reward rolls to double and is collected automatically. The button is hidden in release builds until an ads provider is plugged in.
- **Defeat:** red ribbon, kills, the coins picked up during the run, a tip, and **RETRY**, which reloads instantly.

## Micro-interactions (`UiJuice.cs`)

| Component | Behaviour | Theme keys |
|---|---|---|
| `PressFeedback` | scale to 0.92 in 0.08 s and push the face onto its lip; release bounces back with EaseOutBack; light haptic | `pressScale`, `pressDuration`, `releaseDuration`, `releaseOvershoot` |
| `RollingCounter` | eases to its target (longer for big jumps), punches its container, optional up/down color flash, compact `12.3K` | `counterDuration`, `counterMaxDuration`, `coinPunch` |
| `Pulse` | idle breathing on affordable cards and primary buttons | `affordPulseAmp`, `affordPulseSpeed` |
| `GraphicFlash` / `Shake` / `Pop` | color flash, loss shake, scale punch | `flashDuration`, `badgePunch` |

## Code map

| File | What |
|---|---|
| `UI/UiTheme.cs` | all colors, font sizes, layout numbers, timings, icon overrides |
| `UI/UiKit.cs` | procedural sprites (rounded, bevel, circle), `Lipped` 3D blocks, text material, factories |
| `UI/UiIcons.cs` | SDF-rasterized placeholder icons |
| `UI/UiJuice.cs` | easing plus the juice components above |
| `UI/UiWidgets.cs` | `TactileButton`, `ProgressBar`, `BuffBadge`, `CoinCounter`, `UpgradeCard`, `CoinFlight` |
| `UI/HudView.cs` · `LobbyView.cs` · `ResultView.cs` · `PauseView.cs` | one class per screen |
| `UI/WorldUi.cs` | `SquadBadge`, `WorldText` (damage numbers + callouts) |
| `UI/GameUI.cs` | facade that gameplay talks to |
| `Core/MetaProgress.cs` | coins, lobby upgrades, reward track, `RewardedAds` hook |

The UI is built in code, like the rest of the project: there are no prefabs to keep in sync. To restyle it, edit the theme asset. To use real art, fill the sprite slots.
