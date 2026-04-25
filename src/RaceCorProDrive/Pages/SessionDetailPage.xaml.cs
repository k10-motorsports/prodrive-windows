using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Api;

namespace RaceCorProDrive.Pages
{
    public sealed partial class SessionDetailPage : Page
    {
        private readonly ApiClient _apiClient = new();
        private string? _sessionId;

        public SessionDetailPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _sessionId = e.Parameter?.ToString();
            if (!string.IsNullOrEmpty(_sessionId))
            {
                _ = LoadSession();
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Navigation setup
        }

        private async Task LoadSession()
        {
            if (string.IsNullOrEmpty(_sessionId))
                return;

            try
            {
                var response = await _apiClient.GetAsync<SessionDetailResponse>($"/api/v1/sessions/{_sessionId}");

                if (response?.Session != null)
                {
                    var session = response.Session;
                    SessionTitle.Text = session.TrackName ?? "Session";
                    TrackInfo.Text = $"Track: {session.TrackName ?? "Unknown"}";
                    CarInfo.Text = $"Car: {session.CarName ?? "Unknown"}";
                    DateInfo.Text = session.CreatedAt.ToString("MMM d, yyyy HH:mm");

                    // Display laps
                    var lapLabels = response.Laps?.Select(l =>
                        $"Lap {l.LapNumber}: {l.LapTime:mm\\:ss\\.fff}") ?? new List<string>();

                    LapsList.ItemsSource = lapLabels.ToList();
                }

                LoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Failed to load session: {ex.Message}";
                LoadingRing.IsActive = false;
            }
        }

        private void OnBack(object sender, RoutedEventArgs e)
        {
            this.Frame.GoBack();
        }
    }
}
