using System;
using System.Collections.Generic;
using Windows.System;

namespace XFiles.Navigation
{
    public interface IInputHandler
    {
        int Priority { get; }
        bool IsActive { get; }
        bool OnDPad(VirtualKey key, bool isRepeat);
        bool OnButton(VirtualKey key);
    }

    public sealed class InputRouter
    {
        private readonly List<IInputHandler> _entries = new List<IInputHandler>();
        private bool _sorted;

        public void Add(IInputHandler handler)
        {
            _entries.Add(handler);
            _sorted = false;
        }

        public void Remove(IInputHandler handler)
        {
            _entries.Remove(handler);
        }

        public bool RouteDPad(VirtualKey key, bool isRepeat)
        {
            EnsureSorted();
            for (int i = 0; i < _entries.Count; i++)
            {
                var h = _entries[i];
                if (h.IsActive)
                    return h.OnDPad(key, isRepeat);
            }
            return false;
        }

        public bool RouteButton(VirtualKey key)
        {
            EnsureSorted();
            for (int i = 0; i < _entries.Count; i++)
            {
                var h = _entries[i];
                if (h.IsActive)
                    return h.OnButton(key);
            }
            return false;
        }

        private void EnsureSorted()
        {
            if (!_sorted)
            {
                _entries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                _sorted = true;
            }
        }
    }

    public sealed class OverlayHandler : IInputHandler
    {
        private readonly Func<bool> _isActive;
        private readonly Func<VirtualKey, bool, bool> _onDPad;
        private readonly Func<VirtualKey, bool> _onButton;

        public OverlayHandler(int priority, Func<bool> isActive, Func<VirtualKey, bool, bool> onDPad, Func<VirtualKey, bool> onButton)
        {
            Priority = priority;
            _isActive = isActive;
            _onDPad = onDPad;
            _onButton = onButton;
        }

        public int Priority { get; }
        public bool IsActive => _isActive();
        public bool OnDPad(VirtualKey key, bool isRepeat) => _onDPad(key, isRepeat);
        public bool OnButton(VirtualKey key) => _onButton(key);
    }
}
