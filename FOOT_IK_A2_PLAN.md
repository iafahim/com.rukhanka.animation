# Foot IK (Option A2) — Implementation Plan

## ▶ TODO NEXT SESSION — live on-character validation (parked 2026-06-14, user does tomorrow)

Everything is built + compile/data/wiring-verified. The ONLY remaining check is the on-character
visual/numeric one, blocked because no animated Rukhanka humanoid is live (Main Scene is a
bootstrap; the `Arvex_RIG2` test char is a bare Animator → 0 rigs in play mode; the
NIbir888/Grruzam_DualBlade_Animationsource repo is plain animation source (385 FBX + classic
Animator showcase), NOT a DOTS setup — won't drive FootIKSystem).

To finish:
1. Get an animated Rukhanka character on screen — via the game's real spawn/stage/input flow
   (clips default `applyFootIK=true`, so any timeline anim now exercises FootIKSystem), OR wire a
   minimal Rukhanka rig + idle timeline onto `Arvex_RIG2`.
2. In play mode, find the entity: query `RigDefinitionComponent` where `rigBlob.Value.humanData.IsValid`
   and an `AnimationToProcessComponent` has `applyFootIK && animation.Value.footIKGoals.isValid`.
3. Numeric check via unity-cli: build `AnimationStream` for that rig, read the LeftFoot/RightFoot
   bone world pose, compare to `BoneTransform.Multiply(hipsWorld, footIKGoals.Sample(atp.time).{left,right}Foot)`.
   With FootIKSystem ON the foot should ≈ goal; toggle the system Enabled=false, step a frame, and it
   should drift OFF the goal → proves causation.
4. Then eyeball: knee bends forward, no pop on idle↔walk blend, feet planted.



Goal: make the existing `applyFootIK` per-clip toggle (plumbed from
`BovineLabs.Timeline.Animation` into `AnimationToProcessComponent.applyFootIK`) actually
stabilize the feet, the way Unity's per-state **Foot IK** checkbox does — but on the
Rukhanka rig.

## Why the toggle did nothing (root cause)

`applyFootIK` is read by **nothing** in Rukhanka (it appears exactly once — its declaration
in `AnimationProcessSystemComponents.cs`). Unity's Foot IK stabilizes the feet to the
`LeftFootT/Q` / `RightFootT/Q` **goal curves** baked into humanoid clips. Rukhanka's clip
baker **discards** those curves (`BindingType` has no IK-goal kind), so at runtime there is
no goal to solve to.

## Approach: A2 — bake rig-relative foot goal poses (NOT Unity's opaque goal space)

Instead of reverse-engineering Unity's normalized humanoid goal space (which Rukhanka's
author explicitly never finished — see the comment in `AnimationClipBaker.SampleMissingCurves`),
we sample the **foot bone poses directly** at bake time, in a coordinate space we control
(hips/root-relative `BoneTransform`), and solve the legs to them at runtime with the existing
`TwoBoneIK` math. Same designer-visible result (feet stay where the animation placed them, no
retarget slide / float), fully under our control.

## Phases

### Phase 1 — data layer — DONE & EDITOR-VERIFIED

Verified live via unity-cli (2026-06-14): both Rukhanka assemblies compiled with the new types
(`Rukhanka.Runtime.FootIKGoalSet/Sample`, `AnimationClipBlob.footIKGoals`,
`Rukhanka.Hybrid.AnimationClipBaker.BakeFootIKGoals`); console clean. A bake-equivalent sampling
test on a real humanoid rig produced sane hips-relative foot goals (feet below hips, laterally
separated, stable over time). Original spec below.



- **`AnimationClipBlob.cs`**: add `FootIKGoalSet footIKGoals` — a fixed-rate sampling of the
  left & right foot pose over the clip, in **hips-relative** space, plus an `isValid` flag
  (true only for humanoid clips where both feet resolved).
- **`AnimationClipBaker.cs`**: `BakeFootIKGoals(...)` — for humanoid clips, sample the clip at
  a fixed rate (reuse the `ac.SampleAnimation(go, t)` + `CreateKeyframeTimes` pattern from
  `SampleUnityAnimation`), read `HumanBodyBones.LeftFoot` / `RightFoot` relative to
  `HumanBodyBones.Hips`, store as `BoneTransform`. Hook it from `CreateAnimationBlobAsset`.
