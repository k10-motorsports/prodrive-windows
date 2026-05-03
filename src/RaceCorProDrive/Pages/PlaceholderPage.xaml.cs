using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RaceCorProDrive.Pages
{
    public sealed partial class PlaceholderPage : Page
    {
        public PlaceholderPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Clear titlebar slots so we don't inherit tabs/filters from
            // whatever page navigated us here. This page has no chrome
            // of its own.
            App.MainWindow?.SetTitleBarTabs(null);
            App.MainWindow?.SetTitleBarFilters(null);

            // The tag is passed as the parameter
            if (e.Parameter is string tag)
            {
                var pageTitle = tag switch
                {
                    "moments" => "Moments",
                    "tracks" => "Tracks",
                    "cars" => "Cars",
                    "dna" => "DNA",
                    "when" => "When",
                    "safety" => "Safety",
                    "composure" => "Composure",
                    "debrief" => "Debrief",
                    "settings" => "Settings",
                    _ => "Page"
                };

                PageTitle.Text = pageTitle;
            }
        }
    }
}
