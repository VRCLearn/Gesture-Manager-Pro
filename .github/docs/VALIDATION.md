# Development validation

## 1.5.0 validation

Validated on 2026-10-26 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 in the disposable `Output~/VrValidationProject` fixture.

- The final 1.5.0 source passed the complete graphics-enabled Editor runner with 1,454 assertions and no C# compilation errors (`Output~/Release150-editor.log`). All 66 Python distribution and publishing tests passed (`Output~/Release150-python-final.log`); localization passed 19,079 checks and package validation passed 1,960 checks.
- The detailed checks below cover the five-language terminology, Android shader preview and contributor display. The unchanged preview and contributor implementations retain their complete PC Play (1,719 assertions), actual Android-target focused (86 assertions) and online contributor (89 assertions) results. This release also preserves the existing Extra Tools order by appending new entries.

Android-device rendering, VRChat-client comparisons, physical headset/native XR handovers and manual visual checks across all languages, themes and display scales remain unverified. The final Editor log retains Unity shutdown allocation diagnostics.

## VRCX-0 performance terminology (1.5.0)

Validated on 2026-10-26 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 in the disposable `Output~/VrValidationProject` fixture.

- The final graphics-enabled Editor runner passed 1,454 assertions (`Output~/VrcxTerminology-editor-final.log`) with no C# compilation errors. The actual Model Statistics window switches through English, Japanese, Korean, Traditional Chinese and Simplified Chinese and verifies all 31 metric captions, five rank captions, icons, the Rating heading and localized scope tooltips. Avatar identity, platform, stage, selected record, visible history and serialized/persisted snapshots remain unchanged during switching. Legacy bounds preserve their size-only values and explanatory tooltip.
- Localization passed 19,079 checks across 596 keys in each of five languages. `VrcxPerformanceReference.json` pins the effective upstream strings at `Map1en/VRCX-0` commit `421d84d86fada534f7e1e508844dbe46a7f58dee`; all 185 unique language/key values match that source. Traditional Chinese rank captions follow VRCX-0's existing English fallback. Package validation passed 1,960 checks with 310 GUIDs.
- The final eight package files were synchronized to the existing Test-Avatar installation after a full package backup. All 569 installed files were verified and 33 scene, package-manifest and project-setting files remained unchanged. The project was closed during synchronization; reopening it and inspecting the real avatar remain manual acceptance steps.

SDK calculations, metric values, thresholds, units and snapshot formats were not changed. These checks verify text, UI bindings and data preservation; visual inspection of every language, editor theme and display scale was not performed. Existing Unity shutdown diagnostics remain in the fixture log.

## Android Shader Preview (1.5.0)

Validated on 2026-10-26 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 in the disposable `Output~/VrValidationProject` fixture.

- The Editor runner passed 1,417 assertions (`Output~/AndroidShaders-editor-final.log`). The final complete PC Play regression passed 1,719 assertions (`Output~/AndroidShaders-play-final.log`). The focused runner also passed 86 assertions with the actual active build target set to Android (`Output~/AndroidShaders-android.log`); its target result and images are preserved in `Output~/AndroidShaders-android-diagnostics`.
- Real GPU rendering verifies unsupported materials' normal output changes to pink only during preview, allowed SDK materials remain unchanged, and Outline matches a native non-outline Toon Standard reference. Real Animator/PlayableGraph clips change material slots and shader properties; current animated references and output survive preview restoration. Source material and initialized animation serialization remain unchanged. Renderer-wide and indexed material property blocks, live Outline flags, nested cameras, interrupted rendering, target changes, disabling, paused previews, Play exit and disposal are covered.
- The actual simulator owner covers dynamically added renderers, remote clones and the animation-test dummy. Reset restores references before rebuilding its graph; unlink releases preview resources and camera callbacks. The fixture adds the SDK PipelineManager needed by the existing clone path and explicitly owns the synthetic clone's test graph cleanup.
- Package validation passed 1,960 checks with 310 distinct asset GUIDs. Localization passed 18,321 checks across 592 keys in each of five languages. Both installation archives built and retained the final preview sources and metadata (`Output~/AndroidShaders-final-package`). The fixture was returned to the PC build target after Android acceptance; the user avatar project was not modified.

The Editor log retains an initial compilation diagnostic before Unity refreshed the added SDK assembly reference. The final Android and PC Play invocations compiled successfully. Existing fixture Dynamics allocation warnings and Unity shutdown diagnostics remain in the logs. These checks exercise Unity's DX11 renderer with Android as the build target; no Android device, APK upload or VRChat client comparison was performed. Pink is an explicitly chosen diagnostic appearance, not a claimed client fallback. Manual Inspector interaction and visual checks across all languages, themes and display scales remain unverified.

## About and Contributors (1.5.0)

Validated on 2026-10-25 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 in the disposable `Output~/VrValidationProject` fixture.

- The complete graphics-enabled Editor runner passed 1,401 assertions (`Output~/Contributors-editor-graphics.log`). The final focused contributor runner passed 89 assertions (`Output~/Contributors-expanded-online.log`), including a real public GitHub avatar download, exact cached-texture reuse, cancellation of another pending request, texture destruction and prevention of late work after disposal.
- Default collapsed GUI events leave the catalog and image queue untouched. Actual GUI events render the expanded list at a 220-pixel window width without exceptions, queue all 11 local entries without starting requests during drawing, and deactivate the queue when folded. Expanded/collapsed layout uses explicit foldout-state input; it does not establish physical mouse interaction with the inherited foldout control. Closing the owner and destroying the actual Inspector release their resources. Manager settings and the avatar descriptor retain their previous data.
- The real snapshot contains 11 GitHub contributors in 2,215 bytes of JSON, including inherited upstream contributions. Both local archive formats preserve the snapshot and its Unity GUID; no contributor avatar image is bundled. All 66 Python distribution tests passed (`Output~/Contributors-python.log`); the 12 contributor tests also passed after the final identifier-validation adjustment (`Output~/Contributors-python-focused.log`). Package validation passed 1,948 checks and localization passed 18,073 checks across 584 keys in each of five languages.

GUI diagnostics and the online result are retained in the fixture's `ValidationOutput~/Contributors` directory. Final C# compilation succeeded. Existing fixture warnings and Unity shutdown allocation diagnostics remain in the logs. Manual mouse interaction and visual inspection of every language, display scale and editor theme were not performed. The user avatar project was not modified.

## 1.4.0 validation

Validated on 2026-10-24 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 in the disposable `Output~/VrValidationProject` fixture.

- Final Editor integration passed 1,326 assertions (`Output~/Mmd-editor-final.log`) with no C# compilation errors. The full Play runner passed 1,634 assertions (`Output~/Mmd-play-final.log`); a focused graph/MMD run passed 107 assertions (`Output~/Mmd-focused-timing.log`).
- Real Generic animation drives transform and blendshape curves; a real Humanoid graph evaluates an authored muscle clip and changes arm/leg bone rotations. Stops restore the previous pose and original Base animation control. Clip curves, settings, events, full initialized serialization, controller objects and descriptor references remain unchanged. Diagnostics are retained under the fixture's `ValidationOutput~/Mmd` directory.
- Empty clips and unassigned clips retain station-only mode without a substitute animation. Independent Gesture and FX indices `1`/`2` controls, original FX index `0`, normal module update ordering, station parameter restoration, loop/one-shot completion, incompatible clips, Mouth tracking, ownership conflicts and reset/unlink cleanup are covered.
- Package validation passed 1,924 checks and localization passed 17,856 checks across five languages. All 54 Python distribution/release tests passed (`Output~/Mmd-python.log`).

The Play log retains an initial CS2001 for a removed temporary diagnostic runner before Unity refreshed its source list; the subsequent clean Editor run had no C# errors. Existing fixture CS0252 warnings and Unity shutdown allocation diagnostics remain in the logs. No user avatar project was modified. Actual MMD worlds, client-side station/IK/network behavior, third-party optimizer combinations, MMD pause cleanup, VR hardware and manual Inspector interaction/layout were not verified. The preset's sources and deliberate limits are documented in [MMD Simulation](MMD-SIMULATION.md).

## 1.3.0 validation

Validated on 2026-10-23 and 2026-10-24 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 in the disposable `Output~/VrValidationProject` fixture.

- Editor integration passed 1,326 assertions, including the final 1.3.0 sources (`Output~/SizeMetrics-editor.log`, `Output~/Release130-editor.log`). The statistics runner builds a disposable prefab AssetBundle and verifies both size metrics, two-decimal MB formatting, SDK grades, actual-platform isolation, immutable pending observations and session serialization. Missing bundles, unchanged reports and a colliding SDK temporary report metadata file are covered. Generated fixture assets and temporary build-report imports were removed.
- An independent actual bundle probe measured 44,186 download bytes and 181,788 uncompressed bytes; the SDK helper matched the sum of packed contents and overhead (`Output~/build-report-probe.log`).
- On 2026-10-24, the real Test-Avatar project's Chocolat completed a local SDK build with its installed processors. The PC record and Model Statistics window showed **7.69 MB** download and **45.65 MB** uncompressed, matching 8,067,247 bundle bytes and 47,868,936 bytes from the SDK report helper. The Mobile record retained unavailable sizes. The scene, descriptor, pipeline identity, project settings and existing asset metadata matched their pre-test state afterwards; the tested package remained installed. Evidence is retained in `Output~/TestAvatarSizeMetrics-20261024-010503/test-result.json` and `test-avatar-sdk-build.log`.
- All 54 Python distribution/release tests passed (`Output~/Release130-python.log`). Package validation passed 1,912 checks and localization passed 17,305 checks across five languages. `git diff --check` passed.

The fixture injects SDK lifecycle events around a real Unity AssetBundle build; the Test-Avatar check uses the actual SDK avatar builder. The latter confirms one avatar and its installed processor combination, without upload or client startup. Android builds, other production avatar/optimizer combinations, VRChat client size comparisons and manual window/glyph inspection remain unverified. The Test-Avatar Console had no errors and two Modular Avatar CS0114 warnings. Existing fixture CS0252 warnings and Unity shutdown allocation diagnostics remain in its log.

## 1.2.1 validation

Validated on 2026-10-21 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 in the disposable `Output~/VrValidationProject` fixture without optional VR dependencies.