- Runtime lookup helper `FootIKGoalSet.Sample(normalizedTime)` → `(left, right)` poses with
  linear interpolation + loop wrap.

### Phase 2 — runtime solver — CODE DONE & COMPILE-VERIFIED (visual tuning pending)

Implemented as `Rukhanka.Runtime/IK/FootIKSystem.cs` (pure runtime addition — NO rig-baker/blob
change needed: leg-chain bone indices read live from `humanData.humanBoneToSkeletonBoneIndices`,
which is indexed by `HumanBodyBones`). Verified via unity-cli: compiles, loads as `ISystem` in
`Rukhanka.Runtime`, `[UpdateInGroup(RukhankaAnimationInjectionSystemGroup)]` +
`[UpdateAfter(TwoBoneIKSystem)]` resolved, console clean. The two-bone solve mirrors the proven
`TwoBoneIKSystem` math, operating via `AnimationStream` bone indices.

STILL PENDING — play-mode behavioural/visual validation (needs a Rukhanka humanoid playing a
Timeline animation clip with applyFootIK=true; clips default applyFootIK=true so any existing
timeline anim now exercises it): foot reaches goal, knee bends forward, no pop on blends. Note:
for same-rig (non-retargeted) clips the goal ≈ Rukhanka's own result, so the visible effect is
the CORRECTION of Rukhanka's humanoid-sampling drift vs Unity's bake — may be subtle; the bigger
payoff is as the basis for Option B grounding.

Original design notes:


- **Rig leg chains**: at rig-bake time, resolve and store the per-side chain bone indices
  (UpperLeg → LowerLeg → Foot → Toes) into `RigDefinitionBlob` (new `FootIKChains` field),
  found via `HumanBodyBones`. Runtime needs the bone *indices in the animation rig*, not names.
- **New system `FootIKSystem`** in `RukhankaAnimationInjectionSystemGroup` (same group as
  `TwoBoneIKSystem`, after sampling, before application). Per character:
  1. Find the dominant locomotion ATP that has `applyFootIK == true` (highest weight on the
     base layer). Read its clip blob + `time`.
  2. `goal = clipBlob.footIKGoals.Sample(time)` → left/right hips-relative foot pose.
  3. Convert to rig-relative world via the **current animated hips world pose**
     (`AnimationStream.GetWorldPose(hipsIndex)`), so the goal tracks the live body.
  4. Run the existing two-bone solve (lift the math out of `TwoBoneIKSystem.TwoBoneIKJob`
     into a shared static in `IKCommon`) on each leg toward the goal position, then set the
     foot world rotation to the goal rotation. Weight by the ATP weight × a per-clip foot-IK
     weight.
- **Knee hint**: use the current animated knee position as the bend hint (keeps natural bend
  direction — the editor-validation item).

### Phase 3 — Timeline wiring + authoring polish

- Confirm `applyFootIK` survives all the way to the chosen ATP (it already does via
  `BlendGroupEntry`/`SmoothBlendGroupEntry`/`AnimationToProcessComponent`).
- Optional per-clip `footIKWeight` (0..1) instead of a bare bool, for blends.
- B (separate clip): procedural raycast-to-floor grounding reuses Phase 2's shared solver with
  a raycast-derived goal instead of the baked goal. Lands as its own Timeline clip later.

## Editor validation checklist (Phase 2)

- [ ] Foot plants at the baked position; no sliding on a locomotion loop.
- [ ] Knee bends forward (hint correct), not backward / sideways.
- [ ] Foot rotation matches ground/animation (no twisted ankle).
- [ ] Weight 0 == pure animation (no-op); weight 1 == full goal.
- [ ] Blending two clips (e.g. idle↔walk) doesn't pop.
- [ ] Non-humanoid clips: `isValid == false`, solver skips, zero cost.

## Blob-cache note

Adding `footIKGoals` changes `AnimationClipBlob` layout — bump/clear the baked-animation cache
(`BlobCache`) so old cached clips are re-baked, or stale blobs will deserialize wrong.
