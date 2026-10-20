using System;
using System.Collections.Generic;
using VRCLearn.GestureManagerPro.Control;
using UnityEngine;

// Geometric and injected-tracking checks. These do not establish VRChat client or physical-device parity.
public static class UnityProTrackingChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckMixing(check);
        CheckGeometry(check);
        CheckHumanoid(check);
    }

    private static void CheckMixing(Action<bool, string> check)
    {
        var mixer = new TrackingMixer();
        var origin = new Pose(new Vector3(10f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
        var provider = new TrackingFrame(new[] { Device("controller", TrackingRole.LeftHand, new Vector3(1f, 1f, 0f)) }, 1d, origin: origin);
        var result = mixer.Mix(provider);
        check(result.TryGet(TrackingRole.LeftHand, out var transformed) &&
            Vector3.Distance(transformed.Pose.position, new Vector3(10f, 3f, 2f)) < 0.0001f,
            "Tracking sources apply their translated and rotated origins in canonical Unity world metres");
        check(result.Origin.position == Vector3.zero && Quaternion.Angle(result.Origin.rotation, Quaternion.identity) < 0.0001f,
            "Mixed poses have an identity world-space origin so consumers cannot apply provider origins twice");
        var osc = new TrackingFrame(new[] { Device("osc-hip", TrackingRole.LeftHand, Vector3.one),
            Device("controller", TrackingRole.RightHand, Vector3.one * 99f) }, 2d);
        mixer.Assign("osc-hip", TrackingRole.LeftHand);
        result = mixer.Mix(provider, osc);
        check(result.Devices.Count == 1 && result.TryGet(TrackingRole.LeftHand, out var manual) && manual.Id == "osc-hip",
            "Manual device assignments take priority over advertised roles and the same stable device cannot drive a second role");
        mixer.Assign("controller", TrackingRole.LeftHand);
        result = mixer.Mix(provider, osc);
        check(result.Devices.Count == 1 && result.TryGet(TrackingRole.LeftHand, out var reassigned) && reassigned.Id == "controller",
            "Reassigning a role removes its previous manual owner without making duplicate body constraints");
        mixer.Assign("controller", TrackingRole.Unassigned);
        check(mixer.Mix(provider).Devices.Count == 0, "A manually unassigned device does not return through its automatic role");
        mixer.ClearAssignment("controller");
        check(mixer.Mix(provider).TryGet(TrackingRole.LeftHand, out _), "Clearing a manual assignment restores provider role discovery");
        var all = new List<TrackedDevice>();
        for (var role = TrackingRole.Head; role <= TrackingRole.RightElbow; role++)
        {
            all.Add(Device(role.ToString(), role, Vector3.up));
            all.Add(Device("duplicate-" + role, role, Vector3.down));
        }
        check(new TrackingMixer().Mix(new TrackingFrame(all, 5d)).Devices.Count == 11,
            "An eleven-point source yields at most one head, hand, hip, foot, chest, knee and elbow role");
        var invalid = new TrackingFrame(new[] { Device("bad", TrackingRole.Head, new Vector3(float.NaN, 0f, 0f)) }, 4d);
        check(new TrackingMixer().Mix(invalid).Devices.Count == 0, "Nonfinite external poses cannot enter the mixed tracking frame");
        var badOrigin = new TrackingFrame(new[] { Device("head", TrackingRole.Head, Vector3.up) }, 4d,
            origin: new Pose(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f)));
        check(new TrackingMixer().Mix(badOrigin).Devices.Count == 0,
            "An invalid tracking origin cannot silently substitute a valid provider coordinate space");
    }

    private static void CheckGeometry(Action<bool, string> check)
    {
        var upper = new GameObject("Pro tracking two-bone fixture").transform;
        try
        {
            var lower = Child(upper, "Middle", Vector3.right);
            var end = Child(lower, "End", Vector3.right);
            var target = new Pose(new Vector3(1f, 1f, 0f), Quaternion.Euler(10f, 20f, 30f));
            HumanoidPoseSolver.SolveTwoBone(upper, lower, end, target, Vector3.up);
            check(Vector3.Distance(end.position, target.position) < 0.0001f && Quaternion.Angle(end.rotation, target.rotation) < 0.001f,
                "The analytic arm reaches a known reachable endpoint and applies controller rotation");
            check(Mathf.Abs(Vector3.Distance(upper.position, lower.position) - 1f) < 0.0001f &&
                Mathf.Abs(Vector3.Distance(lower.position, end.position) - 1f) < 0.0001f,
                "Two-bone reaching preserves both measured segment lengths instead of stretching the skeleton");
            check(Vector3.Distance(lower.position, Vector3.up) < 0.0001f,
                "A known bend hint chooses the expected elbow side of the reach plane");
            HumanoidPoseSolver.SolveTwoBone(upper, lower, end, new Pose(Vector3.right * 5f, Quaternion.identity), Vector3.up);
            check(Vector3.Distance(end.position, Vector3.right * 2f) < 0.0001f,
                "An unreachable target clamps at the physical two-bone reach rather than changing bone lengths");
            HumanoidPoseSolver.SolveTwoBone(upper, lower, end, new Pose(new Vector3(0.05f, 0.05f, 0f), Quaternion.identity));
            check(Vector3.Distance(end.position, new Vector3(0.05f, 0.05f, 0f)) < 0.0001f,
                "A nearly folded limb still reaches a short valid target without unstable division");
        }
        finally { UnityEngine.Object.DestroyImmediate(upper.gameObject); }
    }

    private static void CheckHumanoid(Action<bool, string> check)
    {
        GameObject root = null;
        Avatar human = null;
        HumanoidPoseSolver solver = null;
        try
        {
            var animator = CreateHumanoid(out human, out var bones);
            root = animator.gameObject;
            check(human && human.isValid && human.isHuman, "The tracking integration fixture builds a real valid Humanoid Avatar");
            if (!human || !human.isValid || !human.isHuman) return;
            var originals = new Dictionary<Transform, Pose>();
            foreach (var bone in bones.Values) originals[bone] = new Pose(bone.localPosition, bone.localRotation);
            var head = bones[HumanBodyBones.Head];
            var leftHand = bones[HumanBodyBones.LeftHand];
            var rightHand = bones[HumanBodyBones.RightHand];
            var view = head.position + new Vector3(0f, 0.04f, 0.06f);
            var devices = StandingDevices(bones, view);
            var standing = new TrackingFrame(devices, 1d);
            var calibration = new TrackingCalibration();
            check(calibration.Calibrate(animator, view, standing, out var error), "Standing head/hand/body calibration succeeds: " + error);
            if (!calibration.Calibrated) return;
            check(Mathf.Abs(calibration.Scale - 1f) < 0.0001f && Mathf.Abs(calibration.UserHeight - view.y) < 0.0001f &&
                Mathf.Abs(calibration.UserArmSpan - Vector3.Distance(leftHand.position, rightHand.position)) < 0.0001f,
                "Calibration measures standing eye height and controller span in metres");
            check(calibration.TryGetWorldPose(TrackingRole.Head, standing, out var headPose) && Vector3.Distance(headPose.position, head.position) < 0.0001f &&
                calibration.TryGetViewPose(standing, out var viewPose) && Vector3.Distance(viewPose.position, view) < 0.0001f,
                "The head-bone target and headset view retain their separately calibrated offsets");
            check(calibration.TryGetWorldPose(TrackingRole.LeftFoot, standing, out var footPose) &&
                Vector3.Distance(footPose.position, bones[HumanBodyBones.LeftFoot].position) < 0.0001f,
                "Calibration captures per-device foot offsets for additional body trackers");
            solver = new HumanoidPoseSolver(animator);
            var moved = Move(devices, TrackingRole.LeftHand, new Vector3(0.12f, 0.06f, 0.1f));
            var movedFrame = new TrackingFrame(moved, 2d);
            var upperLength = Vector3.Distance(bones[HumanBodyBones.LeftUpperArm].position, bones[HumanBodyBones.LeftLowerArm].position);
            var lowerLength = Vector3.Distance(bones[HumanBodyBones.LeftLowerArm].position, leftHand.position);
            solver.Solve(movedFrame, calibration, role => role == TrackingRole.LeftHand);
            calibration.TryGetWorldPose(TrackingRole.LeftHand, movedFrame, out var handTarget);
            check(Vector3.Distance(leftHand.position, handTarget.position) < 0.001f,
                "The production Humanoid solver reaches a calibrated controller target after animation");
            check(Mathf.Abs(Vector3.Distance(bones[HumanBodyBones.LeftUpperArm].position, bones[HumanBodyBones.LeftLowerArm].position) - upperLength) < 0.0001f &&
                Mathf.Abs(Vector3.Distance(bones[HumanBodyBones.LeftLowerArm].position, leftHand.position) - lowerLength) < 0.0001f,
                "Humanoid arm constraints retain the avatar's actual world-space bone lengths");
            check(Quaternion.Angle(rightHand.localRotation, originals[rightHand].rotation) < 0.001f,
                "Tracking gates preserve the opposite hand's authored pose");
            solver.Solve(TrackingFrame.Empty, calibration);
            check(Vector3.Distance(leftHand.localPosition, originals[leftHand].position) < 0.0001f &&
                Quaternion.Angle(bones[HumanBodyBones.LeftUpperArm].localRotation, originals[bones[HumanBodyBones.LeftUpperArm]].rotation) < 0.001f,
                "An absent tracker releases previous unanimated IK channels instead of freezing the last solved arm");

            var armDevices = new List<TrackedDevice>(devices);
            for (var index = 0; index < armDevices.Count; index++)
            {
                var device = armDevices[index];
                if (device.Role == TrackingRole.LeftElbow)
                    armDevices[index] = new TrackedDevice(device.Id, device.Role,
                        new Pose(device.Pose.position, Quaternion.Euler(40f, 0f, 0f) * device.Pose.rotation), true, 3d);
            }
            var armFrame = new TrackingFrame(armDevices, 3d);
            solver.Solve(armFrame, calibration, role => role == TrackingRole.LeftElbow);
            calibration.TryGetWorldPose(TrackingRole.LeftElbow, armFrame, out var upperArmTarget);
            check(TrackingCalibration.BoneFor(TrackingRole.LeftElbow) == HumanBodyBones.LeftUpperArm &&
                Quaternion.Angle(bones[HumanBodyBones.LeftUpperArm].rotation, upperArmTarget.rotation) < 0.001f &&
                Quaternion.Angle(bones[HumanBodyBones.LeftLowerArm].localRotation, originals[bones[HumanBodyBones.LeftLowerArm]].rotation) < 0.001f,
                "The SteamVR elbow role constrains the upper arm while an untracked forearm keeps its authored local pose");
            solver.Restore();
            solver.Solve(movedFrame, calibration, role => role == TrackingRole.LeftHand || role == TrackingRole.LeftElbow);
            var animatedForearm = Quaternion.Euler(15f, -20f, 30f);
            var animatedWrist = Quaternion.Euler(5f, 10f, -12f);
            bones[HumanBodyBones.LeftLowerArm].localRotation = animatedForearm;
            leftHand.localRotation = animatedWrist;
            var droppedHandDevices = armDevices.FindAll(device => device.Role != TrackingRole.LeftHand);
            var droppedHandFrame = new TrackingFrame(droppedHandDevices, 4d);
            solver.Solve(droppedHandFrame, calibration, role => role == TrackingRole.LeftHand || role == TrackingRole.LeftElbow);
            check(!droppedHandFrame.TryGet(TrackingRole.LeftHand, out _) &&
                Quaternion.Angle(bones[HumanBodyBones.LeftUpperArm].rotation, upperArmTarget.rotation) < 0.001f &&
                Quaternion.Angle(bones[HumanBodyBones.LeftLowerArm].localRotation, animatedForearm) < 0.001f &&
                Quaternion.Angle(leftHand.localRotation, animatedWrist) < 0.001f,
                "Dropping a previously tracked hand keeps the live forearm and wrist animation while its remaining upper-arm tracker controls only the humerus");
            solver.Restore();
            var kneeDevices = new List<TrackedDevice>();
            foreach (var device in devices)
            {
                if (device.Role == TrackingRole.LeftFoot) continue;
                kneeDevices.Add(new TrackedDevice(device.Id, device.Role,
                    new Pose(device.Pose.position, device.Role == TrackingRole.LeftKnee ? Quaternion.Euler(20f, 0f, 0f) * device.Pose.rotation : device.Pose.rotation),
                    device.Valid, device.Timestamp));
            }
            var kneeOnly = new TrackingFrame(kneeDevices, 4d);
            solver.Solve(kneeOnly, calibration, role => role == TrackingRole.LeftKnee || role == TrackingRole.LeftFoot);
            calibration.TryGetWorldPose(TrackingRole.LeftKnee, kneeOnly, out var kneeTarget);
            check(Quaternion.Angle(bones[HumanBodyBones.LeftLowerLeg].rotation, kneeTarget.rotation) < 0.001f &&
                Quaternion.Angle(bones[HumanBodyBones.LeftFoot].localRotation, originals[bones[HumanBodyBones.LeftFoot]].rotation) < 0.001f,
                "A knee-only tracker still drives its mapped lower-leg orientation while an absent foot keeps its authored local pose");
            solver.Restore();
            var chestFrame = new TrackingFrame(Move(devices, TrackingRole.Chest, Vector3.forward * 0.08f), 3d);
            foreach (var mode in new[] { TrackingLockMode.Head, TrackingLockMode.Hip })
            {
                solver.LockMode = mode;
                solver.Solve(chestFrame, calibration, role => role == TrackingRole.Chest);
                check(Vector3.Distance(bones[HumanBodyBones.Chest].localPosition, originals[bones[HumanBodyBones.Chest]].position) < 0.0001f &&
                    Quaternion.Angle(bones[HumanBodyBones.Spine].localRotation, originals[bones[HumanBodyBones.Spine]].rotation) < 0.001f,
                    "Chest trackers use rotation without positional spine constraints in " + mode + " lock mode");
                solver.Restore();
            }
            solver.LockMode = TrackingLockMode.Both;
            solver.Solve(chestFrame, calibration, role => role == TrackingRole.Chest);
            check(bones[HumanBodyBones.Chest].position.z > 0.03f,
                "Both lock mode enables the additional chest position constraint without stretching spine bones");
            solver.Restore();
            var gatedHips = bones[HumanBodyBones.Hips].position;
            var headMoved = new TrackingFrame(Move(devices, TrackingRole.Head, new Vector3(0.08f, 0f, 0.04f)), 3d);
            solver.Solve(headMoved, calibration, role => role == TrackingRole.Head);
            check(Vector3.Distance(bones[HumanBodyBones.Hips].position, gatedHips) < 0.0001f &&
                Quaternion.Angle(bones[HumanBodyBones.Hips].localRotation, originals[bones[HumanBodyBones.Hips]].rotation) < 0.001f,
                "A Hip animation gate prevents inferred pelvis translation even when head tracking remains active");
            check(Quaternion.Angle(bones[HumanBodyBones.Spine].localRotation, originals[bones[HumanBodyBones.Spine]].rotation) > 0.1f,
                "Head tracking can still bend the spine toward its target while the pelvis remains animation-controlled");
            solver.Restore();

            var fullBody = new TrackingFrame(Move(devices, TrackingRole.Hip, new Vector3(0.02f, -0.04f, 0f)), 3d);
            solver.LockMode = TrackingLockMode.Both;
            solver.Solve(fullBody, calibration);
            calibration.TryGetWorldPose(TrackingRole.Hip, fullBody, out var hipTarget);
            calibration.TryGetWorldPose(TrackingRole.LeftFoot, fullBody, out var leftFootTarget);
            check(Vector3.Distance(bones[HumanBodyBones.Hips].position, hipTarget.position) < 0.0001f &&
                Vector3.Distance(bones[HumanBodyBones.LeftFoot].position, leftFootTarget.position) < 0.001f,
                "Hip and foot trackers constrain a full-body pose while the pelvis remains pinned");
            var hips = bones[HumanBodyBones.Hips];
            var animatedHipRotation = Quaternion.Euler(0f, 22f, 0f) * hips.localRotation;
            hips.localRotation = animatedHipRotation;
            var animatedWristOnLoss = Quaternion.Euler(8f, 12f, -20f);
            rightHand.localRotation = animatedWristOnLoss;
            check(Vector3.Distance(hips.localPosition, originals[hips].position) > 0.01f,
                "The release fixture has an actual IK pelvis offset before simulating tracking loss");
            solver.ReleaseTracking();
            check(Vector3.Distance(hips.localPosition, originals[hips].position) < 0.0001f &&
                Quaternion.Angle(hips.localRotation, animatedHipRotation) < 0.001f,
                "Tracking release restores an unanimated IK position channel even when the same bone has a new animated rotation");
            check(Quaternion.Angle(rightHand.localRotation, animatedWristOnLoss) < 0.001f,
                "Tracking release preserves fresh animation channels rather than restoring the startup whole-body pose");
            solver.ReleaseTracking();
            check(Quaternion.Angle(hips.localRotation, animatedHipRotation) < 0.001f,
                "Repeated tracking release is idempotent and keeps animation that was preserved by the first release");
            solver.Restore();
            var onlyHead = new TrackingFrame(new[] { Move(devices, TrackingRole.Head, new Vector3(0.1f, 0f, 0f))[0] }, 4d);
            solver.Solve(onlyHead, calibration);
            check(Quaternion.Angle(bones[HumanBodyBones.LeftUpperLeg].localRotation, originals[bones[HumanBodyBones.LeftUpperLeg]].rotation) < 0.001f,
                "Head-only tracking does not synthesize a walking or foot-planting animation");
            solver.Restore();

            var beforeRoot = new Pose(root.transform.position, root.transform.rotation);
            var afterRoot = new Pose(new Vector3(2f, 0f, 3f), Quaternion.Euler(0f, 90f, 0f));
            calibration.ApplyLocomotion(beforeRoot, afterRoot);
            root.transform.SetPositionAndRotation(afterRoot.position, afterRoot.rotation);
            calibration.TryGetViewPose(standing, out var movedView);
            var expectedView = afterRoot.position + afterRoot.rotation * view;
            check(Vector3.Distance(movedView.position, expectedView) < 0.0001f,
                "Locomotion translates and rotates the calibrated origin exactly once around the player root");
            check(Vector3.Distance(calibration.MapTrackingPose(new Pose(view, Quaternion.identity)).position, expectedView) < 0.0001f,
                "Unassigned device visualizations share the calibrated tracking mapping without requiring a bone-role binding");
            var shiftedSpace = new TrackingFrame(devices, 5d, origin: new Pose(new Vector3(4f, 0f, -2f), Quaternion.identity));
            check(calibration.Recenter(shiftedSpace, expectedView) && calibration.TryGetViewPose(shiftedSpace, out var recentered) &&
                Vector3.Distance(recentered.position, expectedView) < 0.0001f && Mathf.Abs(calibration.Scale - 1f) < 0.0001f,
                "Recenter changes the tracking basis without losing avatar calibration scale");
            var measured = new TrackingCalibration { Measurement = TrackingMeasurement.ArmSpan, UserArmSpanOverride = 2f };
            check(measured.Calibrate(animator, root.transform.TransformPoint(view), standing, out _) &&
                Mathf.Abs(measured.Scale - measured.AvatarArmSpan / 2f) < 0.0001f,
                "A user-supplied arm span selects the measurement used for calibration scale");
            var trackingPose = new Pose(new Vector3(0.3f, 1.3f, -0.4f), Quaternion.Euler(20f, 30f, 10f));
            var roundTrip = measured.UnmapTrackingPose(measured.MapTrackingPose(trackingPose));
            check(Vector3.Distance(roundTrip.position, trackingPose.position) < 0.0001f && Quaternion.Angle(roundTrip.rotation, trackingPose.rotation) < 0.001f,
                "Calibrated tracking/world pose conversion round-trips position and rotation with non-unit measurement scale");
            solver.Dispose();
            solver = null;
            var restored = true;
            foreach (var pair in originals)
                restored &= Vector3.Distance(pair.Key.localPosition, pair.Value.position) < 0.0001f && Quaternion.Angle(pair.Key.localRotation, pair.Value.rotation) < 0.001f;
            check(restored, "Disposing the pose solver restores every captured Humanoid bone local pose");
            check(Vector3.Distance(root.transform.position, afterRoot.position) < 0.0001f,
                "Pose solving and disposal leave locomotion root ownership with the motion session");
        }
        finally
        {
            solver?.Dispose();
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (human) UnityEngine.Object.DestroyImmediate(human);
        }
    }

    private static TrackedDevice Device(string id, TrackingRole role, Vector3 position) =>
        new TrackedDevice(id, role, new Pose(position, Quaternion.identity), true, 1d);

    private static List<TrackedDevice> StandingDevices(Dictionary<HumanBodyBones, Transform> bones, Vector3 view)
    {
        var devices = new List<TrackedDevice>();
        for (var role = TrackingRole.Head; role <= TrackingRole.RightElbow; role++)
        {
            var bone = bones[TrackingCalibration.BoneFor(role)];
            devices.Add(new TrackedDevice(role.ToString(), role, new Pose(role == TrackingRole.Head ? view : bone.position, bone.rotation), true, 1d));
        }
        return devices;
    }

    private static List<TrackedDevice> Move(List<TrackedDevice> original, TrackingRole role, Vector3 delta)
    {
        var devices = new List<TrackedDevice>();
        foreach (var device in original)
            devices.Add(new TrackedDevice(device.Id, device.Role, new Pose(device.Pose.position + (device.Role == role ? delta : Vector3.zero), device.Pose.rotation), device.Valid, device.Timestamp + 1d));
        return devices;
    }

    private static Transform Child(Transform parent, string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = position;
        return child;
    }

    public static Animator CreateHumanoid(out Avatar human, out Dictionary<HumanBodyBones, Transform> bones)
    {
        var root = new GameObject("Pro tracking Humanoid fixture");
        bones = MakeSkeleton(root.transform);
        var humanBones = new List<HumanBone>();
        foreach (var pair in bones)
            humanBones.Add(new HumanBone { boneName = pair.Value.name, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true } });
        var skeleton = new List<SkeletonBone>();
        foreach (var transform in root.GetComponentsInChildren<Transform>())
            skeleton.Add(new SkeletonBone { name = transform.name, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale });
        human = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
        {
            human = humanBones.ToArray(), skeleton = skeleton.ToArray(), upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f, lowerLegTwist = 0.5f, armStretch = 0.05f, legStretch = 0.05f
        });
        human.hideFlags = HideFlags.HideAndDontSave;
        var animator = root.AddComponent<Animator>();
        animator.avatar = human;
        return animator;
    }

    private static Dictionary<HumanBodyBones, Transform> MakeSkeleton(Transform root)
    {
        var bones = new Dictionary<HumanBodyBones, Transform>();
        var hips = bones[HumanBodyBones.Hips] = Child(root, "Hips", Vector3.up);
        var spine = bones[HumanBodyBones.Spine] = Child(hips, "Spine", Vector3.up * 0.2f);
        var chest = bones[HumanBodyBones.Chest] = Child(spine, "Chest", Vector3.up * 0.2f);
        var neck = bones[HumanBodyBones.Neck] = Child(chest, "Neck", Vector3.up * 0.25f);
        bones[HumanBodyBones.Head] = Child(neck, "Head", Vector3.up * 0.15f);
        foreach (var left in new[] { true, false })
        {
            var side = left ? "Left" : "Right";
            var x = left ? -1f : 1f;
            var shoulder = bones[left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder] = Child(chest, side + "Shoulder", new Vector3(x * 0.15f, 0.1f, 0f));
            var arm = bones[left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm] = Child(shoulder, side + "UpperArm", Vector3.right * (x * 0.15f));
            var forearm = bones[left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm] = Child(arm, side + "LowerArm", Vector3.right * (x * 0.3f));
            bones[left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand] = Child(forearm, side + "Hand", Vector3.right * (x * 0.25f));
            var leg = bones[left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg] = Child(hips, side + "UpperLeg", new Vector3(x * 0.1f, -0.05f, 0f));
            var shin = bones[left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg] = Child(leg, side + "LowerLeg", Vector3.down * 0.45f);
            var foot = bones[left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot] = Child(shin, side + "Foot", new Vector3(0f, -0.45f, 0.05f));
            bones[left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes] = Child(foot, side + "Toes", Vector3.forward * 0.12f);
        }
        return bones;
    }
}
