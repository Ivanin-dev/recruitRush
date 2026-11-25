# Recruit Rush: art direction, readability and combat feel

This is based on the current build (screenshots in `Docs/ui/`, phone capture from a Galaxy A55) and the code that generates the scene:
`ProjectSetup.cs` (light, fog, post-processing), `WorldBuilder.cs` (road and surroundings), `LaneObjects.cs` / `ShootableGate.cs` / `Barricade.cs` (objects), `Fx.cs` / `Effects.cs` (effects), `CameraFollow.cs`, `Mobile_RPAsset.asset`.

---

## 0. Honest audit: what's wrong right now

Ordered by how much each one hurts readability.

1. **No value hierarchy.** The sand (`Sand 0.86/0.76/0.55`) is the brightest surface in the frame and takes up about half the screen. The road and units sit darker than the background. The eye gets pulled to the edges instead of the track. This is the main reason for the "dull mush".
2. **The haze is built into the settings.** Linear fog of sky color (`0.64/0.79/0.93`) from 45 to 120 m, while the camera hangs at about 25 m and looks 17 m ahead. The whole top third of the track is fogged into bluish-grey, and the road "dissolves" before the enemies even appear.
3. **Flat lighting.** Ambient is Trilight with a bright sky (`0.78/0.84/0.95`) and equator, sun at 1.3. Shadow and lit sides differ by maybe 25–30%, so volumes read as flat. Neutral tonemapping flattens the picture further.
4. **The three lane colors fight each other.** Blue, red and gold strips are all low-saturation and all the same brightness. They don't zone anything; they just add noise. Meaning is encoded with paint on asphalt, which also competes with the gates and crates.
5. **The squad is a dark-olive mush.** The player's soldiers and the enemies share one Toony WW1 atlas material (`TT_WW1_demo_material`). From a 50° camera you mostly see **helmets and shoulders**, and on both sides they're khaki/olive. There's nothing tying 30–250 soldiers into one silhouette.
6. **Units float.** On Mobile: 1 cascade, 1024 shadow map, no soft shadows, 50 m shadow distance. Shadows from tiny soldiers are smeared or missing entirely (Toony renderers don't receive shadows). There are no contact shadows.
7. **Random scatter along the edges.** `WorldBuilder.Decor()` picks one of six props per random slot every 3–6 m (crates, sandbags, barrels, tents, hedgehogs, towers). There's no rhythm or zoning, and the tall towers and tents pull the eye.
8. **Gates and crates are boxes with text.** No frame, no glow, no icons. You have to read the number to know what an object does. The "before/after shooting" state differs only by panel color.
9. **Weak impact.** Tracers are thin boxes (`0.07×0.8`). Muzzle flashes are capped at 12 per frame. Bloom has threshold 0.95 and intensity 0.6, so emissives barely glow. Enemy hit flash is a material swap with no knockback, and there are no decals or shockwaves from explosions.

Overall the frame has no "foreground / middle / background" and no single point of contrast. Everything is the same in value and saturation.

---

## 1. Light, color and atmosphere

### Value and saturation layers (the core rule)

| Layer | What's in it | Value (V) | Saturation (S) |
|---|---|---|---|
| **Background** | ground outside the road, berms, distant props | 45–60% | 20–35% |
| **Track** | asphalt, concrete corridors, markings | 25–45% (asphalt) / 55–65% (concrete) | ≤10% |
| **Gameplay** | squad, enemies, gates, crates, barrels | 70–100% on accents | 70–100% on accents |
| **FX/UI** | tracers, flashes, numbers | HDR > 1 (glow) | as needed |

The gameplay layer must always be the brightest and most saturated. The background must always be calmer than the track.

### Palette

| Role | Color | Note |
|---|---|---|
| Ground (near field) | `#A88B5D` | down from `#DBC28C`: warm, but deeper |
| Ground (far field / berms) | `#8F7752` | darker toward the horizon = depth |
| Background military | khaki `#7D7656`, dark green `#4F5A3A`, concrete `#8E8B84` | all at S ≤ 30% |
| Asphalt (center, combat lane) | `#3B4048` | slightly cool so warm accents pop |
| Concrete slabs (side corridors) | `#6A6E73` | zoning by value, not hue |
| Markings | `#E9E4D6` (dashes), `#F2C230` (hazard stripe) | |
| **Player squad** | team color blue `#2D8CFF` (helmets/shoulders), uniform `#5E6B4A` | matches the HUD |
| Commander | gold `#FFC52E` | |
| **Enemies** | accent red `#E5402B` (armband/helmet), uniform grey `#565A5E` | |
| Positive gates | `#2D8CFF` → `#4CCB3A` | |
| Negative gates | `#E5402B` | |
| Rewards / crates | gold `#FFC52E`, fire rate `#2FB8D9`, damage `#FF8A1E` | already in `Upgrades.Color` |

Avoid acid neon: emissives only on small elements (tracers, gate glow, barrel caps), and in HDR at ×1.5–3, not ×5+.

### Directional light (`ProjectSetup.BuildScene`)

- **Rotation:** `(55°, -30°, 0)`. Shadows fall back and to the right, so they're visible from the camera and don't hide under the units. Soldiers and objects get a clear lit side.
- **Color / intensity:** `#FFF0D8`, 1.6–1.8 (up from 1.3).
- **Ambient** (keep Trilight, but lower and cooler):
  - sky `#8EA3C2`
  - equator `#857E72`
  - ground `#463D33`
  - Shadow sides then sit at 45–55% of lit sides and the volume reads.
- **Shadows (Mobile_RPAsset):**
  - shadow distance **35–40 m** (the camera sees about 40 m; more just wastes resolution);
  - shadow map **2048** on mid-tier phones (A55-class), 1024 on low-end via a second quality level;
  - 1 cascade, soft shadows off. A hard edge actually reads better in a stylized look.
  - Depth bias 1 / normal bias 0.6 so small soldiers don't lose their shadow.

### Contact shadows

The real shadow map won't give a crisp spot under every 0.95 m soldier at 2048 over 40 m. So:

- **Blob shadow under each unit:** a flat quad with a soft round texture, multiply/alpha ≈ 0.45, radius 0.35 m. Use a shared material with GPU instancing (one drawcall per type). Add it in `UnitFactory.Model` / `PlayerSquad.SpawnUnit` and in enemies.
- **Under large objects** (gates, crates, barrels, barricades): baked AO quads on the ground (a dark gradient around the base) plus regular shadow casting.
- Once the squad passes about 60 soldiers, turn off `castShadows` on the soldiers themselves and keep only the blobs. That saves the shadow pass and looks the same.

### Fog and horizon

- **Remove the horizon from the frame:** pitch the camera 3–5° steeper, or end the world with a designed backdrop (a ridge/dune line plus a dark berm wall at about 70–80 m) instead of empty sky and haze.
- **Fog only for depth:** Linear, start 55 m, end 140 m. Color `#B39D76`, the far ground in warm shade, **not sky color**. Then the fog reads as distance instead of grey mush, and enemies 30–40 m away stay fully contrasted.
- Background `cam.backgroundColor` = a warm light horizon `#D9C9A6`, picked so the sky→ground seam is invisible.

### Post-processing (`MakeVolumeProfile` → `VP_Game.asset`)

> Note: `MakeVolumeProfile` returns the existing asset without updating it. Change it to overwrite values on every run, otherwise new settings won't apply.

- Tonemapping **Neutral** (ACES burns the reds and the HUD blues); contrast is handled separately.
- Color Adjustments: contrast **+18**, saturation **+8**, post exposure 0.
- Shadows/Midtones/Highlights: shadows slightly cool (`#E6EEFF`-ish), highlights slightly warm. This gives cool-shadow/warm-light separation with no extra cost.
- Bloom: threshold **1.0**, intensity 0.5, scatter 0.6. Only HDR emissives glow (tracers, flashes, gate rims), never white surfaces.
- Vignette 0.18 (focus on the center).
- Render scale 0.8 + FXAA is fine; don't add MSAA on mobile.

---

## 2. Track and surroundings

### Track zoning (`WorldBuilder.Build`)

Replace the three tinted strips with **one material language, zoning by value and markings**:

```
 berm │ curb│ side corridor │wall│  COMBAT LANE  │wall│ side corridor │curb│ berm
      │▓▓▓▓│ concrete slabs │ ▌  │   asphalt     │ ▌  │ concrete slabs│▓▓▓▓│
      │    │  #6A6E73       │    │   #3B4048     │    │   #6A6E73     │    │
      │    │  seams every 3 m│   │ white dashes  │    │               │    │
```

- **Center lane (combat):** dark asphalt, white side dashes 0.12 m. At the start, a yellow-black **hazard stripe** across the lane (the corridor entry line).
- **Side lanes:** lighter concrete slabs with seams every 3 m. The seams sell the conveyor motion better than dashes.
- **Lane meaning** isn't painted onto the whole lane. Show it with **small floor decals at the lane entrance:** blue chevrons with a soldier icon on the left, gold with a crate on the right, and the objects themselves. That's enough.
- Curbs: low concrete block with an occasional yellow-black stripe (rhythm every 6 m).

### Surroundings: a modular "frontline / range" kit instead of scatter

Rules:
1. **Nothing random in the 3 m strip next to the road**, only a continuous **sandbag berm** (one low line, 0.6 m, `#8C7A55`). It frames the track as a "corridor".
2. Props go into **bays every 15 m**, alternating sides. Each bay is a pre-made composition (prefab), not a random pick:
   - **A:** sandbags + barrel stack + crates (low, ≤1.2 m);
   - **B:** tent with camo net (the net is darker than the ground and gives a deep spot);
   - **C:** hedgehogs in a **neat row of 3** (not one tilted one);
   - **D:** watchtower, only every 45 m and only 10+ m from the road.
3. Everything in the background follows the background palette (S ≤ 30%, V 45–60%). Nothing red or blue in the surroundings: those colors belong to gameplay.
4. Height grows with distance from the road: low stuff near it, tall stuff farther out (towers, nets, the backdrop ridge). The track stays open and the eye isn't caught by the edges.
5. The far backdrop (70–90 m) is a continuous dark line of berm/dunes with a few tower silhouettes. It closes the frame instead of haze.

Implementation: replace `Decor()` with `Bay(type)` on a fixed grid (`z = MinZ + k·15`, side = `k % 2`), keep the `rng` only for small variations (rotation ±10°, barrel count).

---

## 3. Silhouettes and readability

### Making your own squad stand out

At 30–250 soldiers the player perceives **a mass, not individuals**. So the mass needs one readable shape and one color:

1. **Team color on helmets and shoulders.** From this camera they're about 60% of a soldier's visible pixels. In practice: duplicate the Toony atlas into `TT_player_atlas.png`, repaint the helmet and shoulder-strap regions blue `#2D8CFF`, and give the player models a separate material. This preserves GPU skinning and doesn't break batching.
2. **A blue disc/ring under the squad:** a soft ground decal sized to `Formation.Radius` (alpha 0.35, slightly emissive edge). It unifies the crowd into a "spot" and shows its size even when soldiers overlap.
3. **Rim highlight on player units:** in a toon shader (see the plan), fresnel ×0.35 in team color. The silhouette separates from the asphalt and from the enemy crowd. Enemies don't get a rim.
4. **Commander:** gold helmet, ×1.3 scale, a small flag on his back. He's always visible in the crowd and is "the last to fall".
5. **The squad badge in the HUD** is already blue, which matches.

### Gates and modifiers

Current: two posts + a flat colored panel + text.

**New gate:**

```
   ┌▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀┐   ← top beam with chevrons ▼▼▼ (direction of travel)
   █ ┌───────────┐ █
   █ │   (👤)    │ █   ← icon of the gate type
   █ │    +12    │ █   ← huge number, white with dark outline
   █ │▓▓▓▓▓▓▓░░░░│ █   ← "fill" bar: how much more to shoot to flip it
   █ └───────────┘ █
  ▄█▄             ▄█▄  ← heavy feet + AO spot on the ground
```

- **Shape:** thick posts (0.35 m) + beam = a readable "arch" silhouette, distinct from crates and enemies.
- **Panel:** solid color (no glass) + an emissive rim around the edge at HDR ×1.8, so the gate glows slightly through bloom.
- **States:**
  - **negative:** red, number `-8`, slow pulse of the rim (danger);
  - **being shot:** each hit flickers the panel white for 0.05 s and the fill bar grows. The number rolls (same `RollingCounter` logic);
  - **flip to positive:** the panel scale-punches ×1.15, a color sweep runs bottom to top, a burst of blue sparks, a "flip" sound and a light haptic;
  - **positive:** blue/green, steady glow;
  - **passed through:** the panel shatters into 6–8 colored shards and the number flies up into the squad (this ties in with the `+12` callout).
- **Multiplier gates (×2)** get gold rims and the number in a circle. They must differ by shape, not just by digit.

**Upgrade crates** (`UpgradeCrate`): instead of the word `FIRE RATE +25%` on the box, an **icon on the front face** (lightning / bullet / rifle, the same as in the HUD), the box colored by type, metal corner plates, and an emissive icon line. HP as a thin bar above the crate instead of a large number. When the crate breaks, the icon flies up to the matching buff badge in the HUD.

### Enemy types and destructibles: tell them apart by silhouette first

| Type | Silhouette / size | Color accent | Behavior readability |
|---|---|---|---|
| **Runner** | standard, slightly lowered | red armband | fast; a short dust trail behind |
| **Rifleman** | standard + raised rifle | red helmet + **red triangle ▲ above the head** | before shooting, a **thin red laser sight line** along its lane for 0.4 s (telegraph) |
| **Heavy** | ×1.4, wide shoulders, plate armor | dark steel + red visor | grey shield on the HP bar: weak guns ping off (grey sparks instead of orange) |
| **MG nest / turret** | low ring of sandbags + barrel | red light on the barrel | the barrel turns toward the target; muzzle flash ×2 |
| **Boss** | ×3–4, unique silhouette (tank/helicopter) | red lights, a red ring on the ground | phases change the lights; a warning circle before strikes |

**Destructibles:**
- **Barrels:** bright red `#D9342B` with a white stripe and a glowing yellow cap. You can see from across the screen that it will blow.
- **Barricades:** planks + steel posts, **3 damage stages** (whole → cracked → half the planks gone), switching at 66% / 33% HP. You can see how close it is to breaking without reading the number.
- **Soldier pickups (+1):** keep the blue tint (it's gameplay) but simplify: a low box with the soldier icon, no text. Arranged as a "trail" (already done).

---

## 4. Combat impact (juice)

Principle: **many small, short, bright accents; heavy effects only on important events.** Camera and screen-level effects only for explosions, the boss and big upgrades.

### Shooting
- **Muzzle flash:** an additive "star" billboard (4 rays), 0.05 s, random rotation, HDR ×2.5 in the tracer color. Raise the per-frame cap from 12 to 20, but scale them down with squad size (with 200 soldiers, a "spark carpet" is enough).
- **Tracers:** instead of `0.07×0.8` boxes, a stretched billboard: 0.05 m core + 0.18 m glow, 1.2 m long, HDR ×2 (picked up by bloom). Tier colors already exist (`Weapons.TracerColor`). Rockets get their own: a smoke trail + head.
- **Shooter recoil:** in the shooter animator, or procedurally: a short z-kick of 0.04 m on the gun.

### Hits
- **Hit flash:** keep the white flash (0.06 s), and add a **knockback** of 0.08–0.12 m backward and a squash `scale.y ×0.9` for 0.08 s.
- **Hit sparks:** directed against the bullet, 3–5 particles. Against armor: grey + "ping" sound.
- **Kill:** the existing `DeathFly` + a small dust puff + a short "pop" of the blob shadow.

### Destruction
- **Crates and barricades:** 6–10 chunks colored like the object (chunk pool) + dust + a **shockwave ring** on the ground (decal, 0.3 s scale 0→3 m with fade).
- **Barrels:** fireball (existing `Fx.Explosion`) + shockwave ring + a **scorch decal** that stays on the ground + shake 0.25 + **hitstop 40 ms** (`Time.timeScale = 0.05` for 2–3 frames).
- **Boss kill:** the existing slow-mo + a white screen flash for 0.08 s + a large ring.

### Camera (no motion sickness)
- **No shake on regular shots.** Ever.
- Shake: barrel 0.2–0.25, boss hit on the squad 0.3, boss kill 0.7 (exists). Use a low-frequency noise shake (Perlin, 12–18 Hz, 0.15–0.3 s), not `Random.insideUnitSphere` every frame. The current shake jitters too much.
- **FOV kick** +1.5° for 0.2 s on a weapon upgrade (`GUN UP!`): it feels like a "boost".
- The existing smooth zoom-out as the crowd grows is good; keep it.

---

## 5. Implementation plan

Order is "maximum effect for minimum work".

### Stage 1: light, color, track (1–2 days, ~60% of the visual gain)
| What | Where |
|---|---|
| Light, ambient, fog, background, camera pitch | `ProjectSetup.BuildScene` |
| Post-processing values + overwrite the existing profile | `ProjectSetup.MakeVolumeProfile` → `VP_Game.asset` |
| Shadow distance 40, map 2048, bias | `Assets/Settings/Mobile_RPAsset.asset` |
| Palette: darker ground, one asphalt, concrete side corridors, markings, hazard stripe | `WorldBuilder` constants + `Build` |
| Remove `Decor()`, add a berm along the road + bays every 15 m + backdrop | `WorldBuilder` |
| Blob shadows (instanced quad material) | new `Art.Blob()`, call in `UnitFactory.Model` / enemies |

### Stage 2: silhouettes and objects (2–3 days)
| What | Where |
|---|---|
| Player atlas with blue helmets/shoulders + separate material; gold commander | `Assets/ToonyTinyPeople/...` (texture copy), `UnitFactory`, `PlayerSquad.SpawnUnit` |
| Squad ground disc | `PlayerSquad` |
| New gate: arch, panel + emissive rim, icon, fill bar, flip/shatter states | `ShootableGate` |
| Crates with icons, HP bar instead of text | `LaneObjects.UpgradeCrate`, `Pickup` |
| Enemy differentiation: rifleman ▲ + laser telegraph, heavy ×1.4 + grey sparks against armor | `LaneObjects.Enemy` |
| Barrels: color + glowing cap; barricades: 3 damage stages | `ExplosiveBarrel`, `Barricade` |

### Stage 3: juice (2 days)
| What | Where |
|---|---|
| Tracers as billboards + HDR | `Effects.Tracers` + a new additive material in `GameAssets` |
| Muzzle-flash star, hit sparks against the bullet | `Fx` (new flash texture, `sparks` direction) |
| Knockback + squash on hit | `LaneObjects.Enemy.TakeHit` |
| Chunk pool, shockwave ring, scorch decal | new `Debris` / `GroundFx` in `Fx` |
| Hitstop, Perlin shake, FOV kick | `CameraFollow`, a `HitStop` helper in `GameManager` |

### Stage 4: shader and polish (as time allows)
- One **Toon Lit Shader Graph** for everything (replacing `M_Lit`): 2-step light ramp (smoothstep 0.45–0.55), main-light shadows, SH ambient, optional rim (`_RimColor`, `_RimStrength`). Same cost as URP Simple Lit, but volume reads more clearly.
- Optionally, a **baked-animation crowd** (vertex animation texture + GPU instancing) instead of 200 SkinnedMeshRenderers. That's both performance and the ability to make the crowd denser.

### Mobile budget (keep an eye on it)
- No realtime point/spot lights; all glow comes from emissive + bloom.
- Transparent overdraw: gate panels are opaque; particles small and short.
- Instancing for everything repeated (`Art.Lit` already caches materials with instancing): blobs, chunks, decals.
- Check each stage on the A55 for FPS (target 60) via a dev build with the profiler (`deploy-android.ps1 -Dev`).

### What not to do
- No glassy translucent gates, no neon outlines around everything, no fog for "atmosphere" in the play area.
- Don't paint whole lanes in gameplay colors: color belongs to objects.
- Don't add detail to the background: the less there is beside the road, the better the road reads.
