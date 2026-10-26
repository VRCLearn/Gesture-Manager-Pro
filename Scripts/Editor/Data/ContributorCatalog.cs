using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Editor.Data
{
    [Serializable]
    internal sealed class ContributorEntry
    {
        public string login;
        public string avatarUrl;
        public string profileUrl;
    }

    [Serializable]
    internal sealed class ContributorCatalog
    {
        internal const string Repository = "VRCLearn/Gesture-Manager-Pro";
        internal const string ProjectUrl = "https://github.com/" + Repository;

        public string repository;
        public ContributorEntry[] contributors;
        internal ContributorEntry[] Entries => contributors;

        internal static ContributorCatalog Load()
        {
            var resource = Resources.Load<TextAsset>("GestureManagerPro/Contributors");
            return resource ? Parse(resource.text) : null;
        }

        internal static ContributorCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            ContributorCatalog catalog;
            try { catalog = JsonUtility.FromJson<ContributorCatalog>(json); }
            catch (ArgumentException) { return null; }
            if (catalog == null || catalog.repository != Repository || catalog.contributors == null) return null;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in catalog.contributors)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.login) || entry.login.Length > 100 ||
                    entry.login != entry.login.Trim() || !names.Add(entry.login) ||
                    !IsHttpsUrl(entry.profileUrl, "github.com") || !IsHttpsUrl(entry.avatarUrl, "avatars.githubusercontent.com")) return null;
                foreach (var character in entry.login)
                    if (char.IsControl(character)) return null;
            }
            return catalog;
        }

        internal static bool IsHttpsUrl(string value, string host) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    }
}
