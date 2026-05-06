using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace RaceCorProDrive.Services.Moza
{
    /// <summary>
    /// In-memory ring buffer for Moza serial log lines. The
    /// MozaSerialManager raises every event through logInfo / logWarn
    /// callbacks; before this they were dumped to Debug.WriteLine and
    /// invisible to the user. The Hardware settings page consumes this
    /// to render a live console so device-detection / FFB-write / port
    /// failures are diagnosable without attaching a debugger.
    ///
    /// Capped at <see cref="MaxEntries"/>; oldest entries fall off as
    /// new ones arrive. Thread-safe — the poll thread writes from a
    /// worker, the UI reads / subscribes from the dispatcher thread.
    /// </summary>
    public sealed class MozaLogBuffer
    {
        public static MozaLogBuffer Shared { get; } = new();

        public readonly record struct Entry(DateTime At, LogLevel Level, string Message);

        public enum LogLevel { Info, Warn }

        public const int MaxEntries = 500;

        private readonly object _gate = new();
        private readonly LinkedList<Entry> _entries = new();

        /// <summary>
        /// Fired on every new entry. Listeners must marshal to the UI
        /// thread themselves — the buffer is unaware of dispatchers.
        /// </summary>
        public event EventHandler<Entry>? EntryAdded;

        /// <summary>
        /// Fired when <see cref="Clear"/> is called so consumers can
        /// rebuild from an empty state.
        /// </summary>
        public event EventHandler? Cleared;

        /// <summary>
        /// Snapshot of all current entries, oldest first. Cheap copy —
        /// the buffer is bounded.
        /// </summary>
        public IReadOnlyList<Entry> Snapshot()
        {
            lock (_gate)
            {
                var copy = new Entry[_entries.Count];
                int i = 0;
                foreach (var e in _entries) copy[i++] = e;
                return copy;
            }
        }

        public void Info(string message) => Add(LogLevel.Info, message);
        public void Warn(string message) => Add(LogLevel.Warn, message);

        public void Clear()
        {
            lock (_gate) _entries.Clear();
            Cleared?.Invoke(this, EventArgs.Empty);
        }

        private void Add(LogLevel level, string message)
        {
            var entry = new Entry(DateTime.Now, level, message ?? "");
            lock (_gate)
            {
                _entries.AddLast(entry);
                while (_entries.Count > MaxEntries) _entries.RemoveFirst();
            }
            EntryAdded?.Invoke(this, entry);
        }
    }
}
