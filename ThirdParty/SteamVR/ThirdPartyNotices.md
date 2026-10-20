# External dependencies

The OpenVR XR Plugin 1.2.1 is supplied by Valve in the unmodified archive
`Assets/SteamVR/OpenVRUnityXRPackage/Editor/com.valvesoftware.unity.openvr-1.2.1.tgz`
of the official SteamVR Unity Plugin 2.8.0 tag:

https://github.com/ValveSoftware/steamvr_unity_plugin/tree/2.8.0/Assets/SteamVR/OpenVRUnityXRPackage/Editor

Valve's XR plugin uses the BSD 3-Clause license, except identified Unity headers
under the Unity Companion License. Obtain and preserve all notices in the
original dependency. The dependency is downloaded separately and is not included
in the Gesture Manager Pro distribution.

`Scripts/Editor/SteamVR/SteamVrOwnedLoader.cs` contains configuration ABI declarations and a
minimal initialization sequence derived from that plugin's `OpenVRLoader.cs`.
Those portions retain the following notice:

BSD 3-Clause License

Copyright (c) Valve Corporation
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met

1. Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation andor
other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors
may be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS AS IS AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

- OpenVR XR Plugin and license: https://github.com/ValveSoftware/unity-xr-plugin
- SteamVR 2.8.0 release: https://github.com/ValveSoftware/steamvr_unity_plugin/releases/tag/2.8.0
- Unity XR Plug-in Management and Input System are installed separately through Unity Package Manager and retain their respective Unity licenses.

The JSON bindings in `Resources/GestureManagerPro/SteamVR` are new Pro bindings using the documented
SteamVR binding format. They are not VRChat's shipped bindings. They do not
inherit a user's VRChat application bindings.
