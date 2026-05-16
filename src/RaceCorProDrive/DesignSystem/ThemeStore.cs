using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace RaceCorProDrive.DesignSystem
{
    /// <summary>
    /// Themed-theme-set source of truth. Fetches <c>/api/theme-sets</c>
    /// from the server, persists the user's selection, and drives the
    /// auto-rotation timer. Background-image rendering was removed; this
    /// store now only tracks the active theme slug for picker UI.
    /// </summary>
    public sealed class ThemeStore : INotifyPropertyChanged
    {
        public static ThemeStore Shared { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public const string AutoSlug = "__auto__";
        public const int RotationIntervalSeconds = 5 * 60;

        private const string DefaultsKey = "racecor.theme.slug";

        private readonly HttpClient _http = new();
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? DispatcherQueue.GetForCurrentThread();

        private CancellationTokenSource? _rotationCts;

        public IReadOnlyList<ThemeSet> Sets { get; private set; } = Array.Empty<ThemeSet>();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; Raise(); }
        }

        private string _selectedSlug = AutoSlug;
        public string SelectedSlug
        {
            get => _selectedSlug;
            set
            {
                if (_selectedSlug == value) return;
                _selectedSlug = value;
                SaveSelected();
                Raise();
                Raise(nameof(IsAutoMode));
                if (IsAutoMode) { AdvanceRotation(); ScheduleRotation(); }
                else { _rotationCts?.Cancel(); ActiveSlug = value; }
            }
        }

        private string _activeSlug = "default";
        public string ActiveSlug
        {
            get => _activeSlug;
            private set
            {
                if (_activeSlug == value) return;
                _activeSlug = value;
                Raise();
                Raise(nameof(ActiveSet));
            }
        }

        public ThemeSet? ActiveSet
        {
            get
            {
                foreach (var s in Sets)
                    if (s.Slug == ActiveSlug) return s;
                return null;
            }
        }

        public bool IsAutoMode => SelectedSlug == AutoSlug;

        private ThemeStore()
        {
            var stored = RaceCorProDrive.Support.AppSettings.Get(DefaultsKey);
            _selectedSlug = string.IsNullOrEmpty(stored) ? AutoSlug : stored!;
            _activeSlug = _selectedSlug == AutoSlug ? "default" : _selectedSlug;
        }

        public async Task RefreshAsync()
        {
            OnUi(() => IsLoading = true);
            try
            {
                var response = await _http.GetFromJsonAsync<ThemeSetsResponse>(
                    "https://prodrive.racecor.io/api/theme-sets");
                if (response?.Sets is { } list)
                {
                    OnUi(() =>
                    {
                        Sets = list;
                        Raise(nameof(Sets));
                        if (IsAutoMode)
                        {
                            AdvanceRotation();
                            ScheduleRotation();
                        }
                        else if (!ContainsSlug(list, ActiveSlug))
                        {
                            ActiveSlug = "default";
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemeStore] refresh failed: {ex.Message}");
            }
            finally
            {
                OnUi(() => IsLoading = false);
            }
        }

        private void AdvanceRotation()
        {
            if (Sets.Count == 0) { ActiveSlug = "default"; return; }
            var pool = new List<ThemeSet>(Sets);
            if (Sets.Count > 1)
            {
                pool.RemoveAll(s => s.Slug == ActiveSlug);
            }
            if (pool.Count == 0) return;
            var next = pool[Random.Shared.Next(pool.Count)];
            ActiveSlug = next.Slug;
        }

        private void ScheduleRotation()
        {
            _rotationCts?.Cancel();
            _rotationCts = new CancellationTokenSource();
            var token = _rotationCts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(RotationIntervalSeconds), token); }
                    catch { return; }
                    OnUi(AdvanceRotation);
                }
            }, token);
        }

        private void SaveSelected()
        {
            RaceCorProDrive.Support.AppSettings.Set(DefaultsKey, _selectedSlug);
        }

        private static bool ContainsSlug(IReadOnlyList<ThemeSet> sets, string slug)
        {
            foreach (var s in sets) if (s.Slug == slug) return true;
            return false;
        }

        private void OnUi(Action action)
        {
            if (_dispatcher == null || _dispatcher.HasThreadAccess) action();
            else _dispatcher.TryEnqueue(() => action());
        }

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class ThemeSet
    {
        [System.Text.Json.Serialization.JsonPropertyName("slug")]
        public string Slug { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }

    internal sealed class ThemeSetsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("sets")]
        public List<ThemeSet>? Sets { get; set; }
    }
}
