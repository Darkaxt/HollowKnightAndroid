using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace DualSouls.DualScreen
{
    /// <summary>
    /// Owns the pure lifecycle state around a direct-display presentation.
    /// Unity display activation and rendering remain behind injected callbacks.
    /// </summary>
    public sealed class DirectDisplayHost : IDisposable
    {
        readonly Action _requestActivation;
        readonly Action<bool> _setPresentationVisible;
        readonly Action<bool> _setTouchFenceActive;
        readonly Action _releasePresentation;

        IDirectDisplayContent _content;
        bool _displayPresent;
        bool _presentationReady;
        bool _paused;
        bool _enabled = true;
        bool _active;
        bool _disposed;
        bool _activationRequested;
        float _panelWidth;
        float _panelHeight;

        public DirectDisplayHost(
            Action requestActivation,
            Action<bool> setPresentationVisible,
            Action<bool> setTouchFenceActive,
            Action releasePresentation)
        {
            _requestActivation = requestActivation ?? throw new ArgumentNullException(nameof(requestActivation));
            _setPresentationVisible = setPresentationVisible ?? throw new ArgumentNullException(nameof(setPresentationVisible));
            _setTouchFenceActive = setTouchFenceActive ?? throw new ArgumentNullException(nameof(setTouchFenceActive));
            _releasePresentation = releasePresentation ?? throw new ArgumentNullException(nameof(releasePresentation));
        }

        public bool DisplayPresent => _displayPresent;
        public bool PresentationReady => _presentationReady;
        public bool IsPaused => _paused;
        public bool IsEnabled => _enabled;
        public bool IsActive => _active;
        public bool IsFallback => !_active;
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Records physical display presence. A transition from absent to
        /// present starts one activation attempt for that presence generation.
        /// Repeated observations of the same generation are suppressed.
        /// </summary>
        public void SetDisplayPresent(bool present)
        {
            if (_disposed) return;

            if (!present)
            {
                _displayPresent = false;
                _presentationReady = false;
                _activationRequested = false;
                ApplyActiveState();
                return;
            }

            if (!_displayPresent)
            {
                // Readiness and activation ownership never carry across a
                // detach/reattach boundary.
                _displayPresent = true;
                _presentationReady = false;
                _activationRequested = false;
            }

            ApplyActiveState();
            RequestActivationIfNeeded();
        }

        /// <summary>
        /// Publishes presentation readiness and its measured geometry. A stale
        /// readiness result received after display loss is ignored.
        /// </summary>
        public void SetPresentationReady(bool ready, float width = 0f, float height = 0f)
        {
            if (_disposed) return;
            if (ready && !_displayPresent) return;

            if (ready)
            {
                bool geometryChanged = !_presentationReady ||
                    width != _panelWidth || height != _panelHeight;
                if (geometryChanged && _content != null)
                    _content.OnPanelGeometry(width, height);

                // Geometry and readiness commit only after the content accepts
                // the publication. A throw leaves the previous state intact.
                _panelWidth = width;
                _panelHeight = height;
                _presentationReady = true;
            }
            else
            {
                _presentationReady = false;
                _activationRequested = false;
            }

            ApplyActiveState();
        }

        public void SetPaused(bool paused)
        {
            if (_disposed) return;
            _paused = paused;
            ApplyActiveState();
        }

        /// <summary>
        /// Enables or disables product ownership of the secondary display.
        /// Disabling is the same ordered transition used for pause and loss:
        /// touch is released first, then content restores, then presentation
        /// visibility is removed. Presence/readiness remain available so an
        /// enabled toggle can resume without rebuilding the display.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (_disposed) return;
            if (_enabled != enabled) _enabled = enabled;
            // Reconcile even when the desired value is unchanged. A prior
            // activation/deactivation callback may have thrown after the
            // desired flag was committed, leaving actual state retryable.
            ApplyActiveState();
            RequestActivationIfNeeded();
        }

        void RequestActivationIfNeeded()
        {
            if (_disposed || !_enabled || !_displayPresent ||
                _presentationReady || _activationRequested)
                return;

            _activationRequested = true;
            try
            {
                _requestActivation();
            }
            catch
            {
                // Presence was observed, but this generation has not acquired
                // an activation request and the same observation must retry.
                _activationRequested = false;
                throw;
            }
        }

        /// <summary>
        /// Attaches the one content owner released by this host. Callers should
        /// normally attach after the presentation is measured and before they
        /// publish readiness; late attachment is safely re-ordered.
        /// </summary>
        public void AttachContent(IDirectDisplayContent content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (_disposed) throw new ObjectDisposedException(nameof(DirectDisplayHost));
            if (ReferenceEquals(_content, content)) return;
            if (_content != null)
                throw new InvalidOperationException("Direct-display content is already attached.");

            bool wasActive = _active;
            if (wasActive) Deactivate();

            Exception failure = null;
            try
            {
                if (_presentationReady)
                    content.OnPanelGeometry(_panelWidth, _panelHeight);
            }
            catch (Exception e)
            {
                failure = e;
            }

            if (failure == null)
            {
                _content = content;
                try
                {
                    ApplyActiveState();
                }
                catch (Exception e)
                {
                    // The attachment did not complete. Activation already ran
                    // its own transport rollback; ownership returns to caller.
                    _content = null;
                    failure = e;
                }
            }

            if (failure == null) return;

            var failures = new List<Exception> { failure };
            if (wasActive && !_active)
                TryStep(ApplyActiveState, failures);
            ThrowFailures(failures);
        }

        void ApplyActiveState()
        {
            bool shouldBeActive = _enabled && _displayPresent &&
                _presentationReady && !_paused;
            if (shouldBeActive == _active) return;
            if (shouldBeActive) Activate();
            else Deactivate();
        }

        void Activate()
        {
            Exception activationFailure = null;
            try
            {
                _setPresentationVisible(true);
                if (_content != null) _content.SetTransportActive(true);
                _setTouchFenceActive(true);
            }
            catch (Exception e)
            {
                activationFailure = e;
            }

            if (activationFailure == null)
            {
                _active = true;
                return;
            }

            // Activation is transactional. Roll back every layer in the same
            // safe order as ordinary deactivation, even if rollback also fails.
            var failures = new List<Exception> { activationFailure };
            TryStep(() => _setTouchFenceActive(false), failures);
            if (_content != null)
                TryStep(() => _content.SetTransportActive(false), failures);
            TryStep(() => _setPresentationVisible(false), failures);
            _active = false;
            ThrowFailures(failures);
        }

        void Deactivate()
        {
            var failures = new List<Exception>();
            TryStep(() => _setTouchFenceActive(false), failures);
            if (_content != null)
                TryStep(() => _content.SetTransportActive(false), failures);
            TryStep(() => _setPresentationVisible(false), failures);

            if (failures.Count == 0)
            {
                _active = false;
                return;
            }

            // Desired state was recorded by the setter, but the actual state is
            // not cleanly inactive. Keep it retryable on the same publication.
            _active = true;
            ThrowFailures(failures);
        }

        static void TryStep(Action step, List<Exception> failures)
        {
            try { step(); }
            catch (Exception e) { failures.Add(e); }
        }

        static void ThrowFailures(List<Exception> failures)
        {
            if (failures.Count == 0) return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(failures);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
            _displayPresent = false;
            _presentationReady = false;
            _activationRequested = false;

            // Teardown always attempts the complete final sequence and reports
            // every failure after the last owned resource has been released.
            var content = _content;
            var failures = new List<Exception>();
            TryStep(() => _setTouchFenceActive(false), failures);
            if (content != null)
                TryStep(() => content.SetTransportActive(false), failures);
            TryStep(() => _setPresentationVisible(false), failures);
            if (content != null)
                TryStep(content.Dispose, failures);
            _content = null;
            TryStep(_releasePresentation, failures);
            ThrowFailures(failures);
        }
    }
}
