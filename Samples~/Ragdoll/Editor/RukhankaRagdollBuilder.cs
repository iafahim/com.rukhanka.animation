using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Physics.Authoring;
using UnityEditor;
using UnityEngine;

namespace RukhankaRagdoll.Editor
{
    // Generates a DOTS-physics ragdoll (PhysicsBodyAuthoring + capsule PhysicsShapeAuthoring + Ragdoll/Hinge
    // joints) for ANY humanoid Animator, driven by HumanBodyBones. The bodies are authored Kinematic (inert /
    // free) by default; the runtime toggle (later) flips them Dynamic + bridges the rig bones to follow them.
    // A standard Unity-Ragdoll-Wizard-style 11-body humanoid: hips, spine, head, L/R upper+lower arm & leg.
    public static class RukhankaRagdollBuilder
    {
        private enum JointKind { None, Ball, Hinge }

        private struct BoneSpec
        {
            public HumanBodyBones Bone;
            public HumanBodyBones[] DirChildren; // first existing bone gives capsule direction + length
            public HumanBodyBones Parent;        // parent ragdoll bone; (HumanBodyBones)(-1) = root, no joint
            public JointKind Joint;
            public float RadiusScale;            // radius = boneLen * scale, clamped
            public float Mass;
            public float ConeAngle;              // ball: cone + perpendicular half-angle (deg)
            public float TwistRange;             // ball: +/- twist (deg)
            public float HingeMin, HingeMax;     // hinge: angle limits (deg)
        }

        private const HumanBodyBones Root = (HumanBodyBones)(-1);

        private static readonly BoneSpec[] Specs =
        {
            new() { Bone = HumanBodyBones.Hips, DirChildren = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest }, Parent = Root, Joint = JointKind.None, RadiusScale = 0.6f, Mass = 8f },
            new() { Bone = HumanBodyBones.Spine, DirChildren = new[] { HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head }, Parent = HumanBodyBones.Hips, Joint = JointKind.Ball, RadiusScale = 0.5f, Mass = 10f, ConeAngle = 25f, TwistRange = 15f },
            new() { Bone = HumanBodyBones.Head, DirChildren = null, Parent = HumanBodyBones.Spine, Joint = JointKind.Ball, RadiusScale = 0.5f, Mass = 5f, ConeAngle = 25f, TwistRange = 25f },

            new() { Bone = HumanBodyBones.LeftUpperArm, DirChildren = new[] { HumanBodyBones.LeftLowerArm }, Parent = HumanBodyBones.Spine, Joint = JointKind.Ball, RadiusScale = 0.28f, Mass = 2.5f, ConeAngle = 60f, TwistRange = 45f },
            new() { Bone = HumanBodyBones.LeftLowerArm, DirChildren = new[] { HumanBodyBones.LeftHand }, Parent = HumanBodyBones.LeftUpperArm, Joint = JointKind.Hinge, RadiusScale = 0.25f, Mass = 1.5f, HingeMin = 0f, HingeMax = 150f },
            new() { Bone = HumanBodyBones.RightUpperArm, DirChildren = new[] { HumanBodyBones.RightLowerArm }, Parent = HumanBodyBones.Spine, Joint = JointKind.Ball, RadiusScale = 0.28f, Mass = 2.5f, ConeAngle = 60f, TwistRange = 45f },
            new() { Bone = HumanBodyBones.RightLowerArm, DirChildren = new[] { HumanBodyBones.RightHand }, Parent = HumanBodyBones.RightUpperArm, Joint = JointKind.Hinge, RadiusScale = 0.25f, Mass = 1.5f, HingeMin = 0f, HingeMax = 150f },

            new() { Bone = HumanBodyBones.LeftUpperLeg, DirChildren = new[] { HumanBodyBones.LeftLowerLeg }, Parent = HumanBodyBones.Hips, Joint = JointKind.Ball, RadiusScale = 0.3f, Mass = 7f, ConeAngle = 45f, TwistRange = 20f },
            new() { Bone = HumanBodyBones.LeftLowerLeg, DirChildren = new[] { HumanBodyBones.LeftFoot }, Parent = HumanBodyBones.LeftUpperLeg, Joint = JointKind.Hinge, RadiusScale = 0.28f, Mass = 4f, HingeMin = -150f, HingeMax = 0f },
            new() { Bone = HumanBodyBones.RightUpperLeg, DirChildren = new[] { HumanBodyBones.RightLowerLeg }, Parent = HumanBodyBones.Hips, Joint = JointKind.Ball, RadiusScale = 0.3f, Mass = 7f, ConeAngle = 45f, TwistRange = 20f },
            new() { Bone = HumanBodyBones.RightLowerLeg, DirChildren = new[] { HumanBodyBones.RightFoot }, Parent = HumanBodyBones.RightUpperLeg, Joint = JointKind.Hinge, RadiusScale = 0.28f, Mass = 4f, HingeMin = -150f, HingeMax = 0f },
        };