- Editor checks passed 921 assertions (`Output~/DesktopPoseSpace-final-editor.log`). Focused graphics-enabled PC pose checks passed 49 assertions (`Output~/DesktopPoseSpace-initial-evaluation-focused.log`): absolute level/upward/downward gaze, imported bone axes, animated head/torso offsets, 200 repeated solves, Head Animation control, free-camera pitch and stop/reset cleanup. A real animation graph verifies a fresh animation pose equal to the previous tracking output. Real SDK behaviours cover initial-state delivery, fixed/proportional delay, one-time snapshots, repeated Enter/Exit, parent transforms and independent tracking/locomotion controls.
- The existing focused VR regression passed 194 assertions, including tracking geometry, menu/OSC, injected lifecycle and GPU local/observer visibility (`Output~/DesktopPoseSpace-final-vr.log`). All copied validation scripts matched the repository sources by SHA-256.
- The complete graphics-enabled Play regression passed 1,571 assertions (`Output~/CameraPrecision-final-play.log`), including PC head/Pose Space, observer following, collision, injected VR lifecycle and GPU local/observer rendering. All 19 copied validation scripts matched the repository sources by SHA-256.
- The close-camera failure was a numerically unstable test: a 192-case probe compared simulation and plain Cameras, positive/negative world positions, yaw, distance and output aspect. At `(600, 20, 900)`, yaw 135 degrees and 0.02 m distance, both Cameras returned viewport X `0.50609756`; projecting the same camera-relative point returned `0.49999991`, with only `1.69e-9` m lateral geometric residual. The probe is recorded in `Output~/CameraPrecision-aspect-probe.log` and `Output~/VrValidationProject/ValidationOutput~/CameraPrecisionProbe.json`. Near-distance checks now run at the origin with the original 0.001 viewport threshold and 1e-6 m placement tolerance. Separate large-world checks retain millimeter booms, independent direction/roll checks and float-bounded placement; stable distances also retain native viewport checks. Four injected errors verify rejection of view rotation, lateral placement, behind-camera depth and projection-center offsets. The production camera implementation is unchanged.
- All 54 Python distribution/release checks passed (`Output~/Release121-python.log`). Package validation passed 1,912 checks (`Output~/Release121-package.log`) and localization passed 17,305 checks (`Output~/Release121-localization.log`). The original Gesture Manager coexistence comparison and optional APW reference-calculator comparison were not run.

Physical keyboard/mouse operation, user avatar scenes, VR hardware, the client's exact Pose Space offset formula and full client IK parity were not verified. The PC head preview uses absolute view orientation after animation; it is not a full client body solver. Pose Space preserves the descriptor-to-reference-head offset as a documented implementation choice. Existing CS0252 fixture warnings and Unity shutdown allocation diagnostics remain in the logs. The source avatar project was not modified.

## 1.2.0 validation

Validated on 2026-10-21 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in disposable Windows fixtures.

- Editor checks passed 923 assertions without VR dependencies and 962 with the official OpenVR plugin and its Unity dependencies (`Output~/Release120-pc-editor.log`, `Output~/Release120-vr-restart-editor.log`). Coverage includes the component settings entry, passive UI behavior, complete dependency readiness, explicit project configuration, restart handling, existing loader assignments, other platforms and unrelated unsaved assets.
- Graphics-enabled Play checks passed 1,088 assertions in each fixture (`Output~/Release120-VrValidationProject-play.log`, `Output~/Release120-SimpleVrInstallValidation-play.log`). These cover PC movement, cameras, injected VR tracking and handovers, menu release and restoration. Native XR display/input subsystems and GMP loader instances remain absent during idle Play Mode, PC simulation and VR selection before startup.
- The real installation check passed official download and hash verification, closed-window ownership, Play Mode deferral, UPM installation, domain-reload recovery and automatic project setup (`Output~/SimpleVr-real-install.log`). A subsequent fresh Editor process confirmed persisted Both input handling, both input backends, working legacy input access, a cleared restart marker, ready dependencies and zero native XR sessions.
- All 54 Python distribution/release tests passed. Package validation passed 1,906 checks and localization passed 17,305 checks across 559 keys in each of five languages. The optional comparison against an original Gesture Manager checkout was not run.

Physical headset display, controller operation, native XR handovers and VRChat client parity remain unverified. The Play checks use injected tracking rather than a physical headset. Unity shutdown allocation diagnostics remain in the logs.

## 1.1.0 validation

GMP 1.1.0 includes its SteamVR integration and bindings in one package. Edit Mode setup downloads and verifies the unchanged official Valve OpenVR 1.2.1 archive. PC remains the default and XR dependencies are optional.

Validated on 2026-10-21 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in disposable Windows fixtures.

- The editor runner passed 906 assertions without OpenVR or Input System installed (`Output~/SingleGmpPcValidation-editor.log`). The same GMP package passed 938 editor assertions with the official OpenVR plugin and the Unity VR dependencies installed (`Output~/SingleGmpVrValidation-editor.log`). These runs cover compilation with and without the optional integration and the existing editor/PC behavior; they do not establish physical headset operation.
- Package validation passed 1,906 checks, localization passed 17,089 checks and all 54 Python distribution/release/listing tests passed. The single package includes the SteamVR integration and bindings; its distribution does not include a companion adapter archive or Valve's plugin.
- Graphics-enabled full Play runs passed 1,076 assertions both without the VR dependencies (`Output~/SingleGmpPurePc-play.log`) and with them (`Output~/SingleGmpVrValidation-play.log`). The checks include PC movement/cameras, simulator graph and cleanup, plus injected tracking, calibration, VR lifecycle and local/observer rendering. They do not start a physical headset session.
- The fresh-fixture `UnityProVrInstallChecks.Run` check passed real official download, pinned SHA-256 verification, closed-window ownership, download completion during Play Mode with package changes deferred to Edit Mode, UPM installation, domain-reload recovery, included-provider registration, and scene/XR configuration preservation (`Output~/SingleGmpPcValidation-real-install-final.log`). The initial run used a raw-byte comparison of the SDK XR asset and failed after Unity/SDK reserialization reordered internal file IDs (`Output~/SingleGmpPcValidation-real-install.log`). The corrected check compares the actual platform mapping, startup flags and loader identities; its final run passed without rewriting that configuration.
- Removing OpenVR and Input System through UPM disabled the included SteamVR provider and passed the 906-assertion main-package editor/PC regression (`Output~/SingleGmpPcValidation-remove-vr.log`). Unity briefly compiled a stale source list and reported `CS2001` errors for removed Input System files, then regenerated it and compiled successfully. These successful final results do not mean the complete logs contain no errors; earlier failures and Unity/SDK startup/shutdown diagnostics are retained.
- A separate UnityPackage fixture extracted the actual candidate asset records into `Assets/VRCLearn/GestureManagerPro` and passed 33 focused checks with no failures (`Output~/single-gmp-unitypackage.log`). It compiled the main/control/SteamVR assemblies outside UPM, resolved the action manifest and all 15 adjacent default bindings from the real Assets path, registered one provider, opened the Edit Mode setup menu without changing the scene/XR/manifest, and verified acceptance of the official archive and rejection of a same-size modified copy. It did not start XR or connect a headset.

Physical headset operation, native XR handovers and VRChat client parity remain unverified.

## Session-only Clickable Contacts (unreleased)

Validated on 2026-10-21 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11. Clickable Contacts now keeps its switch and tag filter only in the current tool instance. A new connection starts disabled with an empty filter, independently of the legacy Editor preferences. Both the Inspector and radial toggle release held contacts when disabling. Disposal and avatar unlink release and reset the tool, including when a receiver was destroyed; the unused preference wrappers were removed.

The disposable tool-session runner passed 443 assertions (`Output~/tool-session-switches.log`): 15 new Contacts checks using a real SDK receiver and parameter-access probe, plus the existing 428 observer/lifecycle/free-look assertions. The checks cover legacy values being ignored and left untouched, independent instances, matching/nonmatching/empty tag filters, held-parameter cleanup and default state after rebinding. Package validation passed 1,799 checks. Physical mouse interaction and the full Play suite were not rerun. The production script was backed up and synchronized to Test-Avatar without changing the active scene; live activation awaits Unity's next script reload if compilation is deferred during Play Mode. No commit or push was made for either session-state change.

## Session-only Scene Camera switch (unreleased)

Validated on 2026-10-21 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11. Scene Camera no longer reads or writes the legacy `GM3 SceneCamera` Editor preference. Its enabled state belongs to the current shared tool session; final owner disposal, Play transitions and script reload cleanup reset it to disabled while restoring the original camera. Explicit enabling still supports multiple live owners and recovery of a lost target within that session. Game Simulation continues to own its active observer selection separately.

A disposable fixture runner invoked the existing `CheckObserverCamera` and `CheckGameSimulationFreeLook` checks and passed 428 assertions (`Output~/scene-camera-session-only.log`). Coverage includes an old saved true value being ignored, new/recreated tools staying disabled, idempotent cleanup, target loss and restoration, and all four combinations of ordinary session state and unused legacy preference during the Game Simulation bridge. Package validation passed 1,799 checks. This focused run did not rerun the full Play suite or actual no-domain-reload Play transitions. The complete Play result and camera-precision correction are recorded in the 1.2.1 validation section above. Batch shutdown allocation diagnostics remain in the log.

The validated production script was backed up and synchronized to Test-Avatar without switching, saving or testing its active scene. At the final observation the Editor remained in Play Mode in the same unsaved scene, with zero Console errors or warnings; the old assembly was still loaded while compilation was pending. The live fix therefore awaits Unity's next script reload, which may be deferred until Play Mode ends according to its script-change setting. The disposable fixture confirms compilation and behavior independently of that live reload.

## Maintainer PC compatibility confirmation (unreleased)

On 2026-10-21, the maintainer confirmed that PC mode remains usable with these changes and requested local commits in separate functional batches. VR headset operation and client parity remain pending hardware validation. This manual confirmation complements the automated checks below; the complete Play result and camera-precision correction are recorded in the 1.2.1 validation section above. The blue-background report concerned the operated test scene and was resolved by creating a new scene; it is not recorded as a confirmed GMP regression.

## Direct PC/VR input switching (unreleased)

Validated on 2026-10-21 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 on Windows. The focused Play runner passed 182 assertions (`Output~/vr-hot-switch-final.log`). It exercises the production input-selector operation with injected devices: repeated PC/VR handovers, preserving the moved root while final exit restores the original entry pose, missing/throwing providers without interrupting PC, startup-failure rollback and retry, runtime PC view/zoom/follow settings, restored mouse-capture requests, held expression-button release, and single disposal of each source and its camera/menu. The selector remains available during simulation; each VR handover starts calibration.

