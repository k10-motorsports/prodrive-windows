using System;
using System.ComponentModel;
using System.Diagnostics;

namespace RaceCorProDrive.Services.Moza
{
    /// <summary>
    /// Singleton wrapper around <see cref="MozaSerialManager"/>. Mirrors
    /// the OverlayLauncher.Shared pattern so any page can listen for
    /// PropertyChanged on a connection state change without re-spinning
    /// the background poll thread.
    ///
    /// The plugin used to own this code and expose it via HTTP on
    /// :8889; in the host we read straight off
    /// <c>MozaService.Shared.Manager.Wheelbase?.WheelbaseSettings</c>
    /// (and friends) — no loopback round-trip needed in-process.
    /// Pit House conflicts surface via <see cref="PitHouseWarning"/>.
    /// </summary>
    public sealed class MozaService : INotifyPropertyChanged
    {
        public static MozaService Shared { get; } = new();

        public MozaSerialManager Manager { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private MozaService()
        {
            Manager = new MozaSerialManager(
                logInfo: msg => Debug.WriteLine($"[Moza] {msg}"),
                logWarn: msg => Debug.WriteLine($"[Moza] WARN {msg}"));
        }

        /// <summary>
        /// True iff at least one Moza device is connected and responsive.
        /// </summary>
        public bool IsConnected => Manager.IsConnected;

        /// <summary>
        /// Pit House conflict warning, or empty string. The plugin's
        /// poll loop sets this when it detects MozaService /
        /// MozaPitHouse / Nextmotion holding ports.
        /// </summary>
        public string PitHouseWarning => Manager.PitHouseWarning;

        /// <summary>
        /// Spin up the background discovery + polling thread. Idempotent.
        /// Pages can call this on Loaded; the singleton ensures only one
        /// poll thread runs for the lifetime of the host process.
        /// </summary>
        public void Start() => Manager.Start();

        /// <summary>
        /// Force an immediate re-scan of COM ports.
        /// </summary>
        public void Reconnect() => Manager.ForceRediscovery();

        /// <summary>
        /// Force an immediate re-poll of every connected device.
        /// </summary>
        public void Refresh() => Manager.ForceRefresh();

        /// <summary>
        /// Notify subscribers of a change. Called by the Hardware page
        /// when it wants to drive a UI rebuild after a write completes.
        /// (The poll loop itself doesn't wire into here — UI updates
        /// happen on a dispatcher timer that re-reads typed state.)
        /// </summary>
        internal void RaisePropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
