#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DualSouls.Mods.HollowKnight
{
    internal sealed class HollowKnightLifebloodFlashPolicy : IDisposable
    {
        const string CameraPath = "_GameCameras/CameraParent/tk2dCamera";
        const int CameraProbeInterval = 30;

        readonly Dictionary<SpriteRenderer, HollowKnightFlashStateTracker> _trackers =
            new Dictionary<SpriteRenderer, HollowKnightFlashStateTracker>();
        readonly List<SpriteRenderer> _dead = new List<SpriteRenderer>();
        Transform _tk2dCamera;
        int _cameraProbeCountdown;
        bool _disposed;

        internal void Tick(HollowKnightFlashDecision decision)
        {
            if (_disposed) return;
            if (!decision.HasOwner)
            {
                Release();
                return;
            }

            PruneDestroyedRenderers();
            ResolveCameraBinding();
            if (_tk2dCamera == null) return;

            for (int i = 0; i < _tk2dCamera.childCount; i++)
            {
                var child = _tk2dCamera.GetChild(i);
                if (!child.name.StartsWith("Screen Flash")) continue;
                var renderer = child.GetComponent<SpriteRenderer>();
                if (renderer == null) continue;

                HollowKnightFlashSample live = Sample(renderer);
                HollowKnightFlashStateTracker tracker;
                if (!_trackers.TryGetValue(renderer, out tracker))
                {
                    tracker = new HollowKnightFlashStateTracker(live);
                    _trackers.Add(renderer, tracker);
                }
                HollowKnightFlashTransition transition = tracker.Apply(live, decision);
                ApplyTransition(renderer, transition);
            }
        }

        void ResolveCameraBinding()
        {
            if (_tk2dCamera != null && --_cameraProbeCountdown > 0) return;

            _cameraProbeCountdown = CameraProbeInterval;
            var cameraObject = GameObject.Find(CameraPath);
            Transform camera = cameraObject != null ? cameraObject.transform : null;
            if (camera != null || _tk2dCamera == null) RebindCamera(camera);
        }

        void RebindCamera(Transform camera)
        {
            if (ReferenceEquals(camera, _tk2dCamera)) return;
            ReleaseTrackedRenderers();
            _tk2dCamera = camera;
        }

        void PruneDestroyedRenderers()
        {
            _dead.Clear();
            foreach (var renderer in _trackers.Keys)
                if (renderer == null) _dead.Add(renderer);
            for (int i = 0; i < _dead.Count; i++)
                _trackers.Remove(_dead[i]);
            _dead.Clear();
        }

        internal void Release()
        {
            if (_disposed) return;
            ReleaseTrackedRenderers();
            _tk2dCamera = null;
            _cameraProbeCountdown = 0;
        }

        void ReleaseTrackedRenderers()
        {
            foreach (var pair in _trackers)
            {
                var renderer = pair.Key;
                var tracker = pair.Value;
                if (renderer == null) continue;
                HollowKnightFlashSample live = Sample(renderer);
                HollowKnightFlashTransition transition = tracker.Release(live);
                ApplyTransition(renderer, transition);
            }
            _trackers.Clear();
            _dead.Clear();
        }

        static HollowKnightFlashSample Sample(SpriteRenderer renderer)
        {
            Color color = renderer.color;
            return new HollowKnightFlashSample(
                renderer.enabled,
                new HollowKnightFlashRgba(color.r, color.g, color.b, color.a));
        }

        static void ApplyTransition(
            SpriteRenderer renderer,
            HollowKnightFlashTransition transition)
        {
            if (transition.WriteEnabled)
                renderer.enabled = transition.Sample.Enabled;
            if (transition.WriteColor)
                renderer.color = ToUnityColor(transition.Sample.Color);
        }

        static Color ToUnityColor(HollowKnightFlashRgba color)
        {
            return new Color(color.R, color.G, color.B, color.A);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Release();
            _disposed = true;
        }
    }
}
#endif
