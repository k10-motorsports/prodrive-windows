using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;

namespace RaceCorProDrive.DesignSystem.Components
{
    /// <summary>
    /// Settings row for picking a Windows audio / video device. Replaces
    /// the freeform text fields the Recording card used to ship with
    /// (paste-an-id-and-pray was a port-from-web shortcut, not a UX
    /// decision). Persists the device <c>Id</c> string — that's stable
    /// across reboots and USB hub re-orderings; the friendly Name is
    /// only the displayed label.
    ///
    /// Build the row, hand it to a SettingsCard, that's it. The control
    /// kicks off its own async device enumeration on Loaded; the user
    /// can hit the refresh chip (↻) to re-enumerate after plugging in
    /// new hardware.
    /// </summary>
    public sealed class DevicePickerRow : UserControl
    {
        private readonly DeviceClass _deviceClass;
        private readonly Func<string> _getter;
        private readonly Action<string> _setter;
        private readonly ComboBox _combo;
        private readonly Button _refreshBtn;
        private bool _suppressSelection;

        /// <summary>
        /// Holder for a device the dropdown can show. <c>Id</c> is the
        /// stable WinRT device-info id (used both for ComboBox.SelectedValue
        /// and for the persisted setting). <c>Disconnected</c> tags a
        /// pseudo-entry shown when the persisted id no longer enumerates,
        /// so the user can see what they had selected without losing it.
        /// </summary>
        private sealed class Item
        {
            public string Id { get; init; } = "";
            public string DisplayName { get; init; } = "";
            public bool Disconnected { get; init; }
            public override string ToString() => Disconnected ? $"⚠ {DisplayName}" : DisplayName;
        }

        public DevicePickerRow(DeviceClass deviceClass, Func<string> getter, Action<string> setter)
        {
            _deviceClass = deviceClass;
            _getter = getter;
            _setter = setter;

            _combo = new ComboBox
            {
                MinWidth = 240,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _combo.SelectionChanged += OnSelectionChanged;

            _refreshBtn = new Button
            {
                Content = "↻",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 0, 8, 0),
                MinWidth = 32,
            };
            ToolTipService.SetToolTip(_refreshBtn, "Re-scan devices");
            _refreshBtn.Click += async (_, __) => await PopulateAsync();

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_combo, 0);
            Grid.SetColumn(_refreshBtn, 1);
            row.Children.Add(_combo);
            row.Children.Add(_refreshBtn);

            Content = row;
            Loaded += async (_, __) => await PopulateAsync();
        }

        private async Task PopulateAsync()
        {
            _refreshBtn.IsEnabled = false;
            try
            {
                DeviceInformationCollection devices;
                try
                {
                    devices = await DeviceInformation.FindAllAsync(_deviceClass);
                }
                catch (Exception)
                {
                    // Some device classes throw on systems without the
                    // backing service (rare). Treat as empty list.
                    devices = null!;
                }

                var items = new List<Item>();
                if (devices != null)
                {
                    foreach (var d in devices)
                    {
                        items.Add(new Item { Id = d.Id, DisplayName = d.Name });
                    }
                }

                var savedId = _getter() ?? "";
                // If the persisted id isn't in the live device list, prepend
                // a "disconnected" placeholder so the user can see what was
                // selected and not silently lose it.
                if (!string.IsNullOrEmpty(savedId) && !ItemsContainId(items, savedId))
                {
                    items.Insert(0, new Item
                    {
                        Id = savedId,
                        DisplayName = "Disconnected",
                        Disconnected = true,
                    });
                }
                // Always offer an explicit "(none)" so the user can clear a
                // selection without diving into the JSON.
                items.Add(new Item { Id = "", DisplayName = "(none)" });

                _suppressSelection = true;
                try
                {
                    _combo.ItemsSource = items;
                    _combo.DisplayMemberPath = nameof(Item.DisplayName);
                    _combo.SelectedValuePath = nameof(Item.Id);
                    _combo.SelectedValue = savedId;
                }
                finally
                {
                    _suppressSelection = false;
                }
            }
            finally
            {
                _refreshBtn.IsEnabled = true;
            }
        }

        private static bool ItemsContainId(List<Item> items, string id)
        {
            foreach (var i in items) if (i.Id == id) return true;
            return false;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection) return;
            if (_combo.SelectedValue is string id) _setter(id);
        }
    }
}