Localization passed 16,814 checks over 543 keys in each of five languages; package validation passed 1,799 checks. The optional adapter was unchanged. The ten changed main-package files were backed up and synchronized to Test-Avatar. All 506 installed package files match the rebuilt main ZIP; the new switching/reposition methods are compiled, and Unity's native Console counters report zero errors and warnings. The user's subsequent scene selection was preserved. Physical headset display/input, actual native-loader handovers and VRChat client parity remain unverified. This focused run did not supersede the earlier full Play failure; the subsequent complete Play result and camera-precision correction are recorded in the 1.2.1 validation section above. Batch shutdown also reported Unity allocation diagnostics, so passing assertions do not establish a completely clean log.

## PC input by default (unreleased)

Validated on 2026-10-21 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5. The focused Play runner passed 163 assertions, including starting a new tool through its default PC backend, manually selecting VR, and restoring PC after normal VR exit. Failed VR startup retains the user's explicit selection for retry. The main-package change was backed up and synchronized to Test-Avatar; the adapter was unchanged. Log: `Output~/vr-default-pc.log`.

## Shared PC/VR entry and OpenVR startup correction (unreleased)

Validated on 2026-10-21 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and DX11 on Windows.

- PC and VR now retain one Game Simulation button immediately below the input selector. The localized PC option uses "and" rather than a slash, which Unity treats as a popup submenu separator. The selected backend controls startup; errors remain next to the common entry.
- Test-Avatar exposed a startup failure that the original outside-Play checks did not cover: `EditorBuildSettings.AddConfigObject` rejects the adapter's transient settings object. The adapter now passes session configuration to the pinned plugin's native settings export through a small owned loader override. It leaves persisted Editor configuration untouched and retains Valve's subsystem lifecycle, rendering and event pump.
- The focused Play runner passed 159 assertions, including real GameSimulationTool routing through an injected provider, calibration, common exit, failed startup/retry, and subsequent PC entry without constructing a VR provider. A fresh graphics-enabled optional-adapter editor run passed 930 assertions. The new checks execute the native configuration export, compare the original 1.2.1 ABI, monitor asset saves and verify existing/missing settings and failed-initialization cleanup. Localization passed 16,752 checks over 541 keys in each language; package validation passed 1,799 checks.
- The broader Play attempt stopped after 688 assertions at the existing close-distance camera-centering assertion, before reaching the new VR entry checks. The camera production code was unchanged by this fix. This run remains recorded as a failure; the subsequent precision diagnosis and complete passing Play regression are recorded in the 1.2.1 validation section above. Its log is `Output~/vr-entry-play.log`; the focused and optional-adapter logs are `Output~/vr-entry-focused.log` and `Output~/vr-entry-adapter-final.log`.
- The corrected main files and adapter were backed up and installed in Test-Avatar. The original OpenVR 1.2.1 archive also reports missing metadata on its immutable Linux x86 `.so`/`.dbg` files during import; those files are ignored and the original archive was not modified. No C# compilation failure remained. Actual headset display/input and VRChat parity still require the user's hardware test; native configuration checks do not establish successful headset-session startup.

## Optional SteamVR and OSC full-body simulation (unreleased)

Validated on 2026-10-20 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5, Python 3.14 and DX11 graphics on Windows.

- The main package, without the optional SteamVR adapter, passed 933 Play Mode assertions. Existing desktop movement, collisions, cameras and ownership regressions ran alongside real Humanoid/module VR-session checks with injected devices. VR coverage includes three/eleven-point tracking, manual assignments, two-trigger calibration, blocked locomotion with continuing tracking, pause/resume, head/focus loss, animation-channel release, changed tracking spaces, menu exit during rendering, graph reset and state restoration.
- The separate adapter fixture passed 925 editor assertions with the unmodified OpenVR XR Plugin 1.2.1 archive shipped in SteamVR Unity Plugin 2.8.0. Production coordinate conversion, action weights, HMD-only access for mixed OSC hands, per-controller action neutralization and release, and origin-change event subscription/unsubscription were exercised through the installed adapter. Event checks use the original plugin's managed event pump without starting a native headset session. The adapter build validated metadata, controller bindings, GUIDs and complete license material.
- All 48 distribution/release/listing tests passed. Package validation passed 1,799 checks; localization passed 16,751 checks across 541 keys in each of five languages. Local main ZIP/UnityPackage and optional adapter archives were built and validated. No commit, push or publication was performed.
- Graphics checks produced and inspected local/observer images: the local camera clips the head while retaining body and arms, and the observer retains the complete avatar. Images remain under `Output~/VrValidationProject/ValidationOutput~/GestureManagerProValidation/VRVisibility/`. The final Unity logs are `Output~/vr-play-final.log` and `Output~/vr-editor-final.log`. Both fixture projects are disposable copies beneath `Output~/`; the source Test-Avatar project's files were not changed.
- Physical VR devices, streaming drivers, actual stereo display, controller buttons/rebinding, pose prediction/timing, comfort, drift and VRChat client comparisons were **not performed**. No client build or hardware combination is certified by these results. The independent Humanoid solver is not the client's IK implementation; locomotion-proxy replacement, auto-footsteps, full FBT locomotion-toggle semantics and temporary pose-space behavior remain comparison targets in [VR-SIMULATION.md](../../VR-SIMULATION.md). Touch/Index/Vive binding paths were checked against installed profiles; WMR/HP and physical input remain unverified.
- The optional APW reference-calculator comparison and original Gesture Manager coexistence comparison were skipped because their reference checkouts/DLL were absent. Batch logs contain Unity/SDK licensing and shutdown/memory diagnostics; passing assertions do not establish a completely clean batch log.

## AGPL 1.0.0 replacement release validation

Validated on 2026-10-18 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5 and Python 3.14 on Windows.

- The final combined package passed the 815-assertion editor run and a fresh 873-assertion Play Mode run with graphics enabled. The Play runner exercised GPU first-/third-person and observer rendering, physical collision/jump/fall behavior, and actual Windows microphone recording, injected PCM analysis and cleanup. Before that run, all 482 package files and 11 runner/assembly-definition files matched the reviewed source by SHA-256; the run did not change them.
- All 47 distribution, release and VPM-listing tests passed, including the PowerShell packaging entry point. Package validation passed 1,727 checks; localization passed 14,944 checks over 482 keys per language. Archive checks verified all license text and metadata, source identity, preserved Meta material and absence of deleted credit assets.
- Optional APW reference-calculator comparisons for PC/Mobile were skipped because the fixture-only reference DLL was absent. Physical keyboard/mouse input, VR hardware and a VRChat client comparison were not performed. Unity's batch startup/shutdown messages include licensing, UDP and memory diagnostics; passing assertions do not mean a completely clean batch log.
- The maintainer explicitly authorized replacing the published 1.0.0 distribution so that the official release uses AGPL-3.0-only with the limited SDK linking permission. The previous release metadata, all four asset hashes/files, annotated tag and VPM index were backed up before replacement. This exception preserves the existing main-branch commit history and older package versions; earlier distributed copies retain their existing grants. The individual implementation records below describe their earlier validation stages.

## Author-credit interface removal (1.0.0)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with graphics enabled.

- Removed Inspector and custom-radial credit footers, the supporter acknowledgements menu, its remote-list request/cache, dedicated slice/data classes and four unused icons. The main radial retains Options, Expressions, Looks, Clones and Tools as five contiguous entries. Removed five unused credit strings from each translated catalog and the obsolete credit-only argument classification; user-provided names remain untouched.
- The editor integration runner passed 815 assertions after removing ten obsolete author-credit ordering assertions. A fixture-only legacy radial probe was updated to the simplified constructor before the successful run. Localization validation passed 14,944 checks over 482 keys in each of five languages; package validation passed 1,727 checks. The optional original-package coexistence comparison was not run.
- Both local archive formats exclude the twelve removed credit files/metadata and preserve all AGPL, upstream MIT and Meta license files exactly. No model, animation or scene asset was changed by this interface cleanup. Play Mode, physical input and hardware checks were not rerun for it.
- Backed up and synchronized the current package to Test-Avatar. Its Console reported no errors after compilation. Captured and inspected the actual selected GM Inspector: the author footer is absent and the normal title, settings gear, Play Mode and Model Statistics buttons remain visible. The editor stayed in Edit Mode with its previous selection and unsaved scene state. The synchronization's additional byte differences were line endings only; the backup records retain every previous file.

## AGPL version-3-only licensing (1.0.0)

Validated on 2026-10-17 with Python 3.14, Node.js and PowerShell on Windows.

- All 47 distribution, release and VPM-listing tests passed, including the PowerShell packaging entry point. New cases reject missing/empty project license files, truncated AGPL text, an incomplete linking notice, removed upstream MIT permission and manifest declarations that omit the custom linking permission, before writing release output. The test temporary directory was placed inside `Output~` because the sandbox's default temporary directory denied access.
- Package validation passed 1,767 checks using the disposable fixture's installed VRChat Base and Avatars SDK assets. The optional original-package coexistence check was not run because no original checkout was supplied.
- Built local VPM ZIP and UnityPackage archives and verified exact license-file and metadata identity against source. The complete AGPL text matches the official GNU download (SHA-256 `0d96a4ff68ad6d4b6f1f30f713b18d5184912ba8dd389f86aa7710db079abcb0`). The original MIT notice matches baseline commit `9eea5c1` byte for byte. The root license GUID and all Meta SDK files, notices and the native binary are unchanged.
- The license migration changes notices, metadata, packaging checks and documentation. No C# implementation, model, animation or scene asset was changed by this migration; Unity runtime and hardware checks were not rerun for it. No release, published tag or remote asset was modified. Previously distributed versions retain their existing grants.

## Descriptor-height third-person framing (1.0.0)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with graphics enabled.

- Third person now uses avatar-root world X/Z and the descriptor viewpoint's transformed world Y. It adds no height ratio, near-plane clearance or other vertical offset. Removed the experimental mesh-top scan, skinned-mesh baking, cached height and associated focus-height property. First person still uses the complete descriptor viewpoint; collision avoidance follows the same third-person center.
- The new descriptor-height regression failed against the previous mesh-top implementation. The fixed editor runner passed 825 assertions and the Play Mode runner passed 873 assertions. Coverage includes different viewpoint heights, parent rotation and nonuniform scale, body tilt, immediate preview ViewPosition changes, target changes, root translation, accessories, raised geometry, renderer bounds/visibility, no temporary mesh creation, yaw/pitch/zoom/obstruction, first-person placement and existing free-rotation/input/physics behavior. Real session checks cover descriptor heights of 0.6, 1.6 and 2.5 m with nonzero X/Z offsets. A GPU image verifies a descriptor-height marker at screen center.
- Package validation passed 1,752 checks. The production camera and three changed runners matched the disposable fixture by SHA-256. The camera was backed up and synchronized to Test-Avatar, which compiled without Console errors.
- An Edit Mode probe on Chocolat measured both the descriptor world Y and third-person focus Y as 1.040000 m, with focus X/Z at the root and viewport (0.5, 0.5). Changing only the preview viewpoint's X/Z kept framing unchanged; first-person position error was zero. The avatar root transform, descriptor, selection, unsaved scene flag and Scene view framing were preserved, and the probe removed its temporary objects. No model, animation or scene asset was edited.
- Preview ViewPosition updates are read directly on ApplyView; a session retains its existing startup copy of the descriptor's local viewpoint. Runtime editing of the descriptor component, physical controls and a VRChat client comparison were not added or validated. The client's configurable offsets and exact orbit formula remain unsupported.

