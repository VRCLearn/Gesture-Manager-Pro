using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace VRCLearn.GestureManagerPro.Editor.Data
{
    // Owned by one Inspector. Textures are cached only for that Inspector's lifetime.
    internal sealed class ContributorAvatarCache : IDisposable
    {
        private sealed class Avatar
        {
            internal Texture2D Texture;
            internal UnityWebRequest Request;
            internal bool Waiting = true;
        }

        private const int ConcurrentRequests = 3;
        private readonly Dictionary<string, Avatar> _avatars = new();
        private readonly Action _repaint;
        private bool _active;
        private bool _updating;
        private bool _disposed;

        internal ContributorAvatarCache(Action repaint)
        {
            _repaint = repaint;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
        }

        internal Texture2D GetAvatar(string url)
        {
            if (_disposed || !ContributorCatalog.IsHttpsUrl(url, "avatars.githubusercontent.com")) return null;
            if (_avatars.TryGetValue(url, out var avatar)) return avatar.Texture;
            if (!_active) return null;
            _avatars.Add(url, new Avatar());
            StartUpdating();
            return null;
        }

        internal void SetActive(bool active)
        {
            if (_disposed || _active == active) return;
            _active = active;
            if (active)
            {
                foreach (var avatar in _avatars.Values)
                    if (!avatar.Texture && avatar.Request == null) avatar.Waiting = true;
                if (_avatars.Values.Any(avatar => avatar.Waiting)) StartUpdating();
                return;
            }

            StopUpdating();
            foreach (var avatar in _avatars.Values)
            {
                if (avatar.Request == null) continue;
                avatar.Request.Abort();
                avatar.Request.Dispose();
                avatar.Request = null;
                avatar.Waiting = true;
            }
        }

        private void StartUpdating()
        {
            if (_updating) return;
            _updating = true;
            EditorApplication.update += Tick;
        }

        private void StopUpdating()
        {
            EditorApplication.update -= Tick;
            _updating = false;
        }

        private void Tick()
        {
            if (!_active || _disposed) return;
            var repaint = false;
            foreach (var avatar in _avatars.Values)
            {
                var request = avatar.Request;
                if (request == null || !request.isDone) continue;
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var texture = DownloadHandlerTexture.GetContent(request);
                        if (texture)
                        {
                            texture.name = "GestureManagerPro Contributor Avatar";
                            texture.hideFlags = HideFlags.HideAndDontSave;
                            avatar.Texture = texture;
                        }
                    }
                }
                catch (Exception) { /* A failed image must not hide the local contributor name. */ }
                finally
                {
                    request.Dispose();
                    avatar.Request = null;
                    repaint = true;
                }
            }

            var available = ConcurrentRequests - _avatars.Values.Count(avatar => avatar.Request != null);
            foreach (var pair in _avatars)
            {
                if (available <= 0) break;
                var avatar = pair.Value;
                if (!avatar.Waiting) continue;
                avatar.Waiting = false;
                try
                {
                    var uri = new Uri(pair.Key);
                    avatar.Request = UnityWebRequestTexture.GetTexture(uri.GetLeftPart(UriPartial.Path) + "?s=96", true);
                    avatar.Request.timeout = 10;
                    avatar.Request.redirectLimit = 0;
                    avatar.Request.SendWebRequest();
                    available--;
                }
                catch (Exception)
                {
                    avatar.Request?.Dispose();
                    avatar.Request = null;
                }
            }
            if (!_avatars.Values.Any(avatar => avatar.Waiting || avatar.Request != null)) StopUpdating();
            if (repaint) _repaint?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            SetActive(false);
            _disposed = true;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;
            foreach (var avatar in _avatars.Values)
                if (avatar.Texture) Object.DestroyImmediate(avatar.Texture);
            _avatars.Clear();
        }
    }
}
