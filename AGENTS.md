# Development conventions

Gesture Manager Pro is a standalone Unity/VPM package derived from Gesture Manager 3.9.9. It embeds its simulator and does not depend on the original package.

- Write repository documentation and public GitHub collaboration in English.
- Preserve upstream MIT notices and the separate Meta SDK license/notices.
- Target Unity 2022.3 and VRChat Avatars SDK 3.10.x until compatibility has been validated.
- Preserve actual VRChat behavior. Do not invent substitute animations or behavior to conceal missing client functionality; document unsupported behavior explicitly.
- Use 4 spaces for new C# code. Follow existing source style when editing inherited code.
- Keep parameter identifiers, user expression names, asset identifiers, and persistence keys separate from localized display text.
- Add Unity `.meta` files for new assets. Use the Pro namespace and resource prefix so the original package can remain installed.
- Keep simulator, localization, audio, and input code in separate assemblies. Add optional VR input adapters through the motion input interface.
- Run relevant Unity integration checks before committing, and report visual/hardware checks that were not performed.
- Do not rewrite upstream Git history. Check for duplicate commits and remote state before committing or pushing.
