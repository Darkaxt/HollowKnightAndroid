#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DualSouls.Mods.HollowKnight
{
    internal sealed class HollowKnightLifebloodFlashPolicy : IDisposable
    {
        internal const float DefaultSoftAlpha = 0.35f;

        struct FlashBaseline
        {
            public bool GameEnabled;
            public Color GameColor;
            public bool PolicyActive;
            public bool LastPolicyEnabled;
            public Color LastPolicyColor;
        }

        readonly Dictionary<SpriteRenderer, FlashBaseline> _baselines =
            new Dictionary<SpriteRenderer, FlashBaseline>();
        readonly List<SpriteRenderer> _dead = new List<SpriteRenderer>();
        Transform _tk2dCamera;
        bool _disposed;

        internal void Tick(HollowKnightFlashMode? desiredMode, float softAlpha)
        {
            if (_disposed) return;
            if (!desiredMode.HasValue)
            {
                Restore();
                return;
            }

            _dead.Clear();
            foreach (var renderer in _baselines.Keys)
                if (renderer == null) _dead.Add(renderer);
            for (int i = 0; i < _dead.Count; i++)
                _baselines.Remove(_dead[i]);
            _dead.Clear();

            if (_tk2dCamera == null)
            {
                var camera = GameObject.Find("_GameCameras/CameraParent/tk2dCamera");
                if (camera != null) _tk2dCamera = camera.transform;
            }
            if (_tk2dCamera == null) return;

            for (int i = 0; i < _tk2dCamera.childCount; i++)
            {
                var child = _tk2dCamera.GetChild(i);
                if (!child.name.StartsWith("Screen Flash")) continue;
                var renderer = child.GetComponent<SpriteRenderer>();
                if (renderer == null) continue;

                FlashBaseline baseline;
                if (!_baselines.TryGetValue(renderer, out baseline))
                {
                    baseline = new FlashBaseline();
                    _baselines.Add(renderer, baseline);
                }
                Color livePolicyColor = renderer.color;
                ReconcileLifebloodFlashState(renderer, ref baseline);

                switch (desiredMode.Value)
                {
                    case HollowKnightFlashMode.Soft:
                        Color softened = baseline.GameColor;
                        if (baseline.PolicyActive && livePolicyColor.a < softened.a)
                            softened.a = livePolicyColor.a;
                        if (softAlpha <= 0f) softened.a = 0f;
                        else if (softened.a > softAlpha) softened.a = softAlpha;
                        renderer.enabled = baseline.GameEnabled;
                        renderer.color = softened;
                        baseline.LastPolicyEnabled = renderer.enabled;
                        baseline.LastPolicyColor = renderer.color;
                        baseline.PolicyActive = true;
                        break;
                    case HollowKnightFlashMode.Off:
                        renderer.enabled = false;
                        baseline.LastPolicyEnabled = renderer.enabled;
                        baseline.LastPolicyColor = renderer.color;
                        baseline.PolicyActive = true;
                        break;
                    case HollowKnightFlashMode.Vanilla:
                    default:
                        if (baseline.PolicyActive)
                        {
                            renderer.enabled = baseline.GameEnabled;
                            renderer.color = baseline.GameColor;
                            baseline.PolicyActive = false;
                        }
                        break;
                }
                _baselines[renderer] = baseline;
            }
        }

        static void ReconcileLifebloodFlashState(
            SpriteRenderer renderer,
            ref FlashBaseline baseline)
        {
            bool liveEnabled = renderer.enabled;
            Color liveColor = renderer.color;
            if (!baseline.PolicyActive)
            {
                baseline.GameEnabled = liveEnabled;
                baseline.GameColor = liveColor;
            }
            else
            {
                if (liveEnabled != baseline.LastPolicyEnabled)
                    baseline.GameEnabled = liveEnabled;
                if (liveColor != baseline.LastPolicyColor)
                    baseline.GameColor = liveColor;
            }
        }

        internal void Restore()
        {
            if (_disposed) return;
            foreach (var pair in _baselines)
            {
                var renderer = pair.Key;
                var baseline = pair.Value;
                if (renderer == null) continue;
                ReconcileLifebloodFlashState(renderer, ref baseline);
                renderer.enabled = baseline.GameEnabled;
                renderer.color = baseline.GameColor;
            }
            _baselines.Clear();
            _dead.Clear();
            _tk2dCamera = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Restore();
            _disposed = true;
        }
    }
}
#endif
