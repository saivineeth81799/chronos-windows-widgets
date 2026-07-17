using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfWidgets
{
    public partial class WeatherWidgetWindow : Window
    {
        private bool _isDragging = false;
        private int _dragStartMouseX;
        private int _dragStartMouseY;
        private double _dragStartWindowX;
        private double _dragStartWindowY;
        
        private readonly HttpClient _httpClient = new();
        private readonly DispatcherTimer _refreshTimer;
        private WeatherResponse? _lastResponse;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        public WeatherWidgetWindow()
        {
            InitializeComponent();

            SourceInitialized += WeatherWidgetWindow_SourceInitialized;
            StateChanged += WeatherWidgetWindow_StateChanged;
            Activated += WeatherWidgetWindow_Activated;

            // Timer to refresh weather every 30 minutes
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _refreshTimer.Tick += (s, e) => _ = RefreshWeatherAsync();
            if (WidgetConfig.Current.WeatherEnabled)
            {
                _refreshTimer.Start();
                // Defer initial weather/geolocation fetch to reduce startup impact
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(8000);
                    await Dispatcher.InvokeAsync(async () => await InitializeWeatherAsync());
                });
            }
        }

        public void StartWeatherTimer()
        {
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
                _ = InitializeWeatherAsync();
            }
        }

        public void StopWeatherTimer()
        {
            _refreshTimer.Stop();
        }

        private async Task InitializeWeatherAsync()
        {
            // Auto detect if it's still default Seattle
            if (WidgetConfig.Current.WeatherLocationName == "Seattle, Washington" &&
                Math.Abs(WidgetConfig.Current.WeatherLatitude - 47.6062) < 0.001 &&
                Math.Abs(WidgetConfig.Current.WeatherLongitude - (-122.3321)) < 0.001)
            {
                await AutoDetectLocationAsync(silent: true);
            }
            await RefreshWeatherAsync();
        }

        public async Task<bool> AutoDetectLocationAsync(bool silent = false)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "WpfWidgets/1.0");
                
                double latitude = 0;
                double longitude = 0;
                string city = "";
                string state = "";
                bool locationFound = false;

                // 1. Try native Windows Geolocation Services (extremely accurate)
                try
                {
                    var accessStatus = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
                    if (accessStatus == Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed)
                    {
                        var geolocator = new Windows.Devices.Geolocation.Geolocator
                        {
                            DesiredAccuracyInMeters = 100
                        };
                        var pos = await geolocator.GetGeopositionAsync();
                        if (pos?.Coordinate?.Point?.Position != null)
                        {
                            latitude = pos.Coordinate.Point.Position.Latitude;
                            longitude = pos.Coordinate.Point.Position.Longitude;
                            locationFound = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WeatherWidget] Windows Geolocator failed: {ex.Message}");
                }

                // 2. Fallback to IP Geolocation if Windows Location is unavailable
                if (!locationFound)
                {
                    string json = "";
                    bool fetched = false;
                    
                    try
                    {
                        json = await client.GetStringAsync("https://ipapi.co/json/");
                        fetched = true;
                    }
                    catch
                    {
                        try
                        {
                            json = await client.GetStringAsync("https://freeipapi.com/api/json");
                        }
                        catch (Exception ex)
                        {
                            if (!silent)
                            {
                                System.Windows.MessageBox.Show($"Failed to connect to IP Geolocation servers: {ex.Message}", "Location Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                            return false;
                        }
                    }
                    
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (fetched) // ipapi.co
                    {
                        if (root.TryGetProperty("city", out var cityProp)) city = cityProp.GetString() ?? "";
                        if (root.TryGetProperty("region", out var regionProp)) state = regionProp.GetString() ?? "";
                        if (root.TryGetProperty("latitude", out var latProp)) latitude = latProp.GetDouble();
                        if (root.TryGetProperty("longitude", out var lonProp)) longitude = lonProp.GetDouble();
                    }
                    else // freeipapi.com
                    {
                        if (root.TryGetProperty("cityName", out var cityProp)) city = cityProp.GetString() ?? "";
                        if (root.TryGetProperty("regionName", out var regionProp)) state = regionProp.GetString() ?? "";
                        if (root.TryGetProperty("latitude", out var latProp)) latitude = latProp.GetDouble();
                        if (root.TryGetProperty("longitude", out var lonProp)) longitude = lonProp.GetDouble();
                    }
                    
                    if (latitude != 0 && longitude != 0)
                    {
                        locationFound = true;
                    }
                }

                // 3. Resolve the true city name using reverse geocoding for the coordinates
                if (locationFound)
                {
                    string formattedName = "";
                    try
                    {
                        string rgcUrl = $"https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={latitude}&longitude={longitude}&localityLanguage=en";
                        string rgcJson = await client.GetStringAsync(rgcUrl);
                        using var rgcDoc = JsonDocument.Parse(rgcJson);
                        var rgcRoot = rgcDoc.RootElement;
                        
                        string trueCity = "";
                        if (rgcRoot.TryGetProperty("localityInfo", out var localityInfoVal) &&
                            localityInfoVal.TryGetProperty("administrative", out var adminArrayVal) &&
                            adminArrayVal.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in adminArrayVal.EnumerateArray())
                            {
                                if (item.TryGetProperty("adminLevel", out var levelVal) && levelVal.GetInt32() == 8)
                                {
                                    trueCity = item.GetProperty("name").GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(trueCity))
                        {
                            if (rgcRoot.TryGetProperty("city", out var cityVal) && !string.IsNullOrEmpty(cityVal.GetString()))
                            {
                                trueCity = cityVal.GetString() ?? "";
                            }
                            else if (rgcRoot.TryGetProperty("locality", out var locVal) && !string.IsNullOrEmpty(locVal.GetString()))
                            {
                                trueCity = locVal.GetString() ?? "";
                            }
                        }

                        string trueState = "";
                        if (rgcRoot.TryGetProperty("principalSubdivision", out var stateVal))
                        {
                            trueState = stateVal.GetString() ?? "";
                        }

                        if (!string.IsNullOrEmpty(trueCity))
                        {
                            formattedName = string.IsNullOrEmpty(trueState) ? trueCity : $"{trueCity}, {trueState}";
                        }
                    }
                    catch
                    {
                        // Fallback to IP city name
                        formattedName = string.IsNullOrEmpty(state) ? city : $"{city}, {state}";
                    }

                    if (string.IsNullOrEmpty(formattedName))
                    {
                        formattedName = $"{latitude:F3}, {longitude:F3}";
                    }

                    WidgetConfig.Current.WeatherLocationName = formattedName;
                    WidgetConfig.Current.WeatherLatitude = latitude;
                    WidgetConfig.Current.WeatherLongitude = longitude;
                    WidgetConfig.Save();
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    System.Windows.MessageBox.Show($"Failed to auto-detect location: {ex.Message}", "Location Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            return false;
        }

        private void WeatherWidgetWindow_SourceInitialized(object sender, EventArgs e)
        {
            // Load custom size if configured
            Width = WidgetConfig.Current.WeatherWidth;
            Height = WidgetConfig.Current.WeatherHeight;

            // 1. Load position
            double x = WidgetConfig.Current.WeatherPositionX;
            double y = WidgetConfig.Current.WeatherPositionY;

            if (x < 0 || y < 0)
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                Left = screenWidth - Width - 50; // 50px from right margin
                Top = 310; // Positioned below digital clock widget
            }
            else
            {
                Left = x;
                Top = y;
            }

            // 2. Attach to wallpaper
            DesktopWindowHelper.AttachToDesktop(this);

            // Enable native frosted glass blur
            DesktopWindowHelper.EnableBlur(this);
            DesktopWindowHelper.SetRoundedWindowRegion(this, 8);

            // 3. Set click-through state
            ApplyLockState();

            // 4. Setup resize hook
            SetupResizeHook();
        }

        public void ApplyLockState()
        {
            DesktopWindowHelper.SetClickThrough(this, WidgetConfig.Current.WeatherLocked);
            DesktopWindowHelper.PushToBottom(this);
        }

        private void WeatherWidgetWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    WindowState = WindowState.Normal;
                    DesktopWindowHelper.PushToBottom(this);
                }));
            }
        }

        private void WeatherWidgetWindow_Activated(object sender, EventArgs e)
        {
            DesktopWindowHelper.PushToBottom(this);
        }

        // Window drag movement
        private void WidgetCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WidgetConfig.Current.WeatherLocked) return;

            POINT mousePos;
            if (GetCursorPos(out mousePos))
            {
                _isDragging = true;
                _dragStartMouseX = mousePos.X;
                _dragStartMouseY = mousePos.Y;
                _dragStartWindowX = Left;
                _dragStartWindowY = Top;
                WidgetCard.CaptureMouse();
            }
        }

        private void WidgetCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                WidgetCard.ReleaseMouseCapture();
                
                WidgetConfig.Current.WeatherPositionX = Left;
                WidgetConfig.Current.WeatherPositionY = Top;
                WidgetConfig.Save();
            }
        }

        private void WidgetCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;

            POINT mousePos;
            if (GetCursorPos(out mousePos))
            {
                double deltaX = mousePos.X - _dragStartMouseX;
                double deltaY = mousePos.Y - _dragStartMouseY;

                double scaleX = 1.0;
                double scaleY = 1.0;
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                double deltaXDIP = deltaX / scaleX;
                double deltaYDIP = deltaY / scaleY;

                double rawLeft = _dragStartWindowX + deltaXDIP;
                double rawTop = _dragStartWindowY + deltaYDIP;

                // 1. Snapping to a 20px grid
                const int gridSize = 20;
                double snappedLeft = Math.Round(rawLeft / gridSize) * gridSize;
                double snappedTop = Math.Round(rawTop / gridSize) * gridSize;

                // 2. Snapping to screen edges (within 15px margin)
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                if (Math.Abs(snappedLeft) < 15)
                {
                    snappedLeft = 0;
                }
                else if (Math.Abs(snappedLeft + Width - screenWidth) < 15)
                {
                    snappedLeft = screenWidth - Width;
                }

                if (Math.Abs(snappedTop) < 15)
                {
                    snappedTop = 0;
                }
                else if (Math.Abs(snappedTop + Height - screenHeight) < 15)
                {
                    snappedTop = screenHeight - Height;
                }

                Left = snappedLeft;
                Top = snappedTop;
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DesktopWindowHelper.SetRoundedWindowRegion(this, 8);
            if (this.IsLoaded)
            {
                WidgetConfig.Current.WeatherWidth = e.NewSize.Width;
                WidgetConfig.Current.WeatherHeight = e.NewSize.Height;
                WidgetConfig.Save();
            }
        }

        // Fetch Weather API
        public async Task RefreshWeatherAsync()
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                LocationText.Text = "Updating weather...";
                RefreshButton.IsEnabled = false;
                RefreshButton.Opacity = 0.4;
            }));

            // If auto-detect is enabled, refresh coordinates before fetching forecast
            if (WidgetConfig.Current.WeatherAutoDetect)
            {
                await AutoDetectLocationAsync(silent: true);
            }

            try
            {
                string tempUnit = WidgetConfig.Current.UseCelsius ? "celsius" : "fahrenheit";
                string url = $"https://api.open-meteo.com/v1/forecast?latitude={WidgetConfig.Current.WeatherLatitude}&longitude={WidgetConfig.Current.WeatherLongitude}&current_weather=true&hourly=temperature_2m,weathercode&daily=temperature_2m_max,temperature_2m_min,weathercode&timezone=auto&temperature_unit={tempUnit}&forecast_days=4&past_days=1";
                
                string json = await _httpClient.GetStringAsync(url);
                var response = JsonSerializer.Deserialize<WeatherResponse>(json);

                if (response != null)
                {
                    Dispatcher.Invoke(() => UpdateUI(response));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WeatherWidget] Load failed: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    LocationText.Text = $"{WidgetConfig.Current.WeatherLocationName} (Offline)";
                });
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshButton.IsEnabled = true;
                    RefreshButton.Opacity = 0.7;
                }));
            }
        }

        private void UpdateUI(WeatherResponse data)
        {
            _lastResponse = data;
            LocationText.Text = WidgetConfig.Current.WeatherLocationName;

            // Current Weather Details
            int curCode = data.current_weather.weathercode;
            bool isDay = data.current_weather.is_day == 1;
            var curDetails = GetWeatherCodeDetails(curCode, isDay);

            ConditionLabel.Text = curDetails.text;
            CurrentConditionIcon.Text = curDetails.icon;
            CurrentConditionIcon.Foreground = curDetails.color;

            string unitSymbol = WidgetConfig.Current.UseCelsius ? "°C" : "°F";
            CurrentTempText.Text = $"{Math.Round(data.current_weather.temperature)}{unitSymbol}";

            if (data.daily != null && data.daily.time != null && data.daily.time.Count > 0)
            {
                // Find today's index dynamically (since past_days=1 shifts indices)
                int todayIdx = 0;
                if (!string.IsNullOrEmpty(data.current_weather?.time) && data.current_weather.time.Length >= 10)
                {
                    string todayStr = data.current_weather.time.Substring(0, 10);
                    int idx = data.daily.time.IndexOf(todayStr);
                    if (idx >= 0)
                    {
                        todayIdx = idx;
                    }
                }

                if (todayIdx < data.daily.temperature_2m_max.Count)
                {
                    double todayMax = data.daily.temperature_2m_max[todayIdx];
                    double todayMin = data.daily.temperature_2m_min[todayIdx];
                    TempRangeLabel.Text = $"H: {Math.Round(todayMax)}°  L: {Math.Round(todayMin)}°";
                }

                // 3-Day Forecast rows starting from tomorrow (todayIdx + 1)
                if (data.daily.time.Count > todayIdx + 3)
                {
                    // Day 1 (Tomorrow)
                    ForecastDay1Label.Text = GetDayOfWeekAbbrev(data.daily.time[todayIdx + 1]);
                    int d1Code = GetNoonHourlyCode(data, data.daily.time[todayIdx + 1]);
                    var d1Details = GetWeatherCodeDetails(d1Code);
                    ForecastDay1Icon.Text = d1Details.icon;
                    ForecastDay1Icon.Foreground = d1Details.color;
                    ForecastDay1Temp.Text = $"{Math.Round(data.daily.temperature_2m_max[todayIdx + 1])}°/{Math.Round(data.daily.temperature_2m_min[todayIdx + 1])}°";

                    // Day 2 (Overmorrow)
                    ForecastDay2Label.Text = GetDayOfWeekAbbrev(data.daily.time[todayIdx + 2]);
                    int d2Code = GetNoonHourlyCode(data, data.daily.time[todayIdx + 2]);
                    var d2Details = GetWeatherCodeDetails(d2Code);
                    ForecastDay2Icon.Text = d2Details.icon;
                    ForecastDay2Icon.Foreground = d2Details.color;
                    ForecastDay2Temp.Text = $"{Math.Round(data.daily.temperature_2m_max[todayIdx + 2])}°/{Math.Round(data.daily.temperature_2m_min[todayIdx + 2])}°";

                    // Day 3 (Three days out)
                    ForecastDay3Label.Text = GetDayOfWeekAbbrev(data.daily.time[todayIdx + 3]);
                    int d3Code = GetNoonHourlyCode(data, data.daily.time[todayIdx + 3]);
                    var d3Details = GetWeatherCodeDetails(d3Code);
                    ForecastDay3Icon.Text = d3Details.icon;
                    ForecastDay3Icon.Foreground = d3Details.color;
                    ForecastDay3Temp.Text = $"{Math.Round(data.daily.temperature_2m_max[todayIdx + 3])}°/{Math.Round(data.daily.temperature_2m_min[todayIdx + 3])}°";
                }
            }

            // Draw the trend graph
            DrawWeatherGraph(data);
        }

        private string GetDayOfWeekAbbrev(string dateString)
        {
            try
            {
                if (DateTime.TryParse(dateString, out DateTime dt))
                {
                    return dt.ToString("ddd");
                }
            }
            catch {}
            return "---";
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Simple rotation click micro-animation
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(600)
            };
            RefreshButtonRotation.BeginAnimation(RotateTransform.AngleProperty, anim);
            await RefreshWeatherAsync();
        }

        public void UpdateTheme()
        {
            if (_lastResponse != null)
            {
                Dispatcher.Invoke(() => UpdateUI(_lastResponse));
            }
        }

        private bool IsLightTheme()
        {
            string theme = WidgetConfig.Current.ClockTheme;
            if (theme == "System")
            {
                return App.IsSystemInAppsLightTheme();
            }
            return theme == "Light";
        }

        // Translate Open-Meteo WMO weather code to icon and desc
        private (string text, string icon, System.Windows.Media.Brush color) GetWeatherCodeDetails(int code, bool isDay = true)
        {
            if (IsLightTheme())
            {
                var blackBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26));
                switch (code)
                {
                    case 0:
                        return (!isDay ? "Clear Night" : "Clear Sky", !isDay ? "\uF159" : "\uE81A", blackBrush);
                    case 1:
                    case 2:
                        return (!isDay ? "Partly Cloudy" : "Mainly Clear", !isDay ? "\uF174" : "\uF172", blackBrush);
                    case 3:
                        return ("Overcast",     "\uE2BD", blackBrush);
                    case 45:
                    case 48:
                        return ("Foggy",        "\uE818", blackBrush);
                    case 51:
                    case 53:
                    case 55:
                    case 56:
                    case 57:
                        return ("Drizzle",      "\uF67F", blackBrush);
                    case 61:
                    case 63:
                    case 65:
                    case 66:
                    case 67:
                        return ("Rainy",        "\uF176", blackBrush);
                    case 71:
                    case 73:
                    case 75:
                    case 77:
                        return ("Snowy",        "\uE2CD", blackBrush);
                    case 80:
                    case 81:
                    case 82:
                        return ("Rain Showers", "\uF176", blackBrush);
                    case 95:
                    case 96:
                    case 99:
                        return ("Thunderstorm", "\uEBDB", blackBrush);
                    default:
                        return ("Clear Sky",    "\uE81A", blackBrush);
                }
            }

            switch (code)
            {
                case 0:
                    if (!isDay)
                    {
                        return ("Clear Night", "\uF159", new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 195, 230))); // clear_night
                    }
                    return ("Clear Sky",    "\uE81A", new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 188, 5)));   // sunny
                case 1:
                case 2:
                    if (!isDay)
                    {
                        return ("Partly Cloudy", "\uF174", new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 170, 200))); // partly_cloudy_night
                    }
                    return ("Mainly Clear", "\uF172", new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 210, 240))); // partly_cloudy_day
                case 3:
                    return ("Overcast",     "\uE2BD", new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200))); // cloudy
                case 45:
                case 48:
                    return ("Foggy",        "\uE818", new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 180, 190))); // foggy
                case 51:
                case 53:
                case 55:
                case 56:
                case 57:
                    return ("Drizzle",      "\uF67F", new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 160, 200))); // hail (light drizzle)
                case 61:
                case 63:
                case 65:
                case 66:
                case 67:
                    return ("Rainy",        "\uF176", new SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 133, 244)));  // rainy
                case 71:
                case 73:
                case 75:
                case 77:
                    return ("Snowy",        "\uE2CD", new SolidColorBrush(System.Windows.Media.Color.FromRgb(164, 217, 255))); // cloudy_snowing
                case 80:
                case 81:
                case 82:
                    return ("Rain Showers", "\uF176", new SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 133, 244)));  // rainy
                case 95:
                case 96:
                case 99:
                    return ("Thunderstorm", "\uEBDB", new SolidColorBrush(System.Windows.Media.Color.FromRgb(143, 62, 151))); // thunderstorm
                default:
                    return ("Clear Sky",    "\uE81A", new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 188, 5)));
            }
        }

        // Native Resizing
        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private void SetupResizeHook()
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                if (WidgetConfig.Current.WeatherLocked) return IntPtr.Zero;

                // Get mouse coordinates relative to screen
                int x = (int)(lParam.ToInt32() & 0xFFFF);
                int y = (int)((lParam.ToInt32() >> 16) & 0xFFFF);

                // Convert to local window coordinates
                System.Windows.Point localPoint = this.PointFromScreen(new System.Windows.Point(x, y));

                double width = this.ActualWidth;
                double height = this.ActualHeight;
                double borderWidth = 16; // thickness of the hit-test border (generous for easier grabbing)

                bool left = localPoint.X < borderWidth;
                bool right = localPoint.X > width - borderWidth;
                bool top = localPoint.Y < borderWidth;
                bool bottom = localPoint.Y > height - borderWidth;

                if (top && left) { handled = true; return new IntPtr(HTTOPLEFT); }
                if (top && right) { handled = true; return new IntPtr(HTTOPRIGHT); }
                if (bottom && left) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
                if (bottom && right) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
                if (left) { handled = true; return new IntPtr(HTLEFT); }
                if (right) { handled = true; return new IntPtr(HTRIGHT); }
                if (top) { handled = true; return new IntPtr(HTTOP); }
                if (bottom) { handled = true; return new IntPtr(HTBOTTOM); }
            }
            return IntPtr.Zero;
        }

        // Pick the noon (12:00) hourly weathercode for a given date string
        private int GetNoonHourlyCode(WeatherResponse data, string dateString)
        {
            if (data.hourly?.time == null) return 0;
            string noonTarget = $"{dateString}T12:00";
            int idx = data.hourly.time.IndexOf(noonTarget);
            if (idx >= 0 && idx < data.hourly.weathercode.Count)
                return data.hourly.weathercode[idx];
            // Fallback: find any hour on that date
            idx = data.hourly.time.FindIndex(t => t.StartsWith(dateString));
            return idx >= 0 ? data.hourly.weathercode[idx] : 0;
        }

        private void WeatherGraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_lastResponse != null)
            {
                DrawWeatherGraph(_lastResponse);
            }
        }

        private void DrawWeatherGraph(WeatherResponse data)
        {
            WeatherGraphCanvas.Children.Clear();

            if (data.hourly?.temperature_2m == null || data.hourly.time == null || data.current_weather == null)
                return;

            // Find current time index in hourly forecast
            int currentIndex = data.hourly.time.IndexOf(data.current_weather.time);
            if (currentIndex < 0)
            {
                // Fallback: match by closest hour
                if (data.current_weather.time != null && data.current_weather.time.Length >= 13)
                {
                    currentIndex = data.hourly.time.FindIndex(t => t.StartsWith(data.current_weather.time.Substring(0, 13)));
                }
            }

            if (currentIndex < 0) return;

            // Get points: past 4 hours, current, next 8 hours (total 13 points)
            var graphTemps = new List<double>();
            for (int i = -4; i <= 8; i++)
            {
                int targetIndex = currentIndex + i;
                if (targetIndex >= 0 && targetIndex < data.hourly.temperature_2m.Count)
                {
                    graphTemps.Add(data.hourly.temperature_2m[targetIndex]);
                }
                else
                {
                    // Boundary clamp
                    if (targetIndex < 0)
                        graphTemps.Add(data.hourly.temperature_2m[0]);
                    else
                        graphTemps.Add(data.hourly.temperature_2m[data.hourly.temperature_2m.Count - 1]);
                }
            }

            double canvasWidth = WeatherGraphCanvas.ActualWidth;
            double canvasHeight = WeatherGraphCanvas.ActualHeight;

            // Don't draw if the canvas hasn't loaded or has 0 size
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;

            // Find min/max temperature in our 11-point window to scale the graph
            double minTemp = double.MaxValue;
            double maxTemp = double.MinValue;
            foreach (double temp in graphTemps)
            {
                if (temp < minTemp) minTemp = temp;
                if (temp > maxTemp) maxTemp = temp;
            }

            // Avoid division by zero if all temperatures are the same
            if (Math.Abs(maxTemp - minTemp) < 0.001)
            {
                maxTemp += 1.0;
                minTemp -= 1.0;
            }

            // Calculate coordinates
            int pointCount = graphTemps.Count; // should be 11
            var xPoints = new List<double>();
            var yPoints = new List<double>();

            double yMargin = 12; // safety margin so the graph doesn't clip top/bottom
            for (int i = 0; i < pointCount; i++)
            {
                double x = i * (canvasWidth / (pointCount - 1));
                
                // Scale Y coordinate
                double ratio = (graphTemps[i] - minTemp) / (maxTemp - minTemp);
                double y = canvasHeight - yMargin - (ratio * (canvasHeight - 2 * yMargin));
                
                xPoints.Add(x);
                yPoints.Add(y);
            }

            // Determine graph colors matching weather details
            int curCode = data.current_weather.weathercode;
            bool isDay = data.current_weather.is_day == 1;
            var details = GetWeatherCodeDetails(curCode, isDay);
            System.Windows.Media.Brush themeStrokeBrush = details.color;

            // Create gradient fill brush
            var themeColor = System.Windows.Media.Colors.Gray;
            if (themeStrokeBrush is SolidColorBrush solidBrush)
            {
                themeColor = solidBrush.Color;
            }

            var graphFillBrush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(0, 1)
            };
            graphFillBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(140, themeColor.R, themeColor.G, themeColor.B), 0.0)); // ~0.55 opacity
            graphFillBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, themeColor.R, themeColor.G, themeColor.B), 1.0));  // 0.0 opacity

            // 1. Draw the filled area under the line
            var fillFigure = new PathFigure
            {
                StartPoint = new System.Windows.Point(0, canvasHeight),
                IsClosed = true
            };
            fillFigure.Segments.Add(new LineSegment(new System.Windows.Point(0, yPoints[0]), false));
            for (int i = 1; i < pointCount; i++)
            {
                fillFigure.Segments.Add(new LineSegment(new System.Windows.Point(xPoints[i], yPoints[i]), true));
            }
            fillFigure.Segments.Add(new LineSegment(new System.Windows.Point(canvasWidth, canvasHeight), false));

            var fillGeometry = new PathGeometry();
            fillGeometry.Figures.Add(fillFigure);

            var fillPath = new System.Windows.Shapes.Path
            {
                Fill = graphFillBrush,
                Data = fillGeometry
            };
            WeatherGraphCanvas.Children.Add(fillPath);

            // 2. Draw the trend line itself
            var lineFigure = new PathFigure
            {
                StartPoint = new System.Windows.Point(0, yPoints[0]),
                IsClosed = false
            };
            for (int i = 1; i < pointCount; i++)
            {
                lineFigure.Segments.Add(new LineSegment(new System.Windows.Point(xPoints[i], yPoints[i]), true));
            }

            var lineGeometry = new PathGeometry();
            lineGeometry.Figures.Add(lineFigure);

            var linePath = new System.Windows.Shapes.Path
            {
                Stroke = themeStrokeBrush,
                StrokeThickness = 1.5,
                Data = lineGeometry
            };
            WeatherGraphCanvas.Children.Add(linePath);

            // 3. Draw "Current Time" vertical cursor line (at index 4)
            if (pointCount > 4)
            {
                var cursorLine = new System.Windows.Shapes.Line
                {
                    X1 = xPoints[4],
                    Y1 = 0,
                    X2 = xPoints[4],
                    Y2 = canvasHeight,
                    Stroke = new SolidColorBrush(IsLightTheme() ? System.Windows.Media.Color.FromArgb(30, 0, 0, 0) : System.Windows.Media.Color.FromArgb(30, 255, 255, 255)),
                    StrokeThickness = 1.0,
                    StrokeDashArray = new DoubleCollection(new double[] { 4, 4 })
                };
                WeatherGraphCanvas.Children.Add(cursorLine);

                // 4. Draw current temperature dot
                var currentDot = new System.Windows.Shapes.Ellipse
                {
                    Width = 6.0,
                    Height = 6.0,
                    Fill = themeStrokeBrush,
                    Stroke = new SolidColorBrush(IsLightTheme() ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black),
                    StrokeThickness = 1.0
                };
                System.Windows.Controls.Canvas.SetLeft(currentDot, xPoints[4] - 3.0);
                System.Windows.Controls.Canvas.SetTop(currentDot, yPoints[4] - 3.0);
                WeatherGraphCanvas.Children.Add(currentDot);
            }
        }

        // Open-Meteo response mapping classes
        private class WeatherResponse
        {
            public CurrentWeather current_weather { get; set; }
            public DailyWeather daily { get; set; }
            public HourlyWeather hourly { get; set; }
        }

        private class CurrentWeather
        {
            public double temperature { get; set; }
            public int weathercode { get; set; }
            public int is_day { get; set; }
            public string time { get; set; }
        }

        private class DailyWeather
        {
            public List<string> time { get; set; }
            public List<double> temperature_2m_max { get; set; }
            public List<double> temperature_2m_min { get; set; }
            public List<int> weathercode { get; set; }
        }

        private class HourlyWeather
        {
            public List<string> time { get; set; }
            public List<int> weathercode { get; set; }
            public List<double> temperature_2m { get; set; }
        }
    }
}
