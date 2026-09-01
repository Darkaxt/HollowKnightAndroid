using System;
using System.Collections.Generic;

namespace DualSouls.DualScreen
{
    public readonly struct DirectDisplayContact
    {
        public DirectDisplayContact(int id, float x, float y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        public int Id { get; }
        public float X { get; }
        public float Y { get; }
    }

    /// <summary>
    /// Converts display-attributed contact snapshots into the edge and
    /// multi-pointer contract used by the Dual Souls companion. Coordinates
    /// are normalized with a top-left origin.
    /// </summary>
    public sealed class DirectDisplayGestureTracker
    {
        const float CleanTapSeconds = 0.35f;
        const float CleanTapTravel = 0.03f;

        bool _gestureActive;
        float _downTime;
        float _downX;
        float _downY;
        float _lastX;
        float _lastY;
        int _maxPointers;

        public int TouchCount { get; private set; }
        public float TouchX { get; private set; } = -1f;
        public float TouchY { get; private set; } = -1f;
        public float T0X { get; private set; } = -1f;
        public float T0Y { get; private set; } = -1f;
        public float T1X { get; private set; } = -1f;
        public float T1Y { get; private set; } = -1f;
        public int TapSequence { get; private set; }
        public int CleanTapSequence { get; private set; }
        public float CleanTapX { get; private set; } = -1f;
        public float CleanTapY { get; private set; } = -1f;

        /// <summary>
        /// Cancels live contact ownership without publishing a clean tap.
        /// Sequence counters remain monotonic across transport generations.
        /// </summary>
        public void Cancel()
        {
            _gestureActive = false;
            _maxPointers = 0;
            TouchCount = 0;
            TouchX = TouchY = -1f;
            T0X = T0Y = -1f;
            T1X = T1Y = -1f;
        }

        public void Update(
            IReadOnlyList<DirectDisplayContact> contacts,
            float now,
            bool canceled = false)
        {
            if (contacts == null) throw new ArgumentNullException(nameof(contacts));

            int count = contacts.Count;
            if (!_gestureActive && count > 0)
            {
                var first = contacts[0];
                _gestureActive = true;
                _downTime = now;
                _downX = _lastX = first.X;
                _downY = _lastY = first.Y;
                _maxPointers = count;
                TapSequence++;
            }

            TouchCount = count;
            if (count > 0)
            {
                var first = contacts[0];
                TouchX = T0X = _lastX = first.X;
                TouchY = T0Y = _lastY = first.Y;
                if (count > 1)
                {
                    T1X = contacts[1].X;
                    T1Y = contacts[1].Y;
                }
                if (count > _maxPointers) _maxPointers = count;
            }

            if (!_gestureActive || count != 0) return;

            if (!canceled && _maxPointers == 1 &&
                now - _downTime < CleanTapSeconds &&
                Math.Abs(_lastX - _downX) < CleanTapTravel &&
                Math.Abs(_lastY - _downY) < CleanTapTravel)
            {
                CleanTapX = _lastX;
                CleanTapY = _lastY;
                CleanTapSequence++;
            }

            _gestureActive = false;
            _maxPointers = 0;
        }
    }
}
