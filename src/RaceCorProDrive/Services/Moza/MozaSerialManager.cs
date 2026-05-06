using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RaceCorProDrive.Services.Moza
{
    /// <summary>
    /// Manages discovery, connection, polling, and command dispatch to all connected
    /// Moza racing hardware over CDC ACM serial ports. Completely bypasses Pit House.
    ///
    /// Lifecycle:
    ///   1. Start() — begins discovery scan and polling on a background thread
    ///   2. Poll loop — every 2 seconds, reads registered settings from all connected devices
    ///   3. WriteSetting() — queues a write command for the next poll cycle
    ///   4. Stop() — shuts down background threads and closes all serial ports
    ///
    /// Thread safety: all public methods are safe to call from any thread.
    /// Serial port access is synchronized per-device via SemaphoreSlim.
    /// </summary>
    public class MozaSerialManager : IDisposable
    {
        // ── Configuration ─────────────────────────────────────────────
        private const int BaudRate = 115200;
        private const int DataBits = 8;
        private const StopBits SerialStopBits = StopBits.One;
        private const Parity SerialParity = Parity.None;
        private const int ReadTimeoutMs = 500;
        private const int WriteTimeoutMs = 500;
        private const int PollIntervalMs = 2000;
        private const int DiscoveryIntervalMs = 10000;
        private const int ReadBufferSize = 256;

        // ── State ─────────────────────────────────────────────────────
        private readonly ConcurrentDictionary<string, MozaDevice> _devices = new ConcurrentDictionary<string, MozaDevice>();
        private readonly ConcurrentDictionary<string, SerialPort> _ports = new ConcurrentDictionary<string, SerialPort>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _portLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        private readonly ConcurrentQueue<WriteCommand> _writeQueue = new ConcurrentQueue<WriteCommand>();

        private Thread _pollThread;
        private volatile bool _running;
        private volatile bool _disposed = false;
        private DateTime _lastDiscovery = DateTime.MinValue;

        // ── Logging callback (injected by MozaService) ────────────────
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarn;

        /// <summary>True if at least one Moza device is connected and responsive.</summary>
        public bool IsConnected => _devices.Values.Any(d => d.IsConnected);

        /// <summary>Number of currently connected devices.</summary>
        public int DeviceCount => _devices.Values.Count(d => d.IsConnected);

        /// <summary>All discovered devices (connected and disconnected).</summary>
        public IReadOnlyList<MozaDevice> Devices => _devices.Values.ToList().AsReadOnly();

        /// <summary>First connected wheelbase, or null.</summary>
        public MozaDevice? Wheelbase => _devices.Values.FirstOrDefault(d =>
            d.DeviceType == MozaDeviceRegistry.MozaDeviceType.Wheelbase && d.IsConnected);

        /// <summary>First connected pedals, or null.</summary>
        public MozaDevice? Pedals => _devices.Values.FirstOrDefault(d =>
            d.DeviceType == MozaDeviceRegistry.MozaDeviceType.Pedals && d.IsConnected);

        /// <summary>First connected handbrake, or null.</summary>
        public MozaDevice? Handbrake => _devices.Values.FirstOrDefault(d =>
            d.DeviceType == MozaDeviceRegistry.MozaDeviceType.Handbrake && d.IsConnected);

        /// <summary>First connected shifter, or null.</summary>
        public MozaDevice? Shifter => _devices.Values.FirstOrDefault(d =>
            d.DeviceType == MozaDeviceRegistry.MozaDeviceType.Shifter && d.IsConnected);

        /// <summary>First connected dashboard, or null.</summary>
        public MozaDevice? Dashboard => _devices.Values.FirstOrDefault(d =>
            d.DeviceType == MozaDeviceRegistry.MozaDeviceType.Dashboard && d.IsConnected);

        /// <summary>First connected steering wheel, or null.</summary>
        public MozaDevice? SteeringWheel => _devices.Values.FirstOrDefault(d =>
            d.DeviceType == MozaDeviceRegistry.MozaDeviceType.SteeringWheel && d.IsConnected);

        /// <summary>Warning message if Pit House is detected running (serial port conflict).</summary>
        public string PitHouseWarning { get; private set; } = "";

        // ═══════════════════════════════════════════════════════════════
        //  CONSTRUCTION & LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        public MozaSerialManager(Action<string>? logInfo = null, Action<string>? logWarn = null)
        {
            _logInfo = logInfo ?? (_ => { });
            _logWarn = logWarn ?? (_ => { });
        }

        /// <summary>
        /// Starts the background discovery and polling thread.
        /// Safe to call multiple times (no-op if already running).
        /// </summary>
        public void Start()
        {
            if (_running) return;
            _running = true;

            _pollThread = new Thread(PollLoop)
            {
                Name = "MozaSerialPoll",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _pollThread.Start();

            _logInfo("[MozaSerial] Background polling started");
        }

        /// <summary>
        /// Stops the background thread and closes all serial ports.
        /// </summary>
        public void Stop()
        {
            _running = false;
            _pollThread?.Join(3000);
            CloseAllPorts();
            _logInfo("[MozaSerial] Stopped");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _running = false;
            _pollThread?.Join(3000);
            CloseAllPorts();

            foreach (var sem in _portLocks.Values)
            {
                try { sem.Dispose(); } catch { }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PUBLIC API — WRITE SETTINGS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Queues a single-byte write command to a device for the next poll cycle.
        /// </summary>
        public void WriteSetting(byte deviceId, byte commandId, byte value)
        {
            _writeQueue.Enqueue(new WriteCommand
            {
                DeviceId = deviceId,
                Packet = MozaPacketBuilder.BuildWritePacket(deviceId, commandId, value)
            });
        }

        /// <summary>
        /// Queues a 16-bit write command to a device for the next poll cycle.
        /// </summary>
        public void WriteSetting16(byte deviceId, byte commandId, ushort value)
        {
            _writeQueue.Enqueue(new WriteCommand
            {
                DeviceId = deviceId,
                Packet = MozaPacketBuilder.BuildWritePacket(deviceId, commandId, value)
            });
        }

        /// <summary>
        /// Force an immediate re-discovery of serial ports.
        /// </summary>
        public void ForceRediscovery()
        {
            _lastDiscovery = DateTime.MinValue;
        }

        /// <summary>
        /// Force an immediate re-poll of all device settings.
        /// </summary>
        public void ForceRefresh()
        {
            foreach (var device in _devices.Values.Where(d => d.IsConnected))
                device.LastPollTime = DateTime.MinValue;
        }

        // ═══════════════════════════════════════════════════════════════
        //  MAIN POLL LOOP (background thread)
        // ═══════════════════════════════════════════════════════════════

        private void PollLoop()
        {
            while (_running)
            {
                try
                {
                    // Periodic discovery
                    if ((DateTime.UtcNow - _lastDiscovery).TotalMilliseconds >= DiscoveryIntervalMs)
                    {
                        DiscoverDevices();
                        _lastDiscovery = DateTime.UtcNow;
                    }

                    // Process queued writes
                    ProcessWriteQueue();

                    // Poll all connected devices
                    foreach (var device in _devices.Values.Where(d => d.IsConnected))
                    {
                        if ((DateTime.UtcNow - device.LastPollTime).TotalMilliseconds >= PollIntervalMs)
                        {
                            PollDevice(device);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logWarn($"[MozaSerial] Poll loop error: {ex.Message}");
                }

                Thread.Sleep(200); // Check interval — much faster than poll interval
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  DEVICE DISCOVERY
        // ═══════════════════════════════════════════════════════════════

        private void DiscoverDevices()
        {
            try
            {
                CheckForPitHouse();

                var portNames = SerialPort.GetPortNames();
                if (portNames == null || portNames.Length == 0) return;

                // Prune disconnected devices so they can be re-discovered.
                // Without this, a device that hit MaxFailures stays in _devices
                // forever and the ContainsKey check below blocks rediscovery —
                // even after the hardware is power-cycled or replugged.
                var portNameSet = new HashSet<string>(portNames, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _devices.ToArray())
                {
                    if (!kvp.Value.IsConnected)
                    {
                        // Port vanished from the system — remove immediately
                        if (!portNameSet.Contains(kvp.Key))
                        {
                            _devices.TryRemove(kvp.Key, out _);
                            _logInfo($"[MozaSerial] Pruned stale device entry for {kvp.Key} (port no longer present)");
                        }
                        // Port still exists but device disconnected — allow rediscovery
                        // by removing the entry so the loop below can re-classify and reopen
                        else
                        {
                            _devices.TryRemove(kvp.Key, out _);
                            _logInfo($"[MozaSerial] Clearing disconnected device on {kvp.Key} for rediscovery");
                        }
                    }
                }

                // Query WMI for USB serial device descriptions
                var portDescriptions = GetUsbSerialDescriptions();

                // Drop the not-Moza probe cache if the port set has changed
                // (hot-plug, USB cable replug, etc.) so new devices get a probe.
                RefreshProbeCacheIfPortsChanged(portNames);

                int discoveryMatchedCount = 0;
                int discoveryProbeIdentifiedCount = 0;
                var discoveryMatchesByType = new Dictionary<string, int>();
                var discoveryUnmatchedMozaDescriptors = new List<string>();

                foreach (var portName in portNames)
                {
                    // Skip already-known connected ports
                    if (_devices.ContainsKey(portName)) continue;

                    // Try to match against Moza USB patterns
                    string description = "";
                    if (portDescriptions.TryGetValue(portName, out string desc))
                        description = desc;

                    var match = ClassifyDevice(description);
                    bool identifiedByProbe = false;
                    if (match == null)
                    {
                        // Description regex didn't match — fall back to the
                        // protocol probe. This is the path that catches Moza
                        // hardware presenting with generic COM names where the
                        // description contains no "Gudsen"/"MOZA" substring.
                        var probedType = OpenAndProbe(portName, useCache: true);
                        if (probedType == null) continue;
                        match = (probedType.Value, "");
                        identifiedByProbe = true;
                        discoveryProbeIdentifiedCount++;
                        _logInfo($"[MozaSerial] Probe identified {portName} as {probedType.Value} (USB description: \"{description}\")");
                    }

                    discoveryMatchedCount++;
                    string typeStr = match.Value.type.ToString();
                    if (!discoveryMatchesByType.ContainsKey(typeStr))
                        discoveryMatchesByType[typeStr] = 0;
                    discoveryMatchesByType[typeStr]++;

                    if (match.Value.type == MozaDeviceRegistry.MozaDeviceType.Unknown)
                    {
                        discoveryUnmatchedMozaDescriptors.Add($"{portName} ({description})");
                        _logWarn($"[MozaSerial] Detected Moza device on {portName} but could not determine specific type — USB descriptor: \"{description}\". Device will be tracked but not polled for settings.");
                    }

                    var device = new MozaDevice(
                        portName,
                        match.Value.type,
                        MozaDeviceRegistry.GetDeviceId(match.Value.type),
                        match.Value.subType)
                    {
                        UsbDescription = identifiedByProbe && string.IsNullOrEmpty(description)
                            ? "(identified via protocol probe — generic USB description)"
                            : description
                    };

                    if (TryOpenPort(device))
                    {
                        // Use TryAdd to avoid TOCTOU race condition
                        if (_devices.TryAdd(portName, device))
                        {
                            _logInfo($"[MozaSerial] Discovered: {device.DisplayName} ({description})");
                        }
                    }
                }

                // Emit diagnostic summary if any Moza-like devices were detected
                if (discoveryMatchedCount > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("[MozaSerial] DIAGNOSTIC:");
                    sb.AppendLine($"  Total ports: {portNames.Length}, Matched Moza devices: {discoveryMatchedCount} ({discoveryProbeIdentifiedCount} via protocol probe)");
                    foreach (var kvp in discoveryMatchesByType)
                    {
                        sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
                    }
                    if (discoveryUnmatchedMozaDescriptors.Count > 0)
                    {
                        sb.AppendLine("  Unmatched Moza-looking (fallback only):");
                        foreach (var desc in discoveryUnmatchedMozaDescriptors)
                        {
                            sb.AppendLine($"    {desc}");
                        }
                    }
                    if (!string.IsNullOrEmpty(PitHouseWarning))
                    {
                        sb.AppendLine($"  Pit House Warning: {PitHouseWarning}");
                    }
                    _logInfo(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                _logWarn($"[MozaSerial] Discovery error: {ex.Message}");
            }
        }

        /// <summary>
        /// Queries WMI Win32_PnPEntity for USB serial port descriptions.
        /// Returns a dictionary of COM port name → description string.
        /// </summary>
        private Dictionary<string, string> GetUsbSerialDescriptions()
        {
            var rich = GetUsbPortWmiInfo();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in rich)
            {
                result[kvp.Key] = kvp.Value.Description;
            }
            return result;
        }

        /// <summary>
        /// Per-port WMI data. Richer than GetUsbSerialDescriptions — includes
        /// the raw HardwareID plus extracted USB VID/PID, which the diagnostic
        /// surfaces so users whose devices come back with generic/empty
        /// descriptions (no "Gudsen"/"MOZA" substring) can still be identified
        /// by vendor/product ID.
        /// </summary>
        private class PortWmiInfo
        {
            public string Description { get; set; } = "";
            public string HardwareId { get; set; } = "";
            public string Vid { get; set; } = "";  // 4-char hex, no prefix (e.g. "346E")
            public string Pid { get; set; } = "";  // 4-char hex, no prefix
        }

        /// <summary>
        /// Like GetUsbSerialDescriptions but also pulls HardwareID/VID/PID. The
        /// discovery path (ClassifyDevice) still uses the plain description
        /// dictionary to keep the string-match classifier untouched; only the
        /// diagnostic JSON reads the richer data.
        /// </summary>
        private Dictionary<string, PortWmiInfo> GetUsbPortWmiInfo()
        {
            var result = new Dictionary<string, PortWmiInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Description, Manufacturer, HardwareID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "";
                        string desc = obj["Description"]?.ToString() ?? "";
                        string manufacturer = obj["Manufacturer"]?.ToString() ?? "";

                        // HardwareID in WMI is typically a string[] — the first entry
                        // holds the VID/PID token (e.g. "USB\VID_346E&PID_0005&REV_0100").
                        // Join all entries so downstream matching never loses data.
                        string hardwareId = "";
                        if (obj["HardwareID"] is string[] hids && hids.Length > 0)
                        {
                            hardwareId = string.Join(";", hids);
                        }
                        else
                        {
                            hardwareId = obj["HardwareID"]?.ToString() ?? "";
                        }

                        // Extract COM port number from name like "Gudsen MOZA R9 Base (COM3)"
                        var comMatch = Regex.Match(name, @"\(COM(\d+)\)");
                        if (!comMatch.Success) continue;

                        string portName = "COM" + comMatch.Groups[1].Value;
                        string fullDesc = $"{manufacturer} {name} {desc}".Trim();

                        var vidMatch = Regex.Match(hardwareId, @"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                        var pidMatch = Regex.Match(hardwareId, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);

                        result[portName] = new PortWmiInfo
                        {
                            Description = fullDesc,
                            HardwareId = hardwareId,
                            Vid = vidMatch.Success ? vidMatch.Groups[1].Value.ToUpperInvariant() : "",
                            Pid = pidMatch.Success ? pidMatch.Groups[1].Value.ToUpperInvariant() : "",
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logWarn($"[MozaSerial] WMI query failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Matches a USB description string against known Moza device patterns.
        /// </summary>
        private static (MozaDeviceRegistry.MozaDeviceType type, string subType)? ClassifyDevice(string description)
        {
            if (string.IsNullOrEmpty(description)) return null;

            foreach (var pattern in MozaDeviceRegistry.UsbPatterns)
            {
                if (pattern.Pattern.IsMatch(description))
                {
                    // Skip the generic fallback pattern for "Unknown" — only use it if nothing else matched
                    if (pattern.DeviceType == MozaDeviceRegistry.MozaDeviceType.Unknown)
                        continue;

                    return (pattern.DeviceType, pattern.SubType ?? "");
                }
            }

            // Check generic Gudsen/Moza fallback
            var fallback = MozaDeviceRegistry.UsbPatterns
                .FirstOrDefault(p => p.DeviceType == MozaDeviceRegistry.MozaDeviceType.Unknown);
            if (fallback != null && fallback.Pattern.IsMatch(description))
                return (MozaDeviceRegistry.MozaDeviceType.Unknown, "");

            return null;
        }

        /// <summary>
        /// Checks for processes that commonly hold Moza COM ports open. PitHouse
        /// UI is the obvious one, but closing the UI leaves PitHouse's background
        /// service and Moza's sync daemon running — they keep the port locked.
        /// Users who reported "PitHouse is closed but Moza still doesn't work"
        /// were usually hitting one of these.
        /// </summary>
        private static readonly string[] PitHouseProcessCandidates = new[]
        {
            "PitHouse",              // main UI
            "MozaBackgroundSystem",  // sync daemon, keeps running after UI close
            "MozaService",           // some installers register this as a service helper
            "MozaUsbGateway",        // USB bridge, holds devices for the daemon
            "Nextmotion",            // older Moza companion
        };

        private void CheckForPitHouse()
        {
            try
            {
                var offenders = new List<string>();
                foreach (var name in PitHouseProcessCandidates)
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName(name);
                    if (processes.Length > 0)
                    {
                        offenders.Add(name + ".exe");
                        foreach (var p in processes) p.Dispose();
                    }
                }

                if (offenders.Count > 0)
                {
                    PitHouseWarning = "Moza software is running and holding serial ports: "
                        + string.Join(", ", offenders)
                        + ". Quit these processes (Pit House + its background service) so the plugin can access the hardware.";
                    _logWarn($"[MozaSerial] {PitHouseWarning}");
                }
                else
                {
                    PitHouseWarning = "";
                }
            }
            catch { /* ignore — process enumeration can fail */ }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SERIAL PORT MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        //  PROTOCOL PROBE
        //  Identify a Moza device by talking to it. The description-regex
        //  classifier in MozaDeviceRegistry can't help when the USB descriptor
        //  is generic (no "Gudsen"/"MOZA" substring) — many driver stacks
        //  expose Moza devices as plain "USB Serial Port (COMn)". The probe
        //  bypasses descriptors entirely: send each candidate device type a
        //  known-safe read command, watch for a valid framed response with
        //  the matching nibble-swapped device ID. The device that answers IS
        //  the device. Scales to every current and future Moza product
        //  without a per-product VID/PID catalog.
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Per-device-type single-byte read command used as the probe. Each is
        /// a non-destructive read of a current setting (FFB strength, deadzone,
        /// brightness, etc.). The response is short (~5 bytes) and the parser
        /// only accepts it if the framing, checksum, and nibble-swapped device
        /// ID all match the request — false positives from non-Moza devices
        /// are extremely unlikely.
        /// </summary>
        private static readonly (MozaDeviceRegistry.MozaDeviceType Type, byte ProbeCmd)[] ProbeCandidates = new[]
        {
            (MozaDeviceRegistry.MozaDeviceType.Wheelbase,     MozaDeviceRegistry.WheelbaseCmd.FfbStrength),
            (MozaDeviceRegistry.MozaDeviceType.Pedals,        MozaDeviceRegistry.PedalCmd.GetCommand(MozaDeviceRegistry.PedalAxis.Throttle, MozaDeviceRegistry.PedalCmd.Deadzone)),
            (MozaDeviceRegistry.MozaDeviceType.Shifter,       MozaDeviceRegistry.ShifterCmd.Direction),
            (MozaDeviceRegistry.MozaDeviceType.Handbrake,     MozaDeviceRegistry.HandbrakeCmd.Deadzone),
            (MozaDeviceRegistry.MozaDeviceType.Dashboard,     MozaDeviceRegistry.DashboardCmd.Brightness),
            (MozaDeviceRegistry.MozaDeviceType.SteeringWheel, MozaDeviceRegistry.WheelCmd.PaddleMode),
            (MozaDeviceRegistry.MozaDeviceType.UniversalHub,  MozaDeviceRegistry.HubCmd.CompatibilityMode),
        };

        /// <summary>
        /// Cache of ports we've probed and confirmed don't speak the Moza
        /// protocol. Avoids re-spamming Arduinos/3D printers/CNC controllers
        /// on every 10s discovery cycle. Cleared when the port set changes
        /// so hot-plugged devices get a fresh probe.
        /// </summary>
        private readonly HashSet<string> _probedNotMoza = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string[] _lastKnownPortNames = new string[0];

        /// <summary>
        /// If the port list has changed since the last discovery cycle, drop
        /// the not-Moza cache so newly-attached devices get probed.
        /// </summary>
        private void RefreshProbeCacheIfPortsChanged(string[] currentPortNames)
        {
            bool changed = currentPortNames.Length != _lastKnownPortNames.Length
                || !currentPortNames.OrderBy(p => p).SequenceEqual(_lastKnownPortNames.OrderBy(p => p));
            if (changed)
            {
                _probedNotMoza.Clear();
                _lastKnownPortNames = (string[])currentPortNames.Clone();
            }
        }

        /// <summary>
        /// Opens a port, runs ProbeOpenPort, and closes it. Used by discovery
        /// for unclassified ports and (with cache disabled) by the diagnostic.
        /// </summary>
        private MozaDeviceRegistry.MozaDeviceType? OpenAndProbe(string portName, bool useCache = true)
        {
            if (useCache && _probedNotMoza.Contains(portName)) return null;
            // Don't probe ports we've already opened for polling — the open
            // would fail and a probe response could collide with live traffic.
            if (_ports.ContainsKey(portName)) return null;

            SerialPort port = null;
            try
            {
                port = new SerialPort(portName, BaudRate, SerialParity, DataBits, SerialStopBits)
                {
                    ReadTimeout = ReadTimeoutMs,
                    WriteTimeout = WriteTimeoutMs,
                    Handshake = Handshake.None
                };
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                var result = ProbeOpenPort(port);
                if (result == null && useCache) _probedNotMoza.Add(portName);
                return result;
            }
            catch (Exception ex)
            {
                // AccessDenied / NotFound / etc. — port unprobeable right now.
                // Don't cache as not-Moza: it might be Pit House holding the
                // port, and once Pit House quits we want to retry.
                _logWarn($"[MozaSerial] Probe open failed on {portName}: {ex.Message}");
                return null;
            }
            finally
            {
                if (port != null)
                {
                    try { port.Close(); } catch { }
                    try { port.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// Sends a probe read packet for each candidate device type and
        /// returns the type that responded with a valid framed packet whose
        /// nibble-swapped device ID matches the request. Caller is
        /// responsible for opening and closing the port.
        /// </summary>
        private static MozaDeviceRegistry.MozaDeviceType? ProbeOpenPort(SerialPort port)
        {
            foreach (var candidate in ProbeCandidates)
            {
                byte deviceId = MozaDeviceRegistry.GetDeviceId(candidate.Type);
                if (deviceId == 0) continue;

                try
                {
                    byte[] packet = MozaPacketBuilder.BuildReadPacket(deviceId, candidate.ProbeCmd);
                    port.DiscardInBuffer();
                    port.Write(packet, 0, packet.Length);

                    // ~200ms total wait per candidate. Real Moza devices reply
                    // in tens of ms; the retry budget covers slow USB hubs and
                    // scheduling jitter.
                    int available = 0;
                    for (int retry = 0; retry < 5; retry++)
                    {
                        Thread.Sleep(40);
                        available = port.BytesToRead;
                        if (available > 0) break;
                    }
                    if (available <= 0) continue;

                    byte[] buffer = new byte[Math.Min(available, ReadBufferSize)];
                    int read = port.Read(buffer, 0, buffer.Length);
                    if (read <= 0) continue;
                    if (read < buffer.Length)
                        Array.Resize(ref buffer, read);

                    var responses = MozaResponseParser.ParseResponses(buffer);
                    foreach (var resp in responses)
                    {
                        // The parser already validated start byte + checksum.
                        // We additionally require: this is a read-response
                        // (group 0xA1) and the un-swapped device ID matches
                        // the one we asked. That's strong enough that random
                        // serial noise from a non-Moza device is essentially
                        // never going to false-positive.
                        if (resp.IsReadResponse && resp.DeviceId == deviceId)
                        {
                            return candidate.Type;
                        }
                    }
                }
                catch (TimeoutException) { /* next candidate */ }
                catch (System.IO.IOException) { return null; }
                catch (InvalidOperationException) { return null; }
            }
            return null;
        }

        private bool TryOpenPort(MozaDevice device)
        {
            try
            {
                var port = new SerialPort(device.PortName, BaudRate, SerialParity, DataBits, SerialStopBits)
                {
                    ReadTimeout = ReadTimeoutMs,
                    WriteTimeout = WriteTimeoutMs,
                    Handshake = Handshake.None
                };

                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                _ports[device.PortName] = port;
                _portLocks[device.PortName] = new SemaphoreSlim(1, 1);
                device.IsConnected = true;

                return true;
            }
            catch (Exception ex)
            {
                _logWarn($"[MozaSerial] Failed to open {device.PortName}: {ex.Message}");
                return false;
            }
        }

        private void ClosePort(string portName)
        {
            // Acquire semaphore to ensure no SendAndReceive is in progress
            SemaphoreSlim semaphore = null;
            if (_portLocks.TryGetValue(portName, out semaphore))
            {
                if (!semaphore.Wait(WriteTimeoutMs))
                    _logWarn($"[MozaSerial] Timeout acquiring lock for {portName} during close");
            }

            try
            {
                if (_ports.TryRemove(portName, out var port))
                {
                    try { port.Close(); } catch { }
                    try { port.Dispose(); } catch { }
                }

                if (_devices.TryGetValue(portName, out var device))
                    device.IsConnected = false;
            }
            finally
            {
                // Release the semaphore and dispose it
                if (semaphore != null)
                {
                    try { semaphore.Release(); } catch { }
                    try { _portLocks.TryRemove(portName, out _); semaphore.Dispose(); } catch { }
                }
            }
        }

        private void CloseAllPorts()
        {
            foreach (var portName in _ports.Keys.ToList())
                ClosePort(portName);
        }

        // ═══════════════════════════════════════════════════════════════
        //  POLLING — READ DEVICE SETTINGS
        // ═══════════════════════════════════════════════════════════════

        private void PollDevice(MozaDevice device)
        {
            byte[][] commands = GetPollCommands(device);
            if (commands == null || commands.Length == 0)
            {
                device.RecordSuccess(); // Nothing to poll but device exists
                return;
            }

            foreach (var packet in commands)
            {
                byte[] response = SendAndReceive(device.PortName, packet);
                if (response == null)
                {
                    if (device.RecordFailure())
                    {
                        _logWarn($"[MozaSerial] {device.DisplayName} disconnected after {MozaDevice.MaxFailures} failures");
                        ClosePort(device.PortName);
                    }
                    return; // Stop polling this device on failure
                }

                // Parse and apply responses
                var parsed = MozaResponseParser.ParseResponses(response);
                foreach (var resp in parsed)
                {
                    ApplyResponse(device, resp);
                }
            }

            device.RecordSuccess();
        }

        /// <summary>
        /// Builds the set of read-request packets for a device based on its type.
        /// </summary>
        private byte[][] GetPollCommands(MozaDevice device)
        {
            switch (device.DeviceType)
            {
                case MozaDeviceRegistry.MozaDeviceType.Wheelbase:
                    return BuildReadPackets(MozaDeviceRegistry.DeviceWheelbase,
                        MozaDeviceRegistry.WheelbaseCmd.PollCommands,
                        MozaDeviceRegistry.WheelbaseCmd.TwoByteCommands);

                case MozaDeviceRegistry.MozaDeviceType.Pedals:
                    return BuildPedalReadPackets();

                case MozaDeviceRegistry.MozaDeviceType.Handbrake:
                    return BuildReadPackets(MozaDeviceRegistry.DeviceHandbrake,
                        MozaDeviceRegistry.HandbrakeCmd.PollCommands,
                        MozaDeviceRegistry.HandbrakeCmd.TwoByteCommands);

                case MozaDeviceRegistry.MozaDeviceType.Shifter:
                    return BuildReadPackets(MozaDeviceRegistry.DeviceShifter,
                        MozaDeviceRegistry.ShifterCmd.PollCommands, null);

                case MozaDeviceRegistry.MozaDeviceType.Dashboard:
                    return BuildReadPackets(MozaDeviceRegistry.DeviceDashboard,
                        MozaDeviceRegistry.DashboardCmd.PollCommands, null);

                case MozaDeviceRegistry.MozaDeviceType.SteeringWheel:
                    // Poll both primary (0x15) and extended (0x17)
                    var primary = BuildReadPackets(MozaDeviceRegistry.DeviceSteeringWheelPrimary,
                        MozaDeviceRegistry.WheelCmd.PrimaryPollCommands, null);
                    var extended = BuildReadPackets(MozaDeviceRegistry.DeviceSteeringWheelExtended,
                        MozaDeviceRegistry.WheelCmd.ExtendedPollCommands, null);
                    var combined = new byte[primary.Length + extended.Length][];
                    primary.CopyTo(combined, 0);
                    extended.CopyTo(combined, primary.Length);
                    return combined;

                default:
                    return null;
            }
        }

        private byte[][] BuildReadPackets(byte deviceId, byte[] commands, byte[] twoByteCommands)
        {
            var packets = new List<byte[]>();
            foreach (var cmd in commands)
            {
                packets.Add(MozaPacketBuilder.BuildReadPacket(deviceId, cmd));
            }
            // Also poll 2-byte commands
            if (twoByteCommands != null)
            {
                foreach (var cmd in twoByteCommands)
                {
                    packets.Add(MozaPacketBuilder.BuildReadPacket(deviceId, cmd));
                }
            }
            return packets.ToArray();
        }

        private byte[][] BuildPedalReadPackets()
        {
            var packets = new List<byte[]>();
            foreach (var axis in new[] { MozaDeviceRegistry.PedalAxis.Throttle, MozaDeviceRegistry.PedalAxis.Brake, MozaDeviceRegistry.PedalAxis.Clutch })
            {
                // Single-byte settings
                var singleByteOffsets = new byte[]
                {
                    MozaDeviceRegistry.PedalCmd.Deadzone,
                    MozaDeviceRegistry.PedalCmd.CurveY1, MozaDeviceRegistry.PedalCmd.CurveY2,
                    MozaDeviceRegistry.PedalCmd.CurveY3, MozaDeviceRegistry.PedalCmd.CurveY4,
                    MozaDeviceRegistry.PedalCmd.CurveY5, MozaDeviceRegistry.PedalCmd.HidSource
                };
                foreach (var offset in singleByteOffsets)
                {
                    byte cmd = MozaDeviceRegistry.PedalCmd.GetCommand(axis, offset);
                    packets.Add(MozaPacketBuilder.BuildReadPacket(MozaDeviceRegistry.DevicePedals, cmd));
                }

                // Two-byte settings (calibration)
                foreach (var offset in MozaDeviceRegistry.PedalCmd.TwoByteOffsets)
                {
                    byte cmd = MozaDeviceRegistry.PedalCmd.GetCommand(axis, offset);
                    packets.Add(MozaPacketBuilder.BuildReadPacket(MozaDeviceRegistry.DevicePedals, cmd));
                }
            }
            return packets.ToArray();
        }

        // ═══════════════════════════════════════════════════════════════
        //  RESPONSE HANDLING
        // ═══════════════════════════════════════════════════════════════

        private void ApplyResponse(MozaDevice device, MozaResponse response)
        {
            if (!response.IsReadResponse) return;
            if (response.CommandAndPayload == null || response.CommandAndPayload.Length < 2) return;

            byte commandId = MozaResponseParser.GetCommandId(response);
            bool isTwoByte = MozaDeviceRegistry.IsTwoByteCommand(device.DeviceId, commandId);

            int value;
            if (isTwoByte)
            {
                if (response.CommandAndPayload.Length < 3) return;
                value = MozaResponseParser.GetValueUInt16(response);
            }
            else
            {
                value = MozaResponseParser.GetValueByte(response);
            }

            switch (device.DeviceType)
            {
                case MozaDeviceRegistry.MozaDeviceType.Wheelbase:
                    device.WheelbaseSettings?.ApplyValue(commandId, value);
                    break;
                case MozaDeviceRegistry.MozaDeviceType.Pedals:
                    device.PedalSettings?.ApplyValue(commandId, value);
                    break;
                case MozaDeviceRegistry.MozaDeviceType.Handbrake:
                    device.HandbrakeSettings?.ApplyValue(commandId, value);
                    break;
                case MozaDeviceRegistry.MozaDeviceType.Shifter:
                    device.ShifterSettings?.ApplyValue(commandId, value);
                    break;
                case MozaDeviceRegistry.MozaDeviceType.Dashboard:
                    device.DashboardSettings?.ApplyValue(commandId, value);
                    break;
                case MozaDeviceRegistry.MozaDeviceType.SteeringWheel:
                    // Route to primary or extended based on response device ID
                    if (response.DeviceId == MozaDeviceRegistry.DeviceSteeringWheelPrimary)
                        device.WheelSettings?.ApplyPrimaryValue(commandId, value);
                    else if (response.DeviceId == MozaDeviceRegistry.DeviceSteeringWheelExtended)
                        device.WheelSettings?.ApplyExtendedValue(commandId, value);
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  WRITE QUEUE PROCESSING
        // ═══════════════════════════════════════════════════════════════

        private void ProcessWriteQueue()
        {
            int processed = 0;
            while (_writeQueue.TryDequeue(out var cmd) && processed < 20)
            {
                // Find the port for this device ID
                var device = _devices.Values.FirstOrDefault(d =>
                    d.DeviceId == cmd.DeviceId && d.IsConnected);

                if (device != null)
                {
                    SendPacket(device.PortName, cmd.Packet);
                }
                processed++;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SERIAL I/O (thread-safe per port)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Sends a packet and reads the response, with per-port locking.
        /// Returns the raw response bytes, or null on failure.
        /// </summary>
        private byte[] SendAndReceive(string portName, byte[] packet)
        {
            if (!_ports.TryGetValue(portName, out var port)) return null;
            if (!_portLocks.TryGetValue(portName, out var semaphore)) return null;

            if (!semaphore.Wait(WriteTimeoutMs)) return null;
            try
            {
                if (!port.IsOpen) return null;

                port.DiscardInBuffer();
                port.Write(packet, 0, packet.Length);

                // Retry loop: wait up to ~200ms total for data to appear
                int available = 0;
                for (int retry = 0; retry < 5; retry++)
                {
                    Thread.Sleep(50);
                    available = port.BytesToRead;
                    if (available > 0) break;
                }

                if (available <= 0) return null;

                byte[] buffer = new byte[Math.Min(available, ReadBufferSize)];
                int read = port.Read(buffer, 0, buffer.Length);
                if (read <= 0) return null;

                if (read < buffer.Length)
                    Array.Resize(ref buffer, read);
                return buffer;
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (System.IO.IOException)
            {
                return null; // Port disconnected
            }
            catch (InvalidOperationException)
            {
                return null; // Port closed
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a packet without waiting for a response (for write commands).
        /// </summary>
        private void SendPacket(string portName, byte[] packet)
        {
            if (!_ports.TryGetValue(portName, out var port)) return;
            if (!_portLocks.TryGetValue(portName, out var semaphore)) return;

            if (!semaphore.Wait(WriteTimeoutMs)) return;
            try
            {
                if (port.IsOpen)
                    port.Write(packet, 0, packet.Length);
            }
            catch (Exception ex)
            {
                _logWarn($"[MozaSerial] Write error on {portName}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        // ── Internal write command struct ─────────────────────────────
        private struct WriteCommand
        {
            public byte DeviceId;
            public byte[] Packet;
        }
    }
}