## Initial 1.0.0 build validation (superseded distribution)

Validated on 2026-10-17 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5, Python 3.14 and PowerShell on Windows.

- The complete 1.0.0 candidate passed 803 editor assertions and 836 Play Mode assertions with graphics enabled. The separate body-axis camera commit passed 792 editor and 828 Play Mode assertions before the keyboard camera-turn fix. Coverage includes independent camera/body turning, view transitions, obstruction, collision, jumping, landing and restoration.
- All 44 distribution, release and VPM-listing tests passed in the real user environment, including the PowerShell build entry point. Package validation passed 1,752 checks and localization passed 15,097 checks across 487 keys in each of five languages.
- The ten changed production scripts, package manifest and three validation runners matched the disposable fixture by SHA-256. No model, animation or scene asset was changed. Version metadata, the download URL, displayed version and changelog agree on 1.0.
- Physical keyboard/mouse input, VR hardware and a VRChat client comparison were not performed for this release. The body-axis pivot remains the documented fallback because an exact client orbit formula is not publicly specified. SDK locomotion proxy replacement and client IK remain unsupported; the 1.0 version does not add those capabilities.

## Third-person body-axis orbit and keyboard camera turning (1.0.0)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with graphics enabled.

- Third person now orbits the upright body axis at 75% of the movement capsule's world height, between the head and torso. First person retains the complete descriptor viewpoint. The public client documentation does not specify an exact orbit-pivot formula; this is the user's requested body-axis fallback, not a measured client default.
- During captured Game-view Tab free rotation, comma/period (`<`/`>`) changes camera yaw only; A/D independently turns the body. A new W/S press still restores camera-relative movement. The independent camera-turn channel is sanitized and uses the configured turn rate without contributing to body `AngularY`. Scene and ordinary first-/third-person turning retain their existing mapping. Updated all five languages' controls help.
- The new camera regression failed against the old implementation because it orbited the descriptor viewpoint. With the new frame mapper but the old motor, the camera-turn regression failed because keyboard camera yaw did not advance. The fixed editor runner passed 803 assertions and the Play Mode runner passed 836 assertions. Coverage includes descriptor offsets, independent body turns, tilt, scale, translation, pitch, zoom, obstruction, opposing simultaneous body/camera inputs, W/S/F5 restoration, collision, jumping, landing and cleanup.
- Package validation passed 1,752 checks and localization passed 15,097 checks across 487 keys in each of five languages. The nine changed production scripts and three test runners matched the disposable fixture by SHA-256. The production scripts were backed up and synchronized to Test-Avatar, which compiled without Console errors.
- An Edit Mode render probe used the actual Chocolat avatar and the production camera component. Its body-axis pivot was (0, 0.866667, 0), projecting to viewport (0.5, 0.5); the root stayed horizontally centered, changing only the preview's descriptor offset left third-person placement unchanged, and first-person position error was zero. The probe preserved the avatar root transform, descriptor, selected manager, unsaved scene flag and Scene view framing, then removed its temporary objects. No model, animation or scene asset was edited.
- Physical keyboard/mouse input and a VRChat client comparison were not performed. Automated input mapping/session checks and the Edit Mode framing probe do not establish complete client camera or locomotion equivalence.

## Release validation (0.5.0)

Validated on 2026-10-17 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5, Python 3.14 and PowerShell on Windows.

- The complete 0.5.0 candidate passed 787 editor assertions and 823 Play Mode assertions with graphics enabled. The independent camera/motor commit also passed 774 editor and 782 Play Mode assertions before the keyboard/session integration. Coverage includes free rotation, view transitions, collision, jumping, landing and restoration.
- All 44 distribution, release and VPM-listing tests passed in the real user environment, including the PowerShell build entry point. Package validation passed 1,752 checks and localization passed 15,097 checks across 487 keys in each of five languages.
- The ten changed production scripts, package manifest and three validation runners matched the disposable Unity fixture by SHA-256. No model, animation or scene asset was changed.
- Physical Tab/WASD/F5/wheel input, VR hardware and a VRChat client comparison were not performed for this release. The automated tests exercise production input mapping and subscribed session events with injected capture/input state. SDK locomotion proxy replacement and client IK remain unsupported.

## Third-person free rotation (0.5.0)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with graphics enabled.

- Captured Game-view Tab enters an independent third-person camera yaw without rotating the avatar. Repeated Tab is idempotent, A/D supplies turning, and a new W/S press queues camera-heading alignment before moving. Previously held forward/backward input continues in free rotation. First-person and observer transitions clear the mode; suspended input retains the orbit, and session cleanup releases the input events and temporary objects.
- The editor runner passed 787 assertions and the Play Mode runner passed 823 assertions. New motor tests exercise actual turning/displacement, delayed alignment and AngularY, held movement, blocked movement, tilt preservation, finite angle wrapping, physical wall collision, jumping and landing. Camera tests cover independent yaw, viewport centering, pitch, zoom, invalid overrides and obstruction. Keyboard tests cover capture/suspension/modifiers, Shift+Tab, new versus held movement, ordinary strafing, Scene Tab/WASD preservation and disposal.
- Session checks invoke the real Game Simulation tool with its own keyboard subscriptions. A test adapter delegates frame construction to the production keyboard mapper; capture state is injected. They verify mouse/body separation, A/D turning, W/S forward/backward alignment, held-W Tab, first-/third-person and observer transitions, zoom-to-first-person, pause, input failure and cleanup. These are automated input-path checks; physical desktop input and a VRChat client comparison were not performed.
- Package validation passed 1,752 checks and localization passed 15,097 checks across 487 keys in each of five languages. All nine changed production scripts matched the isolated fixture and the backed-up/synchronized Test-Avatar installation by SHA-256; the three changed test runners matched the fixture. Test-Avatar compiled without Console errors, and its previous simulation view, distance, movement settings and microphone preference were restored. No model, animation or scene asset was edited.

## Unified simulation camera controls (0.5.0)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with graphics enabled.

- The ordinary Scene Camera switch now controls the active Game Simulation observer and reflects View/F5 changes. Disabling it restores the previous player-camera state, including third person enabled at zero distance. The session suspends ordinary Main Camera synchronization and preserves its original selection, snapshot and saved preference. Cleanup releases the camera-control bridge only for its owner.
- Player views restore their perspective lens after leaving either an orthographic or perspective observer. Third person no longer adds a hardcoded lateral shoulder offset, and collision avoidance follows the centered path behind the descriptor viewpoint.
- The editor runner passed 725 assertions and the Play Mode runner passed 748 assertions. Tests use the real registered Game Simulation tool and ordinary Scene Camera instance, with both saved preference values, repeated on/off, F5/zoom/dropdown changes, zero-distance third person, custom player lens restoration and viewport centering. Cleanup covers normal exit, duplicate owners, an occupied bridge, non-game adapters, input failure, pause, unlink and repeated disposal. Camera component tests also cover heading, pitch, distance, near-eye views, obstruction recovery and visibility restoration.
- Package validation passed 1,752 checks and localization passed 15,066 checks across 486 keys in each of five languages. Production source hashes matched both the isolated fixture and the backed-up/synchronized Test-Avatar installation; camera test runners matched the fixture.
- Test-Avatar compiled without Console errors. A temporary preview-scene camera rendered the actual Chocolat scene through the production camera component: both descriptor eye and root projected to horizontal viewport coordinate 0.5, and the inspected image was centered. Avatar root position/rotation and the scene's unsaved flag were preserved. No model, animation or scene asset was edited. The render probe ran in Edit Mode and does not validate locomotion animation or client equivalence.
- F5/zoom and ordinary tool actions were invoked through their actual subscribed events and tool methods. Physical UI/key input, actual Play transitions with domain reload disabled and VRChat client comparison were not performed.

## Scene Camera restoration (0.5.0)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with graphics enabled.

- Outside Game Simulation, disabling the regular Scene Camera tool now restores the selected camera's original local position, rotation and synchronized projection settings. Target changes restore each camera independently; the last emulator releases the shared override. Play transitions and assembly reloads also release it while preserving the enabled preference.
- The editor runner passed 681 assertions and the Play Mode runner passed 609 assertions. New checks cover repeated toggles, navigation after disabling, target changes, parent movement, two tool instances, idempotent cleanup, temporary-camera rejection and delayed Main Camera availability. The parented-camera fixture stays near the origin to keep its strict transform tolerance independent of distant-world float conversion error. Production source and validation runner hashes matched the disposable fixture. Package validation passed 1,752 checks.
- The production script was backed up and synchronized to Test-Avatar. After compilation, its Console had no errors. An isolated preview-scene probe used the real target setter, update and toggle to verify synchronization, original pose/projection restoration and subsequent updates remaining disabled. The active scene's unsaved flag and saved preference were preserved; no model, animation or scene asset was edited.
- Physical UI clicks and actual Play transitions with domain reload disabled were not tested. The automated lifecycle assertions invoke the same restoration routine directly.

## Release validation (0.4.6)

Validated on 2026-10-17 with Unity 2022.3.22f1, VRChat Avatars SDK 3.10.5, Python 3.14 and PowerShell on Windows.

- The complete 0.4.6 candidate passed 681 editor assertions and 576 Play Mode assertions with graphics enabled. The separate third-person commit was also tested independently before adding the movement fixes, passing 663 editor and 466 Play Mode assertions. Production sources and runners matched the disposable fixture; no avatar model or animation assets were changed.
- All 44 distribution, release and VPM-listing tests passed in the real user environment, including the PowerShell build entry point. Package validation passed 1,752 checks and localization passed 15,065 checks across 486 keys in each of five languages.
- The 0.4.6 VPM ZIP, UnityPackage, manifest and checksums passed local archive validation, including cross-format source/metadata identity. The numeric version, download URL and changelog section agree. The VPM source description now reflects multilingual UI.
- Physical F5/wheel/Space input, VR hardware and a VRChat client comparison were not performed for this release. SDK locomotion proxy replacement and client IK remain unsupported; the working-avatar contact and camera projection checks are recorded below.

