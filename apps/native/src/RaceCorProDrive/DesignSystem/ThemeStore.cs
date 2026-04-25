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
    /// Themed-background source of truth. Mirrors the macOS / iOS
    /// <c>ThemeStore</c>: fetch <c>/api/theme-sets</c>, fall back to
    /// <c>/api/unsplash/background?brand=&lt;slug&gt;</c> when the
    /// theme set's <c>liveryImage</c> column is null, expose the
    /// resolved URL via <see cref="INotifyPropertyChanged"/> so
    /// XAML-bound backgrounds re-render without imperative wiring.
    /// </summary>
    /// <remarks>
    /// The auto-rotation loop is identical to the Swift build's: every
    /// `RotationIntervalSeconds` we pick a new theme slug from the set
    /// (excluding the current one when more than one is available) and
    /// re-resolve the image. Manual selection cancels the rotation
    /// task; setting the slug back to <see cref="AutoSlug"/> resumes it.
    /// </remarks>
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
        private readonly Dictionary<string, Uri> _imageCache = new();

        private CancellationTokenSource? _rotationCts;
        private CancellationTokenSource? _resolveCts;

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
                _ = RefreshImageAsync();
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

        private Uri? _activeImageUri;
        public Uri? ActiveImageUri
        {
            get => _activeImageUri;
            private set { _activeImageUri = value; Raise(); }
        }

        private ThemeStore()
        {
            var stored = Windows.Storage.ApplicationData.Current.LocalSettings
                .Values[DefaultsKey] as string;
            _selectedSlug = string.IsNullOrEmpty(stored) ? AutoSlug : stored!;
            _activeSlug = _selectedSlug == AutoSlug ? "default" : _selectedSlug;
        }

        // MARK: - Public API

        /// <summary>
        /// Fetches the theme-sets list from the server and starts the
        /// rotation loop if the user is in auto mode. Safe to call
        /// repeatedly (e.g. on app foreground); each call replaces the
        /// previous task.
        /// </summary>
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
                        else
                        {
                            // Re-resolve the image for the existing slug now
                            // that we have a freshly-fetched lookup.
                            _ = RefreshImageAsync();
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

        // MARK: - Rotation

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

        // MARK: - Image resolution

        private async Task RefreshImageAsync()
        {
            _resolveCts?.Cancel();
            var cts = new CancellationTokenSource();
            _resolveCts = cts;
            var slug = ActiveSlug;

            // Cache hit — instant.
            if (_imageCache.TryGetValue(slug, out var cached))
            {
                OnUi(() => ActiveImageUri = cached);
                return;
            }

            // DB-backed livery image → fastest real-image path.
            if (ActiveSet?.LiveryImage is { Length: > 0 } livery
                && Uri.TryCreate(livery, UriKind.Absolute, out var liveryUri))
            {
                _imageCache[slug] = liveryUri;
                OnUi(() => ActiveImageUri = liveryUri);
                return;
            }

            // Otherwise ask the server to pick an Unsplash match for
            // this theme the same way the web does. The previous image
            // stays visible until the new one resolves so we don't
            // flash to blank.
            try
            {
                var url = await FetchUnsplashAsync(slug, cts.Token);
                if (url == null || cts.Token.IsCancellationRequested) return;
                _imageCache[slug] = url;
                OnUi(() =>
                {
                    if (ActiveSlug == slug) ActiveImageUri = url;
                });
            }
            catch (TaskCanceledException) { /* superseded */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemeStore] unsplash fetch failed: {ex.Message}");
            }
        }

        private async Task<Uri?> FetchUnsplashAsync(string brand, CancellationToken token)
        {
            var url = $"https://prodrive.racecor.io/api/unsplash/background?brand={Uri.EscapeDataString(brand)}&theme=dark";
            using var resp = await _http.GetAsync(url, token);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<UnsplashBackgroundResponse>(cancellationToken: token);
            return Uri.TryCreate(body?.Url, UriKind.Absolute, out var abs) ? abs : null;
        }

        // MARK: - Persistence

        private void SaveSelected()
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[DefaultsKey] = _selectedSlug;
        }

        private static bool ContainsSlug(IReadOnlyList<ThemeSet> sets, string slug)
        {
            foreach (var s in sets) if (s.Slug == slug) return true;
            return false;
        }

        // MARK: - Plumbing

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
        [System.Text.Json.Serialization.JsonPropertyName("liveryImage")]
        public string? LiveryImage { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }

    internal sealed class ThemeSetsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("sets")]
        public List<ThemeSet>? Sets { get; set; }
    }

    internal sealed class UnsplashBackgroundResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string? Url { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("photographer")]
        public string? Photographer { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("photographerUrl")]
        public string? PhotographerUrl { get; set; }
    }
}
