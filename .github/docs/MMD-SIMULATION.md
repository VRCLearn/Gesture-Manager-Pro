# MMD Simulation

MMD Simulation previews a non-seated station setup in the Unity Editor. It uses
your own Unity AnimationClip and the avatar's existing playable controllers.
MMD worlds use different station controllers and layer controls, so this preset
is a local compatibility test, not a reproduction of every MMD world.

## Use

1. Enter Play Mode with an initialized Gesture Manager Pro avatar and resume the
   Editor if it is paused.
2. Stop **Game Simulation**, **Pose Avatar** and **Test Animation**, then open
   **Tools > Extra Tools > MMD Simulation** in the emulator Inspector.
3. Assign **Animation (optional)** or leave it empty. **Loop** starts enabled.
   **Disable FX Layers 2 and 3** starts enabled; **Disable Gesture Layer** starts
   disabled. Choose the controls that match the world setup you want to inspect.
4. Click **Enter MMD Simulation**. Use **Exit and Restore** before starting another
   animation or simulation tool.

An empty animation tests only station state and the selected layer controls.
It does not generate a dance, pose or facial animation. Import a Unity
AnimationClip before assigning a motion; direct VMD/PMX import and conversion are
outside this tool. Legacy clips are rejected, and Humanoid clips require a valid
Humanoid avatar. As with a station, the root Animator needs an assigned, valid
Unity Avatar.

Exiting the session, pausing the Editor, leaving Play Mode, or resetting or
disconnecting the avatar releases the station preview and restores the parameters,
tracking control, locomotion control and layer weights owned by the session.
The tool does not edit the source clip or avatar controllers.

## Preview behavior

- Set `InStation` to `true` and `Seated` to `false`.
- Set tracking control to **Animation** and disable locomotion for the session.
- Evaluate the optional clip through an independent non-seated station playable
  after **Gesture** and before **Action**. The avatar's **Sitting** controller is
  not used as the MMD motion slot.
- When selected, suppress internal FX animator layers at zero-based indices `1`
  and `2`, shown in the tool as **FX Layers 2 and 3**. The first FX layer, index
  `0`, is not included in this switch.
- Optionally suppress the **Gesture** playable layer. The preset does not force
  **Base**, **Additive**, **Action**, or the remaining FX layers off. Their
  existing controllers and state behaviours continue to apply.

Tracking and locomotion settings are applied on entry. The avatar's later state
behaviours can change them during playback, so conflicting avatar logic remains
observable. The selected layer-suppression switches stay effective until exit.

The station-playable placement follows the
[EZUtils MMD tester](https://github.com/Timiz0r/EZUtils/blob/9d95cc16444a9669198c77463ae5741e973684ca/src/EZUtils.MMDAvatarTools/MmdAvatarTester.cs)
and its
[non-seated station implementation](https://github.com/Timiz0r/EZUtils/blob/9d95cc16444a9669198c77463ae5741e973684ca/src/EZUtils.Common/Animation/AvatarPlayableAnimator.cs#L187).
The VRChat client's complete native playable graph order is not officially
specified or established as equivalent to this preview.

## World-specific differences

[Modular Avatar's author documentation](https://modular-avatar.nadena.dev/docs/general-behavior/mmd)
describes worlds that disable the second and third FX animator layers and limits
its workaround to that particular pattern.
[VRCFury's MMD compatibility implementation](https://github.com/VRCFury/VRCFury/blob/6bd33f4cb453df816c943a335e47f64e6a37e188/com.vrcfury.vrcfury/Editor-Avatars/Feature/MmdCompatibilityBuilder.cs#L56)
also protects those two indices and notes that worlds disable them only in some
cases. This is the basis for the default FX switch, rather than a rule that all
MMD worlds disable the first three FX layers.

[VRCFury's technical notes](https://github.com/VRCFury/vrcfury.com/blob/main/docs/70-technical/fixes.md)
describe Gesture suppression in MMD worlds, while the EZUtils tester does not
unconditionally turn that playable layer off. Gesture suppression is therefore
a separate option in GMP. There is no general rule here that disables Base,
Additive and Action along with it.

`InStation` and `Seated` are distinct station signals. The
[official station documentation](https://creators.vrchat.com/worlds/components/vrc_station/#stations-used-in-worlds)
explains that SDK3 station controllers select tracking control and that the
Seated property does not itself determine the tracking type.
[VRCFury uses `InStation && !Seated`](https://github.com/VRCFury/VRCFury/blob/6bd33f4cb453df816c943a335e47f64e6a37e188/com.vrcfury.vrcfury/Editor-Avatars/Utils/ControllerManager.cs#L162)
as an MMD heuristic; ordinary non-seated stations can have the same signals.
GMP sets these signals for the selected preset, without treating them as a
dedicated VRChat MMD identifier.

## Facial animation and controller compatibility

Facial curves keep the clip's original mesh paths and blendshape names. A clip
targeting `Body` needs the corresponding mesh at that exact path. The tool
reports missing animation targets and plays the clip without retargeting; it does
not rename meshes or translate blendshape identifiers.

[Avatar Optimizer's MMD handling](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cdae9f3afd3c6f7cbbd99a99046a479/Editor/AnimatorParserV2/AnimatorParser.cs#L319)
preserves MMD blendshapes on a root-level `Body` SkinnedMeshRenderer. Its
[name table](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cdae9f3afd3c6f7cbbd99a99046a479/Editor/AnimatorParserV2/AnimatorParser.cs#L720)
contains Japanese and multiple English conventions. Those conventions describe
known motion bindings, not a requirement that every clip use Japanese names or
that every avatar have every listed blendshape.

FX masks, other active animations and **Write Defaults** can prevent the intended
body or facial motion. The
[EZUtils tests](https://github.com/Timiz0r/EZUtils/blob/9d95cc16444a9669198c77463ae5741e973684ca/src/EZUtils.MMDAvatarTools.Tests/MMDAvatarTesterTests.cs)
cover cases where active FX states with Write Defaults off block station facial
animation, and where blending those layers out allows it. VRCFury's MMD option
[selects its Force On strategy](https://github.com/VRCFury/VRCFury/blob/6bd33f4cb453df816c943a335e47f64e6a37e188/com.vrcfury.vrcfury/Editor-Avatars/Service/FixWriteDefaultsService.cs#L157)
as an avatar-build transformation. GMP leaves the avatar's Write Defaults and
masks intact so the preview can expose these conflicts.

## Scope and verification

This tool does not run a world's Udon program, synchronize players or world
state, reproduce world scaling or client viewpoint adjustments, or implement
VRChat's native IK. It does not download music or motions, convert VMD/PMX files,
or generate replacement motion for an empty or incompatible clip.

The references above establish the chosen preset and known differences. They do
not establish parity with a specific world's current controller or the VRChat
client. Check the intended world in the client for final compatibility; editor
preview alone cannot verify network behavior, native IK or client viewpoint
handling.