## Game observer framing continuity (0.4.6)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- The working avatar's root and hips did not move when entering the observer, but its original Game camera was 0.001 m above the Scene camera due to an inherited Scene Camera tool offset. The simulation observer copied the Scene camera exactly, lowering the camera on entry and shifting the avatar's Game viewport position. The regular tool now copies the Scene camera position exactly as well.
- A same-frame regression failed against the old camera code with a measured 0.0009994507 m camera mismatch. After the fix, the complete Play Mode runner passed 576 assertions. Four perspective/orthographic rounds, including manual Scene navigation, verify camera pose/projection settings, independent Game aspect ratio, unchanged viewport positions for the avatar root and a second world point on entry/exit, fixed Scene framing and cleanup. Existing height, collision, jump/landing, falling, camera and GPU checks also passed.
- The single changed production script was backed up and synchronized to Test-Avatar, then compiled without console errors. After rendering the existing Scene view to initialize its camera after reload, a same-frame Chocolat check measured zero root, hips, camera and viewport displacement across entry and exit. The root viewport stayed at (0.4633600, 0.3766923, 1.1777730); the hips viewport stayed at (0.4622940, 1.0283600, 1.1156350). The temporary test returned to the user's prior Edit Mode and retained Scene framing, selection and unsaved scene state.
- Package validation passed 1,752 checks, and production sources and Unity runners matched the isolated fixture by SHA-256. No model or animation asset was edited. These transform/projection comparisons and automated rendering checks are not physical input or VRChat client equivalence tests.

## Preserve simulation standing height (0.4.6)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- The temporary movement capsule aligns the bottom of its collision skin with the avatar's support foot point. Its skin width, native overlap recovery, floor placement and vertical physics remain enabled; model assets and local model transforms are not adjusted to conceal the displacement.
- The new regression failed against the old production motor on its first gravity/collision frame, reproducing the reported skin-width lift. With the fix, the editor runner passed 681 assertions and the Play Mode runner passed 536 assertions. Standing-height checks use a fixed 0.5 mm tolerance against the pre-simulation root height, covering first contact, sustained idle, walking, repeated entry and jump landing on temporary and existing floors. They also cover small/large capsules and nonzero foot offsets, model-local transform preservation, real wall blocking, low-ceiling jump collision, actual falling beyond the floor, and cleanup/restoration.
- Before the fix, the active Chocolat session's root rose from 0 to 0.01386654 m, matching its 0.01386667 m skin width. The validated motor source was backed up and synchronized to Test-Avatar, compiled, and tested by re-entering through the real Game Simulation tool. After 60 native downward controller moves, maximum root drift was 0.00000011921 m; physical floor contact and Grounded remained true, gravity stayed enabled, and the floor top stayed at 0. The console had no errors. The original selected manager, unsaved scene state and Scene view framing were preserved, and the simulation was left running.
- Package validation passed 1,752 checks and five-language localization validation passed 15,065 checks. Production sources and Unity check scripts matched the isolated test fixture by SHA-256. Movement/jump tests use injected input, and the working-avatar check directly exercises native controller contact; physical key presses and VRChat client equivalence were not validated.

## Automatic simulation gravity (0.4.6)

Validated on 2026-10-17 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Removed the Game Simulation Gravity / Jump field, toggle and unused translation keys. The real tool entry now requests gravity automatically; Scene Collisions remains enabled by default and disabling it also disables gravity and jumping. Low-level motion adapter gravity options remain independent.
- The editor runner passed 663 assertions and the Play Mode runner passed 502 assertions. A new check enters through the actual game tool, verifies a real jump and landing, walks across a physical floor boundary and falls, then re-enters without collisions and verifies no jump or falling. Both exits restore the avatar transform and parameters and release the motor, camera and floor. Input frames and the input-suspension gate are injected; physical Space input and OS focus are not tested.
- Five-language localization validation passed 15,065 checks across 486 keys; package validation passed 1,752 checks. All changed production sources and Unity check scripts matched the isolated fixture by SHA-256.
- Eight production source/translation files were backed up and synchronized to Test-Avatar as a complete set, including the related 0.4.6 camera controls. After compilation, reflection confirmed the removed gravity setting and loaded wheel event, and the console had no errors. Edit Mode, selection, unsaved scene state and Scene view framing were preserved; no temporary simulation objects remained. No model, rig, controller or animation asset was edited.

## F5 and wheel third-person controls (0.4.6)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Game-view F5 toggles the third-person feature instead of cycling through the observer. Captured wheel input changes distance while enabled. Zooming fully in selects first person without clearing the feature flag, allowing wheel zoom back out; F5 disabling prevents wheel re-entry. Explicit Scene Camera selection keeps the observer separate, and the Inspector follows the current wheel distance.
- The editor runner passed 663 assertions and the Play Mode runner passed 466 assertions. Tests cover captured/suspended/disposed input, modified F5/wheel ownership, Scene navigation, signed and nonfinite wheel input, distance bounds, remembered positive distance, the independent zoomed-in state, repeated toggles, renderer visibility restoration, near-eye shoulder-offset reduction, obstruction recovery and cleanup. The existing observer, physics, graph and GPU-rendering checks also passed.
- Eight production source/translation files were backed up and synchronized to Test-Avatar. After compilation, the new wheel event and F5 dispatcher were loaded, the default remained SceneCamera with third person disabled, and the console had no errors. The original Scene view framing, Edit Mode, selection and unsaved scene state were preserved, with no temporary simulation objects left behind.
- Distance uses editor tuning rather than a measured client curve: 0.25 avatar heights per wheel unit, a maximum of six heights, and snapping distances at or below 0.05 heights to first person. TAB free rotation and full client camera settings remain unsupported. No model, rig, controller or animation asset was edited.
- Five-language localization validation passed 15,127 checks across 488 keys; package validation passed 1,752 checks. Physical F5/wheel interaction, VR hardware and a client-side camera comparison were not performed; injected tests do not establish complete client equivalence.

## Release distribution (0.4.5)

Validated on 2026-10-16 with Python 3.14 and PowerShell on Windows.

- All 44 distribution, release and VPM-listing tests passed in the real user environment, including the PowerShell build entry point. The sandbox's temporary-directory access restrictions prevented its fixture setup/cleanup; that run was not counted as a successful validation.
- Package validation passed 1,752 checks against both SDK dependencies; five-language localization validation passed 15,127 checks. Production scripts and validation runners matched the isolated Unity fixture that passed 647 editor and 430 Play Mode assertions. The version/manifest and release-note updates do not change those scripts.
- The release retains the documented SDK proxy-animation limitation and manual input/client/hardware validation gaps. No generated locomotion animation or reference-tool binary is included.

## Optional observer camera following (0.4.5)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- The default Game Simulation view is now Scene Camera, with observer following disabled. The temporary Game-view camera mirrors the active Scene camera's position, rotation and projection settings while retaining the Game view's own aspect ratio. It falls back to the previously selected camera or an available scene camera when no Scene view exists. Observer Game input leaves the mouse free; mouse look/capture remains confined to first/third person. V cycles all three views without changing their existing enum identifiers.
- The live follow switch moves only the current Scene view by actual avatar displacement, including vertical jumping, while retaining manual pan, rotation, zoom and projection mode. With every Scene view closed, it offsets only the temporary Game-view camera and leaves the original scene camera unchanged. Disabling or re-enabling does not catch up on earlier movement. Stop removes this session's accumulated translations independently from each affected Scene view, preserving manual navigation and original camera objects/references.
- The disposable-project editor runner passed 647 assertions. The final Play Mode runner passed 430 assertions, including two real SceneView windows, a real existing camera with every Scene view closed, fixed/default observer behavior, repeated polling, live toggles, jump/landing, posture input, turn-only motion, independent view ownership, manual-navigation preservation, V cycling, cursor state and cleanup. Existing first/third-person GPU rendering checks also passed.
- The production source and translation files were backed up and synchronized to Test-Avatar. Its default session and tool reported SceneCamera with following disabled after compilation. The editor retained its original Scene view pivot/rotation/zoom, Edit Mode, no selection and pre-existing unsaved scene state, with no temporary simulation objects. The final reload logged a VRChat SDK remote-configuration connection timeout and cached-configuration fallback; there were no camera-code compilation errors.
- Five-language localization validation passed 15,127 checks across 488 keys per catalog; package validation passed 1,752 checks. Physical keyboard/mouse interaction, VR hardware, other graphics backends and in-client comparisons were not performed for this camera change. No model or animation assets were modified.

## Game Simulation reference alignment (0.4.5)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11. See [compatibility findings](GAME-SIMULATION-PARITY.md) for the supplied reference archive, official sources and deliberate differences.

- Statically inspected the supplied GestureManager+ package and its editor DLL without installing or executing it. Aligned focused-view Shift/Space/C/Z controls, reference-derived horizontal acceleration, and preservation of existing GM tracking and posture controls. Kept actual collision displacement and ground probes. Default jump impulse follows VRCWorld rather than avatar height.
- The disposable-project editor runner passed 642 assertions. Injected input covers accelerated starts/stops/reversals, forward running with unchanged strafe speed, Scene action taps/repeats, navigation ownership and reset, pose commands, default jump impulse and explicit height overrides.
- The final Play Mode runner passed 308 assertions, including preservation of external VR/tracking/posture changes, explicit crouch/prone toggles and restoration, posture-callback failure and synchronous-stop cleanup, invisible-floor physics, cameras, rendering and the initialized simulator graph. A test-only, explicitly authored time-varying clip advanced through the inherited proxy wrapper; the original zero-duration SDK walking proxy remained unchanged. Neither check claims client locomotion replacement.
- Backed up and synchronized nine production source/translation files to Test-Avatar. The editor recompiled successfully and exposed the new pose/impulse APIs, with no console errors or substitute-gait type. It remained outside Play Mode with no temporary simulation objects, no selection and its pre-existing unsaved scene state. No model, rig, avatar controller or SDK animation asset was edited.
- Package validation passed 1,752 checks and five-language localization validation passed 15,064 checks. The optional original-package GUID comparison and fixture-only APW reference comparison were skipped. Physical keyboard/mouse input, VR hardware and an in-client Build & Test comparison were not performed for this alignment.

## SDK locomotion proxy limitation (0.4.5)

Checked on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows.

