using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfWidgets.Views
{
    public partial class WeatherSettingsView : System.Windows.Controls.UserControl
    {
        private bool _isInitialized = false;
        private const string PLACEHOLDER = "Enter city name (e.g. London, Seattle)...";
        private readonly HttpClient _httpClient = new();

        public WeatherSettingsView()
        {
            InitializeComponent();
            RefreshSettings();
        }

        public void RefreshSettings()
        {
            _isInitialized = false;

            WeatherEnabledToggle.IsChecked = WidgetConfig.Current.WeatherEnabled;
            LockPositionToggle.IsChecked = WidgetConfig.Current.WeatherLocked;
            UnitCelsiusToggle.IsChecked = WidgetConfig.Current.UseCelsius;

            ActiveLocationLabel.Text = $"Current Location: {WidgetConfig.Current.WeatherLocationName} ({WidgetConfig.Current.WeatherLatitude:F4}, {WidgetConfig.Current.WeatherLongitude:F4})";
            LookupStatusText.Text = "Ready";

            ResetSearchPlaceholder();

            _isInitialized = true;
        }

        private void WeatherEnabledToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isEnabled = WeatherEnabledToggle.IsChecked ?? false;
            WidgetConfig.Current.WeatherEnabled = isEnabled;
            WidgetConfig.Save();

            var app = System.Windows.Application.Current as App;
            app?.SetWeatherWidgetVisibility(isEnabled);
        }

        private void LockPositionToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isLocked = LockPositionToggle.IsChecked ?? false;
            WidgetConfig.Current.WeatherLocked = isLocked;
            WidgetConfig.Save();

            var app = System.Windows.Application.Current as App;
            var weatherWin = app?.WeatherWidgetWindow;
            weatherWin?.ApplyLockState();
        }

        private void UnitCelsiusToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool useCelsius = UnitCelsiusToggle.IsChecked ?? false;
            WidgetConfig.Current.UseCelsius = useCelsius;
            WidgetConfig.Save();

            var app = System.Windows.Application.Current as App;
            var weatherWin = app?.WeatherWidgetWindow;
            _ = weatherWin?.RefreshWeatherAsync();
        }

        // Search TextBox Placeholder Management
        private void ResetSearchPlaceholder()
        {
            LocationSearchInput.Text = PLACEHOLDER;
            LocationSearchInput.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150));
        }

        private void LocationSearchInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (LocationSearchInput.Text == PLACEHOLDER)
            {
                LocationSearchInput.Text = "";
                LocationSearchInput.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("DashboardText");
            }
        }

        private void LocationSearchInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LocationSearchInput.Text))
            {
                ResetSearchPlaceholder();
            }
        }

        // Geocoding API City Search
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string query = LocationSearchInput.Text.Trim();
            if (string.IsNullOrEmpty(query) || query == PLACEHOLDER)
            {
                LookupStatusText.Text = "Please enter a city name first.";
                return;
            }

            SearchButton.IsEnabled = false;
            LookupStatusText.Text = "Searching location...";

            try
            {
                string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1&language=en&format=json";
                string json = await _httpClient.GetStringAsync(url);
                
                var response = JsonSerializer.Deserialize<GeocodingResponse>(json);
                if (response?.results != null && response.results.Count > 0)
                {
                    var result = response.results[0];
                    
                    // Format Location description (city, state/country)
                    string region = !string.IsNullOrEmpty(result.admin1) ? result.admin1 : result.country;
                    string formattedName = $"{result.name}, {region}";

                    // Save settings
                    WidgetConfig.Current.WeatherLocationName = formattedName;
                    WidgetConfig.Current.WeatherLatitude = result.latitude;
                    WidgetConfig.Current.WeatherLongitude = result.longitude;
                    WidgetConfig.Current.WeatherAutoDetect = false;
                    WidgetConfig.Save();

                    // Update UI Labels
                    ActiveLocationLabel.Text = $"Current Location: {formattedName} ({result.latitude:F4}, {result.longitude:F4})";
                    LookupStatusText.Text = $"Successfully linked to {result.name}!";

                    // Reset search text
                    ResetSearchPlaceholder();

                    // Refresh widget contents
                    var app = System.Windows.Application.Current as App;
                    var weatherWin = app?.WeatherWidgetWindow;
                    if (weatherWin != null)
                    {
                        _ = weatherWin.RefreshWeatherAsync();
                    }
                }
                else
                {
                    LookupStatusText.Text = "City not found. Try specifying state or country (e.g. 'Paris, FR').";
                }
            }
            catch (Exception ex)
            {
                LookupStatusText.Text = $"Lookup error: {ex.Message}";
            }
            finally
            {
                SearchButton.IsEnabled = true;
            }
        }

        private async void AutoDetectButton_Click(object sender, RoutedEventArgs e)
        {
            AutoDetectButton.IsEnabled = false;
            LookupStatusText.Text = "Detecting location from IP...";

            try
            {
                var app = System.Windows.Application.Current as App;
                var weatherWin = app?.WeatherWidgetWindow;
                bool success = false;
                
                if (weatherWin != null)
                {
                    success = await weatherWin.AutoDetectLocationAsync(silent: true);
                }
                else
                {
                    // Fallback local detection if weather window is null
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "WpfWidgets/1.0");
                    string json = await client.GetStringAsync("https://ipapi.co/json/");
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("city", out var cityProp) &&
                        root.TryGetProperty("latitude", out var latProp) &&
                        root.TryGetProperty("longitude", out var lonProp))
                    {
                        string city = cityProp.GetString() ?? "";
                        string state = root.TryGetProperty("region", out var regionProp) ? (regionProp.GetString() ?? "") : "";
                        string formattedName = string.IsNullOrEmpty(state) ? city : $"{city}, {state}";
                        
                        WidgetConfig.Current.WeatherLocationName = formattedName;
                        WidgetConfig.Current.WeatherLatitude = latProp.GetDouble();
                        WidgetConfig.Current.WeatherLongitude = lonProp.GetDouble();
                        WidgetConfig.Current.WeatherAutoDetect = true;
                        WidgetConfig.Save();
                        success = true;
                    }
                }

                if (success)
                {
                    // Mark auto-detect as preferred for future refreshes
                    WidgetConfig.Current.WeatherAutoDetect = true;
                    WidgetConfig.Save();

                    RefreshSettings();
                    LookupStatusText.Text = $"Detected: {WidgetConfig.Current.WeatherLocationName}";
                    
                    // Trigger forecast update on window if active
                    if (weatherWin != null)
                    {
                        _ = weatherWin.RefreshWeatherAsync();
                    }
                }
                else
                {
                    LookupStatusText.Text = "Detection failed. Try searching manually.";
                }
            }
            catch (Exception ex)
            {
                LookupStatusText.Text = $"Detection error: {ex.Message}";
            }
            finally
            {
                AutoDetectButton.IsEnabled = true;
            }
        }

        private void ScrollViewer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.08));
                e.Handled = true;
            }
        }


        // Open-Meteo Geocoding classes
        private class GeocodingResponse
        {
            public List<GeocodingResult> results { get; set; }
        }

        private class GeocodingResult
        {
            public string name { get; set; }
            public double latitude { get; set; }
            public double longitude { get; set; }
            public string country { get; set; }
            public string admin1 { get; set; }
        }
    }
}
