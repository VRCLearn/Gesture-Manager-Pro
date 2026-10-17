# Upstream baseline

- Project: https://github.com/BlackStartx/VRC-Gesture-Manager
- Baseline release: Gesture Manager 3.9.9
- Baseline commit: `9eea5c1` (the release commit inherited by this repository)
- Upstream license: MIT, with the original notice retained verbatim at `ThirdParty/GestureManager/LICENSE.txt`.

Gesture Manager Pro's combined work is licensed under GNU AGPL version 3 only with the limited SDK linking permission in `LICENSING.md`; `LICENSE.md` contains the complete GNU AGPL text. The inherited material retains its MIT terms, and the bundled native analyzer retains its separate Meta SDK terms. Earlier distributed versions retain their existing license grants.

This repository preserves the inherited Git history and develops on `main`. The simulator is embedded as source, with Pro-specific namespaces, assembly names, resource paths, and asset GUIDs. It is not a VPM dependency on the original Gesture Manager package.

Compare upstream changes against the recorded baseline, review affected SDK and simulator behavior, then port relevant fixes. Avoid wholesale replacement of localized/control-integrated files. SDK compatibility belongs to this project and must be validated before expanding dependency ranges.

Pro targets SDK3. The obsolete SDK2 module and its controller templates have been removed. Unreachable serialized objects were pruned from the eight built-in controllers and their matching restore snapshots; every object reachable from the original controller graph was preserved verbatim. This removes old, unused SDK references without guessing replacements for them. VR tracking input and body IK are separate planned milestones.
