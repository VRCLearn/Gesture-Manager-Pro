using System;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Localization;
using UnityEditor;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Editor.Data
{
    internal sealed class ContributorsPanel : IDisposable
    {
        private readonly ContributorAvatarCache _avatars;
        private ContributorCatalog _catalog;
        private bool _loaded;
        private bool _expanded;
        private Vector2 _scroll;
        private GUIStyle _linkStyle;

        internal ContributorsPanel(Action repaint) => _avatars = new ContributorAvatarCache(repaint);

        internal void SetVisible(bool visible)
        {
            if (!visible) _avatars.SetActive(false);
        }

        internal void Draw()
        {
            using (new GUILayout.VerticalScope(GestureManagerStyles.EmoteError))
            {
                var expanded = GmgLayoutHelper.FoldoutSection(EditorLocalization.Tr("About and Contributors"), ref _expanded);
                _avatars.SetActive(expanded);
                if (!expanded) return;

                GUILayout.Label(GestureManager.Version, EditorStyles.boldLabel);
                Link(EditorLocalization.Tr("Project on GitHub"), ContributorCatalog.ProjectUrl);
                Link(EditorLocalization.Tr("Based on Gesture Manager by BlackStartx."), "https://github.com/BlackStartx/VRC-Gesture-Manager");
                GUILayout.Space(6);
                GUILayout.Label(EditorLocalization.Tr("Contributors"), GestureManagerStyles.ToolSubHeader);
                GUILayout.Label(EditorLocalization.Tr("Avatars load when this section is expanded."), GestureManagerStyles.SettingsText);

                if (!_loaded)
                {
                    _catalog = ContributorCatalog.Load();
                    _loaded = true;
                }
                if (_catalog == null || _catalog.Entries.Length == 0)
                    GUILayout.Label(EditorLocalization.Tr("Contributor information is unavailable."), GestureManagerStyles.SettingsText);
                else DrawContributors();

                Link(EditorLocalization.Tr("View all contributors"), ContributorCatalog.ProjectUrl + "/graphs/contributors");
            }
        }

        private void DrawContributors()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(240, _catalog.Entries.Length * 40 + 8)));
            foreach (var contributor in _catalog.Entries)
            {
                using (new GUILayout.HorizontalScope(GUILayout.Height(36)))
                {
                    var rect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
                    var avatar = _avatars.GetAvatar(contributor.avatarUrl);
                    if (avatar) GUI.DrawTexture(rect, avatar, ScaleMode.ScaleToFit, true);
                    else
                    {
                        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.15f));
                        GUI.Label(rect, contributor.login.Substring(0, 1).ToUpperInvariant(), EditorStyles.centeredGreyMiniLabel);
                    }
                    GUILayout.Space(6);
                    // GitHub identities are user content, never localization keys or rich text.
                    Link(contributor.login, contributor.profileUrl);
                }
            }
            GUILayout.EndScrollView();
        }

        private void Link(string text, string url)
        {
            _linkStyle ??= new GUIStyle(EditorStyles.linkLabel) { richText = false, wordWrap = true };
            if (GUILayout.Button(new GUIContent(text, url), _linkStyle, GUILayout.MinHeight(24), GUILayout.MinWidth(0))) Application.OpenURL(url);
        }

        public void Dispose() => _avatars.Dispose();
    }
}
