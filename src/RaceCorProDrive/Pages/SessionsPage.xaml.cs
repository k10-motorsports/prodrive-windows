using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Api;
using System.Collections.ObjectModel;

namespace RaceCorProDrive.Pages
{
    public sealed partial class SessionsPage : Page
    {
        private readonly ApiClient _apiClient = new();
        private readonly ObservableCollection<SessionItemViewModel> _sessions = new();
        private int _offset = 0;
        private const int PageSize = 50;
        private string _currentCategory = "all";

        public SessionsPage()
        {
            this.InitializeComponent();
            SessionsList.ItemsSource = _sessions;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadSessions();
        }

        private async Task LoadSessions()
        {
            LoadingRing.IsActive = true;
            ErrorText.Text = "";

            try
            {
                var response = await _apiClient.GetAsync<SessionsResponse>(
                    $"/api/v1/sessions?limit={PageSize}&offset={_offset}&category={_currentCategory}");

                if (response?.Sessions != null)
                {
                    foreach (var session in response.Sessions)
                    {
                        _sessions.Add(new SessionItemViewModel
                        {
                            Id = session.Id,
                            TrackName = session.TrackName ?? "Unknown Track",
                            CarName = session.CarName ?? "Unknown Car",
                            DateStr = session.CreatedAt.ToString("MMM d, yyyy")
                        });
                    }

                    LoadMoreButton.IsEnabled = response.HasMore;
                    _offset += PageSize;
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Failed to load sessions: {ex.Message}";
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentCategory = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLower() ?? "all";
            _sessions.Clear();
            _offset = 0;
            _ = LoadSessions();
        }

        private async void OnLoadMore(object sender, RoutedEventArgs e)
        {
            await LoadSessions();
        }

        private void OnSessionClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SessionItemViewModel session)
            {
                this.Frame.Navigate(typeof(SessionDetailPage), session.Id);
            }
        }
    }

    public class SessionItemViewModel
    {
        public string Id { get; set; } = "";
        public string TrackName { get; set; } = "";
        public string CarName { get; set; } = "";
        public string DateStr { get; set; } = "";
    }
}