- On Chocolat, `Grounded=true` and `VelocityZ=2` selected walk/run clips with zero duration; graph evaluation did not change the thigh pose. [VRChat documents SDK proxy clips as placeholders](https://creators.vrchat.com/avatars/#proxy-animations) that the client replaces with built-in animations. That client-side locomotion replacement remains unsupported by this simulator; the walking-pose issue is not resolved.
- Locomotion proxies retain their original controller references. Generated substitute gaits and their tests were removed instead of presenting invented motion as client behavior. The existing inherited gesture/emote handling is unchanged. No model, rig, avatar controller or SDK animation asset was edited.
- The working Test-Avatar installation compiled without console errors after restoring the original proxy resolver. The generated locomotion type and cached generated clips were absent. Unity remained in Edit Mode with no owned temporary scene objects, and the user's scene retained its existing unsaved state. The restored Play Mode suite passed 292 assertions, including invisible-ground physics and cleanup. Package validation passed 1,752 checks against both SDK dependencies; the optional original-package comparison was not performed.

## Temporary Game Simulation ground (0.4.5)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Game Simulation creates an unsaved, invisible 100-by-100-meter solid ground collider at the avatar's starting foot height when no supporting scene collider exists. The temporary object has no mesh or rendering components. It reuses existing support and destroys only its owned collider on cleanup. The collider is added after temporary flags and scene placement so it participates in runtime physics.
- The real CharacterController raised the avatar by its skin width during horizontal movement. A measured rounding excess of approximately 6 micrometers put the floor beyond the old probe distance. Ground probes now allow an additional 1 millimeter of contact tolerance; grounding still comes from physical support, including during input suspension.
- Editor integration passed 597 assertions for the initial ground implementation. After changing the ground to a collider-only object, Play Mode integration passed all 292 assertions again, including the absence of rendering components. New real-physics checks cover initial and stationary grounding, every walking step, leaving the floor boundary, gravity-enabled jumping and landing, existing-floor preservation, and cleanup after stop, pause, unlink, input/startup failure, floor deletion and disposal. The existing camera, graph, rendering, ownership and microphone checks also passed. The optional APW comparison was skipped because its fixture-only DLL was absent.
- Backed up and synchronized the production scripts to the working Test-Avatar installation. Recompiled outside Play Mode to replace the previously loaded implementation, then verified the corrected Game Simulation on Chocolat: the owned floor had a solid BoxCollider and zero rendering components, `Grounded=true`, and the Base controller reported `Standing`, with `SmallHop` inactive. A captured frontal render confirmed the normal standing pose without a visible floor or floor shadow. Stopping removed every owned temporary object and restored the avatar position. The project retained its initial Play Mode state, selected manager and unsaved scene, without console errors.

Physical keyboard/mouse interaction, other operating systems and graphics backends were not revalidated for this change. Jumping and walking checks used injected motion input; the Chocolat smoke check exercised the production Game Simulation entry and its actual animation graph.

## Package metadata and build entry points (0.4.4)

Validated on 2026-10-16 with Python 3.14, PowerShell, Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows.

- Removed generated source-documentation assets; published manifests retain source identity. Audited tracked assets and both archive formats for orphan metadata, ignored-name metadata and duplicate asset identities.
- Distribution and publishing tests passed all 44 cases, including regression checks for empty-folder extraction, orphan metadata rejection and ignored names with their metadata. The PowerShell entry point was executed against an isolated fixture to verify filtering and a nonzero exit on validation failure.
- Built and validated actual VPM ZIP and UnityPackage archives. A separate fixture archive added nested empty asset folders and deliberately ignored scratch files. Unity imported the extracted package successfully, retained the empty folder's original GUID and confirmed that generated documentation and ignored scratch entries were absent. No missing-folder metadata warning or script compilation error occurred.
- Restored the disposable Unity project's previous package after the fixture check. The working Test-Avatar installation was not changed by these packaging checks.

These checks cover packaging and Unity import. They do not add hardware, visual-layout or other-platform validation beyond the existing records below; the release workflow separately runs distribution tests on Windows, macOS and Linux with Python 3.10 and 3.12.

## Five-language localization audit (0.4.4)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Audited the existing interface, OSC, microphone and model-statistics terminology against their implementations. Corrected misleading Chinese SDK grades, mesh/constraint/bounds labels, OSC address-mapping instructions, HSV brightness, friend-parameter spelling and stale standalone lip-sync wording. English display corrections keep canonical translation keys stable. Technical identifiers, user names, product names and author credits retain their original identities.
- The settings selector lists Automatic, English, Japanese, Korean, Traditional Chinese and Simplified Chinese. Existing persisted language values retain their meanings. Automatic resolves Japanese/Korean and Chinese scripts/regions, with English as the fallback. Centralized catalogs remove initialization-order-dependent registration; all five languages cover 486 keys.
- Translation provenance retains structured format arguments across live switches. Cached captions, nested diagnostics and localized author-credit order update correctly, while user names containing colons, line breaks or text equal to a caption remain unchanged. Only English source templates can recognize untagged runtime exception messages.
- Static localization validation passed 15,064 checks, and graphics-enabled Unity editor integration passed 597 assertions. Checks cover all locales in both switching directions, automatic selection, parameter/name preservation and shared Japanese/Korean/Traditional Chinese font chains with representative glyphs. Static package validation passed 1,753 checks against the original GM baseline and both SDK packages.
- Captured and visually checked 35 production views across the five languages: Edit Mode, settings, expanded Game Simulation, the radial Tools submenu, avatar debugging, OSC and Model Statistics. Long buttons and toggle labels size to their captions, wheel labels wrap within their existing slots, and descriptive text wraps instead of clipping. Fixed a missed OSC undock-button translation exposed by these captures.
- Synchronized the final production files and README to Test-Avatar after checking the installed baseline, with backups and unchanged existing asset metadata. The five native language names appear in its settings. The project remains in Edit Mode with Automatic selected, GestureManagerPro selected and its existing scene unsaved. Unity compiled without console errors.

The visual review used the Windows dark theme at pixelsPerPoint 1 and a generic fixture avatar. Physical dropdown interaction, light theme, higher display scaling, other operating systems and hardware behavior were not revalidated for this localization change.

## Microphone lip sync integrated into Game Simulation (0.4.4)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Game Simulation is the single feature entry in both the main Tools group and radial Tools submenu. Its inline settings include default-enabled Microphone Lip Sync, device selection, gain, noise floor and mapping diagnostics. The independent lip-sync tool, AudioClip test path and monitoring controls were removed. The renamed microphone helper retains its C# asset GUID, and audio processing remains in its separate assembly.
- Microphone recording starts after motion control successfully starts. The motion session's stop notification synchronously releases recording and restores mouth/speech state for exit, pause, avatar loss, failure or disposal. Missing/failed microphone input reports a warning while movement remains usable. Monitoring is always muted. In scenes without an existing AudioListener, the temporary listener is a separate child so the microphone filter does not also process the global listener output.
- Editor integration passed 439 assertions. The final Play Mode suite passed 233 assertions, including microphone disabled/unavailable cases, real USB microphone recording, muted output, native analysis of an injected PCM frame, speech-parameter restoration, repeated stop, pause and avatar-loss cleanup. Static package validation passed 1,723 checks against the original GM baseline and both SDK packages. The optional APW reference comparison was skipped because its fixture-only DLL was absent.
- An additional production-Inspector probe passed eight checks and captured five views: collapsed tools, expanded Game Simulation in Chinese/English, the radial submenu and the running combined session. Real microphone samples reached the native analyzer through Unity's audio callback; stopping through the Inspector released recording started through the radial menu. The main-group layout, localized settings and radial entry were visually checked.
- Final sources and README were synchronized to Test-Avatar with backups and unchanged asset metadata. Unity compiled without console errors. Play Mode was restored with Chocolat selected and Game Simulation expanded for review; the user's scene remains unsaved.

Recording lifecycle and signal flow were tested with an available USB microphone, and the analyzer was also given deterministic PCM. Spoken-phoneme recognition accuracy, visible humanoid mouth motion, physical movement controls, light theme, higher display scaling and other operating systems were not revalidated. This integrated microphone presentation supersedes the separate lip-sync tool in the following records.

## Main tool group and radial shortcuts (0.4.4)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Lip Sync Test and Game Simulation appear directly after Scene Camera, Clickable Contacts and Pose Avatar, above the Extra Tools heading. Both are standard toggles in the radial Tools submenu with the existing viseme and movement icons, running-state colors and link to settings in the Tools tab.
- The module lazily resolves its controlling manager and owns one registered tool list shared by the Inspector and all of its radial menus. Opening the radial submenu before visiting Tools initializes the same sessions and defaults. Each feature remains in its separate assembly; no new assets or dependencies were added.
- Graphics-enabled editor integration passed 442 assertions, including radial-first initialization, explicit owner binding, shared tool instances, icon resolution and live translated captions. Static package validation passed 1,725 checks against the original GM baseline and both SDK packages.
- Eighteen real Play Mode checks passed, including starting through the radial menu, stopping through the Inspector, starting through the Inspector and stopping through the radial menu, running-state highlights, pause, collapse/tab changes and avatar-disconnection restoration. Ten production Inspector captures cover the Chinese/English radial submenu and inline controls. The submenu in both languages and the main-group layout were visually inspected.
- The seven changed production/documentation files were synchronized to Test-Avatar after checking its installed sources against the previous commit, with backups and unchanged metadata. The actual 469-by-918 Inspector shows both entries in the radial submenu with Chocolat selected. Play Mode was restored, the scene remains unsaved and the Unity console contains no errors.

The audio check uses a synthetic silent AudioClip. Microphone hardware, physical input, light theme, higher display scaling and other operating systems were not revalidated. This grouping and radial access supersede the Extra Tools placement in the following record.

## Standard inline avatar tools (0.4.4)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in the disposable Windows fixture with Direct3D 11.

- Lip Sync Test and Game Simulation now inherit the same GmgDynamicFunction implementation as the original avatar tools. They appear as normal collapsed entries under Extra Tools, with the shared foldout frame, running-state green highlight, inline settings and start/stop action. Their separate windows and unused UXML/USS assets were removed; the renamed C# assets retain their GUIDs. Each feature still lives in its own assembly and uses its existing session implementation.
- Tool factories bind once to the displayed manager and are owned by that module's AvatarTools instance. Closing a foldout or changing tabs preserves the session. Pause, Play Mode exit, assembly reload and avatar disconnection stop or dispose the sessions and restore their owned resources. Running, expanded tools request Inspector repaint only while the Tools tab is visible.
- Graphics-enabled editor integration passed 433 assertions, including standard-tool registration, repeated registration, manager binding, first-person and microphone-monitoring defaults, live localized captions and existing native, motion, camera, statistics and font checks. Static validation passed 1,721 checks against the original GM resource baseline and both SDK packages.
- Eight production-Inspector captures at 470 by 1080 pixels cover collapsed tools, each expanded tool in Chinese and English, running-state highlights and a paused tool. Twelve real Play Mode checks start the production AudioClip and game-simulation actions, preserve running sessions across collapse/tab changes, stop lip sync on pause, and dispose both tools on avatar disconnection while restoring player ownership and the original avatar position.
- The final sources were synchronized to the open Test-Avatar project with backups and unchanged script GUIDs. Unity compiled without console errors. Its actual 469-by-918-pixel Inspector shows both standard entries beside the inherited tools, with Chocolat selected; Play Mode was restored after compilation and the scene remained unsaved.

Audio uses a looping synthetic silent clip; microphone hardware, physical keyboard/mouse input, light theme, higher display scaling and other operating systems were not revalidated. This shared inline-tool presentation supersedes the standalone feature windows and special button row described in earlier records.

## Original Inspector layout and contextual tools (0.4.4)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in the disposable Windows fixture with Direct3D 11.

- The main Inspector keeps the original version heading, gear settings and two-button Edit Mode layout. Model Statistics occupies the original Discord shortcut's position beside Enter Play-Mode. The language selector is inside the existing gear settings, above the unchanged general and simulation sections.
- Lip Sync Test and Game Simulation contribute their existing actions to the initialized avatar's Tools tab. No runtime action is exposed in Edit Mode, settings or the Gestures tab. Contributions retain the originating manager, require an active initialized module and are disabled while paused. Returning to Edit Mode removes the runtime actions. No assembly dependency, persistence key or asset change was needed.
- Graphics-enabled editor integration passed 446 assertions. Static validation passed 1,739 checks with the original GM 3.9.9 resource baseline and both installed SDK packages supplied.
- Eight production-Inspector render captures at 470 by 760 pixels cover Chinese and English Edit Mode, settings and Tools, paused Tools, and returning to Edit Mode. Actual Play Mode transitions initialize a generic SDK3 avatar through SetModule. The independent statistics action opens its production window in Edit Mode; that window can remain open during Play Mode. This placement supersedes the earlier Inspector feature-access layout below.

The render probe uses a temporary host for the production custom Inspector in the disposable fixture. The four changed production scripts were subsequently synchronized to the open Test-Avatar project, retaining its asset metadata. Unity compiled without console errors, and the actual Inspector render confirmed the new Edit Mode layout while preserving the selected object and unsaved scene state. Physical mouse/dropdown interaction, microphone hardware, light theme and higher display scaling were not revalidated.

## Stable multi-page editor fonts (0.4.3)

Validated on 2026-10-16 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Font validity and resource retention now inspect only `atlasTextureCount` active pages. TextCore can reserve a four-slot array for three valid atlas pages; its unused null slot no longer destroys and recreates the entire language font chain on every Editor update. Retention also avoids rewriting unchanged hide flags. Missing active pages or materials still trigger recovery.
- Graphics-enabled editor integration passed 446 assertions; 1,739 static package checks passed. The regression generates a real three-page CJK font with reserved null capacity, verifies stable font/material/atlas instances over 200 refreshes, and checks retention and recovery from destroyed active resources.
- The running Test-Avatar Model Statistics window was exercised for 180 Editor updates, including 20 warm-up updates and 160 measured updates. Eighty dispatched UI Toolkit wheel events produced 28 effective scroll changes over offsets 0–711; `EditorWindow.position` drove 40 position changes and 40 size changes. All 17 font/material/active-atlas instance IDs remained unchanged, with zero font-definition reads and glyph-rasterization calls after warm-up. The three-page/four-slot CJK atlas remained valid throughout.
- Across those 160 updates, the font-maintenance callback took a median 0.0255 ms, 95th percentile 0.0703 ms and maximum 0.1856 ms. These are callback timings on this machine, not total Editor frame times. No errors were logged. The original window rectangle, scroll offset, selected snapshot, history and unsaved scene state were preserved.

Wheel dispatch and window geometry changes were automated through Unity APIs; physical mouse title-bar dragging was not separately tested. Other operating systems, graphics backends and display scaling were not checked for this change.

## Non-readable help texture copy (0.4.3)

Validated on 2026-10-15 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 on Windows with Direct3D 11.

- Replaced `Instantiate` of Unity's built-in help texture with a private texture of the same graphics format, dimensions and mip count, populated by [Graphics.CopyTexture](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.CopyTexture.html). This supports the CPU-unreadable native icon without changing its readability, Point filtering or Clamp wrapping. Each window keeps its own cached Bilinear copy and releases it on close.
- Editor integration passed 439 assertions without a graphics device and 442 with graphics enabled. The GPU regression discards a patterned source texture's CPU data, requires zero copy errors, checks its known alpha values and compares every rendered color/alpha pixel with the copy. Existing cache reuse, separate-window ownership and cleanup checks also pass. All 1,739 static package checks pass.
- The running Test-Avatar project compiled and opened Model Statistics without errors. Its actual 32x32 native help source remained CPU-unreadable; the private copy matched every GPU-rendered pixel, including all 325 nontransparent pixels. None and unknown grades reused that copy. Shared source settings, the active render target and the unsaved scene state were preserved. The console contained no errors after the check.

Other graphics backends, light theme and higher display scaling were not checked for this change.

## Inspector feature access (0.4.3)

Validated on 2026-10-15 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in the disposable Windows fixture.

- The emulator Inspector now exposes the global language selector and Model Statistics, Lip Sync Test and Game Simulation buttons beneath its version heading. The controls are available before Play Mode and while inspecting the prefab. Feature windows no longer register standalone Tools menu items, and localization no longer registers a Unity Preferences page.
- Editor integration: 438 assertions passed, including feature-menu and settings-provider absence, one Inspector contribution from each feature assembly, explicit window binding to the originating emulator, language/cache switching and the existing motion/statistics checks. The original add-emulator Tools command remains unchanged.
- Distribution: 1,739 static package checks passed. Removed the unused settings script, metadata and its two translations; no assembly dependency or prefab data changes were needed.
- The running Test-Avatar project compiled the updated sources without errors. Inspector render captures verified the language selector and all three feature buttons on a scene instance in Chinese and English, and on the prefab asset in Chinese, at widths of 464–469 pixels. Invoking the production entry actions opened all three windows; Game Simulation and Lip Sync Test bound to the originating instance. The check restored the original selection and language, closed its temporary windows and preserved the scene's unsaved state. Native mouse/dropdown interaction was not validated.

## Shared localized editor fonts (0.4.3)

Validated on 2026-10-15 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in the disposable Windows fixture.

- Model Statistics, Lip Sync Test and Game Simulation now share language-specific regular system font chains to avoid inconsistent Chinese weights from Unity's default font fallback cache. The design follows [NDMF's language font preferences](https://github.com/bdunderscore/ndmf/blob/89c8f6d18918cdd54fa20148070bea0578ccc1b6/Editor/UI/Localization/LanguagePrefs.cs#L266); it adds no NDMF dependency or font binary. On this Windows fixture the Latin chain includes Verdana, Tahoma and Arial; Simplified Chinese adds Microsoft YaHei UI and Microsoft YaHei. All faces have empty alternate-weight tables, so headings use synthetic bold over the regular glyphs.
- Editor integration: 438 assertions passed. Checks cover shared fonts in all three production windows, language/cache selection, automatic-language culture policy, idempotent attachment, child material/atlas retention, complete-chain recovery after a destroyed fallback material, and detached-window cleanup. Also fixed a reentrant lip-sync language dropdown callback exposed by opening multiple localized windows.
- Actual rendering and lifecycle: all three production windows were inspected in Chinese and English, with Model Statistics at widths of 620 and 460 pixels. Each Chinese stage verified 667 regular and 28 synthetic-bold CJK glyph instances against their styles and the owned fallback chain. The English stage preserved 11 Chinese user-name/value glyphs. The same checks passed during actual Play Mode and after returning to Edit Mode across domain reloads. Ratings were synthetic typography samples, not avatar performance measurements.
- Distribution: all 42 Python tests and 1,745 static package checks passed. Candidate families remain data in `Resources/GestureManagerPro/EditorFontPreferences.txt`; the implementation skips missing families and falls back to the editor default if no configured face loads. Shared owned resources are released on assembly reload or editor shutdown.

Only the Windows dark theme at pixelsPerPoint 1 was visually inspected. Light theme, higher display scaling and macOS/Linux font resolution and rendering remain unverified.

## Unrated help icon (0.4.2)

Validated on 2026-10-15 in the existing disposable Windows fixture with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5.

- Editor integration: 427 assertions passed. Resource, caption and rendered-rating checks require a private native 32x32 Unity `d__Help@2x` copy with bilinear filtering for None and unrecognized ratings. Repeated requests reuse that window's texture; separate windows own separate copies. Closing a window destroys its copy while preserving other windows and Unity's shared texture/filter settings. Empty-history cleanup remains covered.
- Desktop UI: an actual UI Toolkit comparison at 16, 24 and 32 pixels reproduced jagged contours from the native help texture's Point filtering, including at the metric row's 24-pixel size. Bilinear filtering on a private copy smooths the scaled contour. The corrected production statistics window was inspected with synthetic Chinese ratings: overall and per-metric **Not Rated** icons retain their image bounds, caption alignment and spacing alongside the five SDK performance icons. Table headings and bundle file sizes do not acquire an unrated icon. Only dark theme at pixelsPerPoint 1 was inspected.
- Compatibility and distribution: Diagnostics compile against SDK 3.10.4. The 42 Python distribution tests and 1,733 static package checks pass. No asset, dependency, calculator or processing-stage change was introduced.

The visual sample uses synthetic ratings solely to inspect icon rendering and placement. It is not an avatar performance measurement. The earlier 32x32-only fix did not correct Point sampling at scaled sizes; this filtering check supersedes that visual assessment. This also supersedes 0.4.1's absence of an unrated icon; other 0.4.1 validation limits still apply.

## SDK rating icons and terminology (0.4.1)

Validated on 2026-10-15 in the existing disposable Windows fixture with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5.

- Editor integration: 424 assertions passed, including 16 additional checks for the actual SDK icon resources, overall and per-metric image/label binding, localized captions, snapshot preservation during language changes, and icon removal for unrated, unrecognized and empty-history states.
- Desktop UI: controlled display samples show all five SDK icons alongside their Chinese captions. The actual window was inspected at widths of 680 and 460 pixels, then switched to English while retaining the same record. Overall captions fit without splitting the Chinese rating; metric values and long labels wrap within the table.
- Terminology: Simplified Chinese ranks and shared performance metric labels match the local VRCX-0 `src/localization/zh-CN.json` entries under `dialog.avatar.performance`. Non-local Contact scope and Bounds center/extents remain explicit rather than adopting a less precise label.
- Compatibility and distribution: Diagnostics compile against SDK 3.10.4, which also contains all five referenced icon assets. Unity integration uses SDK 3.10.5. The 42 Python distribution tests and 1,733 static package checks pass. Icons are loaded from the existing SDK dependency; no copied artwork, native binary or dependency was added.

This update changes presentation and terminology only. The 0.4.0 SDK calculation and processing-stage behavior remain unchanged. Display samples use synthetic ratings to expose every visual state and are not avatar performance measurements. Light-theme UI and macOS/Linux editor visuals were not inspected.

## Processing stages and APW alignment (0.4.0)

Validated on 2026-10-15 in the existing disposable Windows fixture with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5.

- Editor integration: 408 assertions passed. All 31 SDK values and ratings are checked independently for PC and Mobile, including optional sizes, raw constraint depth, full Bounds, PhysBone null handling, SDK ignore-delegate preservation, two-platform build status, 48-record history, stage filters, localization and legacy snapshot formats.
- Play Mode integration: 281 assertions passed. A real Play transition invokes the SDK preprocessing callback pipeline with a controlled processor that removes one of two cubes. Both platform histories independently retain 24 triangles before processing and 12 in the SDK and first-Editor-update Play observations. Manual Play scans have their own source and cannot overwrite the before-processing stage. Existing simulator, input, graph and GPU camera checks pass.
- APW reference: all 31 metric values and ratings, plus the overall rating, match the original APW calculator for both PC and Mobile on the processed fixture. Only the unmodified DTO/calculator code was extracted from anatawa12 gists pack 0.25.3 into a temporary reference DLL; its UI, callbacks and fallback-status mutation were excluded. Upstream source SHA-256: `aaab1dfbb6afb966c4b8b9f72b35f6a2f2568ab7433806b2dcc98afdc45dda22`. The reference DLL is not distributed with Pro.
- Desktop UI: the actual Chinese statistics window displays 24 triangles before processing and 12 after selecting the Play-processing stage. The stage menu, retained snapshot selection, current-Play measurement caption and full Bounds are visible and usable.
- Compatibility and distribution: final Diagnostics sources compile against SDK 3.10.4 and run in Unity against 3.10.5. The 42 Python distribution tests and 1,733 static package checks pass. No new package dependency or native binary was introduced.

This controlled processor checks lifecycle timing; it is not an acceptance test for production avatars or NDMF/MA/AAO combinations. SDK build completion/error correlation is tested with injected events, without a real bundle build or upload. The Play transition cannot guarantee that it runs before earlier tools' transition callbacks. SDK observations run at the last preprocessing priority, without claiming to include later bundle-time transformations. Android imports/builds and macOS/Linux editor operation remain unverified. This section supersedes the 0.3.0 capture/filter behavior below; old records keep their original values in the legacy stage.

## Model Statistics (0.3.0)

Validated on 2026-10-15 in the existing disposable Windows fixture with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5.

- Editor integration: 267 assertions passed, including 62 new statistics checks. Known geometry and PC/Mobile grades match the installed SDK calculator. Checks cover shared asset/component state, temporary simulator Contacts, same-name avatar isolation, bounded SessionState history, interrupted build records, localized window controls and stylesheet preservation.
- Play Mode integration: 146 assertions passed. Actual simulator initialization records both platform previews before temporary contacts/animator changes; existing graph, input, camera rendering and cleanup checks still pass.
- Desktop UI: clicked the actual Inspector's replacement Model Statistics button and confirmed the Chinese window renders the selected test avatar, SDK metrics and source labels. First-open sizing keeps statistics visible; detailed platform/data caveats are collapsed below the table.
- Compatibility: Diagnostics sources compile with Unity's Roslyn against SDK 3.10.4 and 3.10.5 binaries. The referenced builder/control-panel source contracts are byte-identical between those versions; full Unity integration was performed on 3.10.5 only.
- Distribution: 42 Python tests and 1,733 static package checks passed; VPM ZIP and UnityPackage asset/GUID parity checks passed. No new package dependency was added.

SDK build lifecycle tests inject SDK event payloads and invoke the observer; they do not execute an actual bundle build, upload, or third-party optimizer pipeline. Production humanoid avatars, NDMF/MA/AAO processing combinations, Android imports/builds and macOS/Linux editor UI remain unverified. Download/uncompressed sizes and constraint depth stay explicitly unavailable; SDK-confirmed bundle file size is separate from download size.

## Window styles and Scene focus (0.2.2)

Validated on 2026-10-15 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in a disposable Windows project containing the real simulator graph and a generic test avatar.

- Automated integration: 205 editor assertions and 144 Play Mode assertions passed, including default/external stylesheet preservation, language rebuilds, short-tap consumption and navigation/reset cleanup. The Play Mode checks also rendered first-person, third-person and independent-observer images on the actual graphics device. Distribution validation passed 42 Python tests and 1,685 static package checks.
- Native desktop input: opened the Game Simulation controls, entered Play Mode, started the session, switched Game to Scene, and pressed WASD and comma/period. All six Scene controls moved or turned the model while the Scene camera position, rotation, pivot and size stayed unchanged. Short key down/up events delivered in the same player frame now survive until one motor sample.
- Native focus and Game input: returned from Scene to Game, confirmed input stayed suspended until clicking the Game canvas, then verified movement, mouse look, V switching between first and third person, Esc release, and Exit and Restore. Recapture did not jump the model yaw or view pitch. Exiting restored the original model pose and unlocked visible cursor.
- Actual Scene window event integration: sent right-button and Alt orbit gestures, including W during navigation, through `SceneView.SendEvent` over successive editor frames. Unity's own Scene navigation moved/rotated its camera; each gesture changed the camera angle by approximately 22.20 degrees with zero model translation or rotation. Model input was suspended during navigation and rearmed after release; a subsequent native W press moved the model after each gesture. The fixture never wrote the camera transform or called the private input handler.

The desktop automation API cannot hold a mouse button or Alt while dragging. Those two combined gestures were tested through Unity window events, not physical OS-held input. Native single keys, mouse look, clicks and real view focus changes were tested separately. Real humanoid-avatar scenes, microphone hardware, macOS/Linux Unity operation and SRP rendering remain unverified.

## Scene input and direct Tools actions (0.2.1)

Validated on 2026-10-15 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in a separate disposable Windows project.

- Editor integration: 193 assertions passed, including the four actual direct Tools menu registrations, English/Chinese window content, and injected Scene GUI events driving the avatar motor. Checks cover WASD/turning, normalized diagonals, right/middle mouse and Alt navigation event ownership, modified shortcuts, key release, focus-reset recovery, cursor preservation and disposal.
- Play Mode integration: 144 assertions passed, including the simulator graph, first/third-person rendering, movement publication, exclusive ownership, pause/unlink/failure cleanup and restoration.
- Distribution: 42 Python tests and 1,685 static package checks passed. The release archives retain the existing asset GUIDs and unchanged native audio library.

Scene input checks inject events into the production event handler; they do not reproduce physical keyboard input or real window focus transitions. Actual Scene camera navigation, Game mouse capture, real avatar scenes and hardware acceptance remain manual checks. No Scene camera position, rotation or navigation setting is changed by the Scene input implementation. The platform and rendering limits listed for 0.2.0 still apply.

## Game Simulation (0.2.0)

Validated on 2026-10-14 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in the disposable Windows VPM project.

- Editor integration: 167 assertions covering the existing simulator, native audio backend, localization and user identifier preservation, plus game-mode UI choices, bounded mouse look, sprint/jump physics, viewpoint transforms, camera obstructions and renderer restoration.
- Play Mode integration: 144 assertions covering the real simulator graph, motion/parameter publication, first/third-person switching, exclusive Game-view ownership, camera loss, pause/unlink/input failure, restoration callback errors and reentrant start rejection.
- GPU rendering: actual first-person, third-person and independent-observer RenderTexture images verify that the face mesh does not obstruct first-person rendering, third person shows the local avatar and another camera still sees it after first-person rendering. Deterministic callback checks also cover nested-camera visibility and cleanup.
- Distribution: 42 Python tests and 1,685 static package checks passed. The existing asset GUIDs and unchanged Meta native library are preserved.

First person hides the entire local avatar for its simulation camera. Head-only clipping, visible first-person body/arms, VR tracking and IK remain unimplemented. Physical keyboard/mouse capture, focus changes, Chinese layout/glyph rendering, microphone hardware and real humanoid-avatar scenes still require manual acceptance. macOS/Linux Unity functionality and SRP rendering were not validated.

## Initial standalone baseline (0.1.0)

Validated on 2026-10-13 using Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in a disposable Windows project. The original Gesture Manager and the earlier extension prototype were removed from that project through the Unity Package Manager API before testing Pro.

- Static package validation: 1,685 checks passed; 257 distinct asset GUIDs; all 203 serialized YAML references resolved against Pro and the SDK fixtures. No original-package GUIDs were reused.
- Unity editor checks: 117 assertions passed, including compilation, separate assemblies, all eight built-in controller/restore pairs, prefab/resources, English/Chinese switching, cached captions, custom names/parameter identity preservation and native analyzer initialization/silence processing.
- Unity Play Mode checks: 84 assertions passed, including deterministic motion input, parameter publication, exclusive session ownership, stop/pause/unlink/failure cleanup, restoration and a fully initialized generic-avatar PlayableGraph with eight default animation layers.
- Controller cleanup: 682 unreachable serialized objects removed from the eight controllers and their identical restore snapshots. Two independent reachability checks covered local, same-GUID and cross-controller references. All retained object blocks remained unchanged; the cleanup leaves no unresolved legacy SDK references.

The bundled binary is unchanged from Oculus Lipsync Unity SDK 29.0.0; its reported native version is 1.54.0. Binary hash and provenance are recorded under `ThirdParty/OVRLipSync`.

Physical key presses, Game-view focus transitions, microphone hardware, collision/gravity behavior in real avatar scenes, Chinese glyph/layout appearance and humanoid body animation were not manually validated. VR tracking, calibration and IK are future implementation work. These are release checks to complete before presenting this development build as a stable release.
