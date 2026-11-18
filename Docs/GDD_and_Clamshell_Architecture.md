# IronFauna — Game Design Summary & Clamshell Reference

**IronFauna** is a physics mech brawler (Unity 6 URP): Gang Beasts-style active-ragdoll robots beat each other up
with MMA moves, grapple, throw and slam each other and tear each other apart limb from limb. Every strike is a
readable, committed move with real weight (For Honor / Dark Souls feel). Targets: 8-player online and local co-op,
destructible arenas, destructible fighters. The garage builds each fighter from interchangeable clamshell armor.

## 1. Look
- Realistic hard-surface mecha: physically based metal and coated paint, exposed skeleton with glowing joint cores.
- Palette (`IronFaunaPalette.cs`): `CrimsonFront` `#C8282E` · `MaroonRear` `#6E1E26` · `GunmetalFrame` `#3A3F46` ·
  `OffWhitePanel` `#F0ECE1` · `HazardYellow` `#E6B800` · `OrangeJointCore` `#FF7A1A` · `CyanEnergy` `#24E5FF`.
- Brawls take place on a rocky hill island under an overcast sky.

## 2. Clamshell Construction Rules
- **Single-bone clamping:** every armor piece is two halves (front in crimson, back in maroon) clamped onto one bone,
  locked by toggle latches across a seam that runs down the sides.
- **Exposed joint hubs:** armor stops short of every joint (hip, knee, ankle, neck, shoulder, elbow, wrist, waist)
  with 45° clearance bevels.
- **180 interchangeable halves** in 10 sets (Ironclad … Aegis):
  - Legs (60): thigh, calf, toe and heel, front and back.
  - Body (20): chest front + backplate.
  - Arms (80): shoulder, upper arm, forearm, hand, front and back.
  - Heads (20): visor front + cranium back.
- Weight classes (Light / Medium / Heavy) for torso and legs change the skeleton, the armor envelope and the stats.

## 3. Fighting
- Strikes: jab, cross, hook, uppercut, front kick, roundhouse, plus 12 scroll-selected specials (spinning backfist,
  superman punch, haymaker, spinning elbow, side kick, axe kick, spinning hook kick, flying knee, headbutt,
  shoulder charge …).
- Grapples: grab, drag, throw, slam, tear limbs off and swing them as clubs.
- Damage is physical: per-limb health, armor plates crack off, limbs tear away; balance breaks cause knockdowns.
- Ring-outs: the island has cliffs.

See `CLAUDE.md` (section 9) for the code map.