        [MenuItem("Tools/Rukhanka Ragdoll/Build On Selected")]
        private static void BuildOnSelected()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogError("[Ragdoll] Select a GameObject with a Humanoid Animator first.");
                return;
            }

            var anm = go.GetComponentInChildren<Animator>();
            if (anm == null || !anm.isHuman)
            {
                Debug.LogError("[Ragdoll] Selection has no Humanoid Animator.");
                return;
            }

            BuildRagdoll(anm);
        }

        public static GameObject BuildRagdoll(Animator anm)
        {
            var existing = anm.transform.Find("Ragdoll");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var root = new GameObject("Ragdoll");
            Undo.RegisterCreatedObjectUndo(root, "Build Ragdoll");
            Undo.SetTransformParent(root.transform, anm.transform, "Build Ragdoll");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var bodies = new Dictionary<HumanBodyBones, PhysicsBodyAuthoring>();

            // Pass 1 — bodies + capsules.
            foreach (var s in Specs)
            {
                var boneT = anm.GetBoneTransform(s.Bone);
                if (boneT == null)
                {
                    continue;
                }

                var childT = FirstBone(anm, s.DirChildren);
                float3 start = boneT.position;
                float3 dir;
                float len;
                if (childT != null)
                {
                    dir = (float3)childT.position - start;
                    len = math.length(dir);
                }
                else
                {
                    dir = boneT.up; // head fallback: a short capsule up the bone's own axis
                    len = 0.18f;
                }

                if (len < 1e-3f)
                {
                    len = 0.1f;
                    dir = new float3(0f, 1f, 0f);
                }

                var ndir = math.normalize(dir);
                float3 mid = start + (ndir * (len * 0.5f));

                var bodyGO = new GameObject(s.Bone.ToString());
                Undo.RegisterCreatedObjectUndo(bodyGO, "Build Ragdoll");
                Undo.SetTransformParent(bodyGO.transform, root.transform, "Build Ragdoll");
                bodyGO.transform.position = mid;
                bodyGO.transform.rotation = Quaternion.LookRotation((Vector3)ndir, StableUp(ndir));
                bodyGO.transform.localScale = Vector3.one;

                var body = bodyGO.AddComponent<PhysicsBodyAuthoring>();
                // Authored DYNAMIC so it bakes a real (finite-mass) PhysicsMass — required so the runtime can
                // switch it. Free-off is achieved at runtime by PhysicsMassOverride{IsKinematic=1} + Disabled
                // (a kinematic-AUTHORED body bakes infinite mass and can never be released to dynamic).
                body.MotionType = BodyMotionType.Dynamic;
                body.Mass = s.Mass;
                body.LinearDamping = 0.05f;
                body.AngularDamping = 0.05f;

                var radius = math.clamp(len * s.RadiusScale, 0.03f, 0.15f);
                var shape = bodyGO.AddComponent<PhysicsShapeAuthoring>();
                shape.SetCapsule(new CapsuleGeometryAuthoring
                {
                    Orientation = quaternion.identity, // capsule runs along local +Z, which we aligned to the bone
                    Center = float3.zero,
                    Height = math.max(len, radius * 2.1f),
                    Radius = radius,
                });

                // No self-collision: ragdoll capsules overlap at joints / the rest pose, and with self-collision
                // on the solver violently shoves them apart (explodes). Put every ragdoll body in a dedicated
                // category and exclude that category from what it collides with — so they collide with the world
                // but never each other.
                shape.BelongsTo = new PhysicsCategoryTags { Category31 = true };
                var collides = PhysicsCategoryTags.Everything;
                collides.Category31 = false;
                shape.CollidesWith = collides;

                bodies[s.Bone] = body;
            }

            // Pass 2 — joints (need both bodies to exist).
            foreach (var s in Specs)
            {
                if (s.Joint == JointKind.None)
                {
                    continue;
                }

                if (!bodies.TryGetValue(s.Bone, out var body) || !bodies.TryGetValue(s.Parent, out var parentBody))
                {
                    continue;
                }

                var bodyGO = body.gameObject;
                var boneT = anm.GetBoneTransform(s.Bone);
                float3 anchorLocal = bodyGO.transform.InverseTransformPoint(boneT.position); // proximal joint point

                if (s.Joint == JointKind.Ball)
                {
                    var j = Undo.AddComponent<RagdollJoint>(bodyGO);
                    j.ConnectedBody = parentBody;
                    j.PositionLocal = anchorLocal;
                    j.AutoSetConnected = true;
                    j.TwistAxisLocal = new float3(0f, 0f, 1f);        // along the bone
                    j.PerpendicularAxisLocal = new float3(1f, 0f, 0f);
                    j.MaxConeAngle = s.ConeAngle;
                    j.MinPerpendicularAngle = -s.ConeAngle;
                    j.MaxPerpendicularAngle = s.ConeAngle;
                    j.MinTwistAngle = -s.TwistRange;
                    j.MaxTwistAngle = s.TwistRange;
                }
                else
                {
                    var j = Undo.AddComponent<LimitedHingeJoint>(bodyGO);
                    j.ConnectedBody = parentBody;
                    j.PositionLocal = anchorLocal;
                    j.AutoSetConnected = true;
                    j.HingeAxisLocal = new float3(1f, 0f, 0f);        // bend around local X
                    j.PerpendicularAxisLocal = new float3(0f, 0f, 1f);
                    j.MinAngle = s.HingeMin;
                    j.MaxAngle = s.HingeMax;
                }
            }

            Selection.activeGameObject = root;
            Debug.Log($"[Ragdoll] Built {bodies.Count} bodies on '{anm.name}'.");
            return root;
        }

        private static Transform FirstBone(Animator a, HumanBodyBones[] candidates)
        {
            if (candidates == null)
            {
                return null;
            }

            foreach (var b in candidates)
            {
                var t = a.GetBoneTransform(b);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        // LookRotation degenerates when the bone direction is parallel to the up reference (legs/spine are
        // vertical). Pick an up that is not parallel to the bone.
        private static Vector3 StableUp(float3 dir)
        {
            return math.abs(math.dot(dir, new float3(0f, 1f, 0f))) > 0.99f ? Vector3.forward : Vector3.up;
        }
    }
}
