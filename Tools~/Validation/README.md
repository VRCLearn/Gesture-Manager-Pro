# Package validation

These checks target Unity 2022.3 and VRChat Avatars SDK 3.10.x. They test the standalone package without the original Gesture Manager installed.

## Static checks

Run `node Tools~/Validation/Validate-Package.cjs` with `--dependency-root` pointing to each installed `com.vrchat.base` and `com.vrchat.avatars` package. To also check coexistence GUID isolation against an original Gesture Manager checkout, pass `--original-root` with that checkout's path.

The validator checks assembly dependencies, asset metadata, GUID uniqueness, serialized references, resource lookups, and the native DLL hash/importer/license files. Missing historical references are reported separately when an original baseline is available; they are not counted as resolved.

`node Tools~/Validation/Prune-ControllerOrphans.cjs` performs a dry run of controller graph reachability. `--apply` prunes orphan object blocks in the eight built-in controllers and their matching restore snapshots. It verifies that the retained objects are byte-for-byte unchanged and rejects missing local graph references. This cleanup was applied to the initial Pro baseline after checking that other package assets and source code did not reference the orphan subobjects.

For a read-only report of external controller references, run `node Tools~/Validation/Inspect-ControllerReferences.cjs --sdk-root <com.vrchat.avatars directory>`. Supply `--output <report.json>` only when a saved diagnostic report is needed.

## Unity integration checks

Use a disposable Unity avatar project with the target SDK and this package installed. Copy `UnityProChecks.cs`, `UnityProMotionChecks.cs`, `UnityProPlayChecks.cs`, `UnityProGraphChecks.cs`, `UnityProCameraChecks.cs`, `UnityProSimulationRenderChecks.cs`, and `UnityProValidation.asmdef` into its `Assets/Editor` folder. Do not copy these scripts into a user's working avatar project for batch testing.

Run Unity with `-batchmode -nographics -projectPath <fixture> -executeMethod UnityProChecks.Run -logFile <log>`. For actual Play Mode session checks and GPU-rendered camera images, use a second invocation with `-executeMethod UnityProPlayChecks.Run` and omit `-nographics`. These runners exit Unity themselves; do not add `-quit` to the Play runner. Rendered validation images are saved beneath the fixture's `Temp/GestureManagerProValidation/SimulationViews` folder.

The editor runner creates a temporary GUI context to check localized UI caches, resource ownership, native analyzer initialization and preservation of user names/parameter identities. The motion checks inject deterministic input. Play checks exercise session ownership, movement publication, stop/pause/unlink/failure cleanup and restoration. The graph checks initialize a valid generic avatar through the actual simulator connection path and eight default animation layers. They do not automate physical key presses, microphone hardware, VR hardware, humanoid body IK or visual layout inspection.

The GitHub workflow builds an archive only. It does not replace Unity integration or hardware validation.
