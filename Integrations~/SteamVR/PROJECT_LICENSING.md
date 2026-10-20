# Gesture Manager Pro licensing

Copyright (C) 2026 VRCLearn contributors (Gesture Manager Pro changes).
The inherited Gesture Manager code is Copyright (C) 2019-2023 BlackStartx.

## Project license

Except for the third-party materials identified below, Gesture Manager Pro,
as a combined work, is licensed under the GNU Affero General Public License,
version 3 only (`AGPL-3.0-only`), with the additional permission in this file.
This licensing change applies to the project revision introducing this notice
and subsequent revisions. It does not revoke permissions already granted for
previously distributed copies, including releases published under MIT.

This program is free software: you can redistribute it and/or modify it under
the terms of version 3 of the GNU Affero General Public License as published
by the Free Software Foundation. No option to use a later version is granted.

This program is distributed in the hope that it will be useful, but WITHOUT
ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more
details. The complete, unmodified license text is included in [LICENSE.md](LICENSE.md).
Its concluding application example is not this project's license grant; the
version-3-only grant above governs this project.

## Additional permission under GNU AGPL version 3 section 7

If you modify this Program, or any covered work, by linking or combining it
with any of the following dependencies, the licensors of this Program grant
you additional permission to convey the resulting work:

1. The Unity Editor and Unity Engine, including the UnityEditor and
   UnityEngine libraries supplied by Unity Technologies.
2. The VRChat Base and Avatars SDKs, including components supplied as part of
   those SDKs by VRChat.
3. The unchanged Oculus Lipsync Unity SDK 29.0.0 Windows x86_64 native library
   distributed here as `ThirdParty/OVRLipSync/Plugins/Win64/VRCLearnOVRLipSync.dll`,
   identified by the hash and provenance in `ThirdParty/OVRLipSync/PROVENANCE.json`.

For such a combination, you are not required by this Program's license to
provide the source code of those dependencies. All covered Gesture Manager
Pro code, including modifications and its interfaces to those dependencies,
remains subject to the GNU AGPL version 3, including its applicable
Corresponding Source and network-interaction requirements. This permission
does not extend to other dependencies or permit licensing covered Gesture
Manager Pro code or modifications under proprietary terms.

This additional permission does not relicense the dependencies, grant rights
on behalf of their copyright holders, or waive their license conditions.
Use and redistribution of each dependency must also comply with its own
terms. You may remove this additional permission as provided by section 7.
Authors of modifications may choose whether to extend this permission to
their modifications.

## Third-party materials

- **Gesture Manager 3.9.9**, baseline commit `9eea5c1`, by BlackStartx:
  the original MIT copyright and permission notice is preserved verbatim in
  [ThirdParty/GestureManager/LICENSE.txt](ThirdParty/GestureManager/LICENSE.txt).
  The inherited material remains available under its original MIT terms;
  this does not offer Gesture Manager Pro's combined work or new changes
  under MIT.
- **Oculus Lipsync native library**: remains under its separate Meta/Oculus
  SDK terms, not the GNU AGPL. Its license reference, third-party notices,
  provenance and unchanged binary are retained in
  [ThirdParty/OVRLipSync](ThirdParty/OVRLipSync).
- **Unity and VRChat SDKs**: obtained separately and remain under their own
  terms. This repository does not relicense or bundle those SDKs.

The package manifest uses `SEE LICENSE IN LICENSING.md` so that the
version-3-only grant, limited linking permission and third-party scope are
read together instead of describing the entire distribution as plain AGPL.
