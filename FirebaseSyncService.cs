using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WpfWidgets
{
    public class CalendarEvent
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string StartTime { get; set; } = "";
        public int Duration { get; set; } = 60;
        public string Color { get; set; } = "";
        public string Date { get; set; } = "";
        public string RecurringRuleId { get; set; } = "";
        public string OriginalDate { get; set; } = "";
        public string Status { get; set; } = ""; // 'deleted', etc.

        // Derived properties for UI binding
        [System.Text.Json.Serialization.JsonIgnore]
        public string TimeDisplay => string.IsNullOrEmpty(StartTime) ? "All Day" : FormatTime(StartTime);
        [System.Text.Json.Serialization.JsonIgnore]
        public string DurationDisplay => Duration >= 1440 ? "All Day" : (Duration >= 60 ? $"{Duration / 60}h {(Duration % 60 > 0 ? (Duration % 60) + "m" : "")}".Trim() : $"{Duration}m");
        
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Visibility TimeSeparatorVisibility => string.IsNullOrEmpty(StartTime) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Media.Brush AccentBrush => GetBrushFromColorString(Color);

        private System.Windows.Media.Brush GetBrushFromColorString(string colorStr)
        {
            if (string.IsNullOrEmpty(colorStr))
            {
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 133, 244)); // Google Blue #4285F4
            }

            colorStr = colorStr.Trim().ToLower();

            // Map Tailwind color classes commonly used in Chronos
            if (colorStr.StartsWith("bg-"))
            {
                return colorStr switch
                {
                    "bg-blue-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6")),
                    "bg-red-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444")),
                    "bg-green-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")),
                    "bg-yellow-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")),
                    "bg-purple-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B5CF6")),
                    "bg-indigo-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6366F1")),
                    "bg-pink-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EC4899")),
                    "bg-gray-500" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B7280")),
                    _ => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6"))
                };
            }

            try
            {
                if (!colorStr.StartsWith("#"))
                {
                    colorStr = "#" + colorStr;
                }
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 133, 244)); // Default Blue
            }
        }

        private string FormatTime(string timeStr)
        {
            try
            {
                var parts = timeStr.Split(':');
                int hour = int.Parse(parts[0]);
                int minute = int.Parse(parts[1]);
                bool is24 = WidgetConfig.Current.Is24HourFormat;
                
                if (is24)
                {
                    return $"{hour:D2}:{minute:D2}";
                }
                else
                {
                    string ampm = hour >= 12 ? "PM" : "AM";
                    int hour12 = hour % 12;
                    if (hour12 == 0) hour12 = 12;
                    return $"{hour12}:{minute:D2} {ampm}";
                }
            }
            catch
            {
                return timeStr;
            }
        }
    }

    public class FirebaseSyncService
    {
        private static readonly string GoogleClientId;
        private static readonly string GoogleClientSecret;
        private static readonly string FirebaseApiKey;

        static FirebaseSyncService()
        {
            string clientId = "YOUR_GOOGLE_CLIENT_ID";
            string clientSecret = "YOUR_GOOGLE_CLIENT_SECRET";
            string apiKey = "YOUR_FIREBASE_API_KEY";

            try
            {
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("GoogleClientId", out var cid))
                            clientId = cid.GetString() ?? clientId;
                        if (root.TryGetProperty("GoogleClientSecret", out var cs))
                            clientSecret = cs.GetString() ?? clientSecret;
                        if (root.TryGetProperty("FirebaseApiKey", out var ak))
                            apiKey = ak.GetString() ?? apiKey;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSyncService] Failed to load appsettings.json: {ex.Message}");
            }

            GoogleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? clientId;
            GoogleClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? clientSecret;
            FirebaseApiKey = Environment.GetEnvironmentVariable("FIREBASE_API_KEY") ?? apiKey;
        }
        
        private readonly HttpClient _httpClient;
        private readonly DispatcherTimer _syncTimer;
        private string _idToken = "";

        private class GoogleTokenCache
        {
            public string AccessToken { get; set; } = "";
            public DateTime ExpirationTime { get; set; }
        }

        private readonly Dictionary<string, GoogleTokenCache> _googleTokenCache = new();
        
        public bool IsLoggedIn { get; private set; }
        
        public event Action<bool>? LoginStatusChanged;
        public event Action<List<CalendarEvent>>? EventsUpdated;
        public event Action<List<WidgetTaskItem>>? TasksUpdated;
        public event Action<string>? SyncStatusChanged;

        public FirebaseSyncService()
        {
            _httpClient = new HttpClient();
            
            // Periodically sync every 5 minutes
            _syncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _syncTimer.Tick += (s, e) => _ = SyncNowAsync();
        }

        public void Initialize()
        {
            string refreshToken = WidgetConfig.Current.GetRefreshToken();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                IsLoggedIn = true;
                LoginStatusChanged?.Invoke(true);
                
                // Only start background sync timer if Calendar or Tasks widget is active
                if (WidgetConfig.Current.CalendarEnabled || WidgetConfig.Current.TasksEnabled)
                {
                    _syncTimer.Start();
                    _ = SyncNowAsync();
                }
            }
            else
            {
                IsLoggedIn = false;
                LoginStatusChanged?.Invoke(false);
            }
        }

        public void StartBackgroundSync()
        {
            if (IsLoggedIn && !_syncTimer.IsEnabled)
            {
                _syncTimer.Start();
                _ = SyncNowAsync();
            }
        }

        public void StopBackgroundSync()
        {
            _syncTimer?.Stop();
        }

        public void SignOut()
        {
            WidgetConfig.Current.CalendarUserId = "";
            WidgetConfig.Current.CalendarRefreshTokenEncrypted = "";
            WidgetConfig.Current.CalendarEventsCache = "[]";
            WidgetConfig.Save();
            
            _idToken = "";
            IsLoggedIn = false;
            _syncTimer.Stop();
            
            LoginStatusChanged?.Invoke(false);
            EventsUpdated?.Invoke(new List<CalendarEvent>());
            SyncStatusChanged?.Invoke("Signed out.");
        }

        public async Task<bool> SignInWithEmailAsync(string email, string password)
        {
            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseApiKey}";
            var payload = new
            {
                email = email,
                password = password,
                returnSecureToken = true
            };

            try
            {
                SyncStatusChanged?.Invoke("Signing in with email...");
                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] Email sign-in failed: {response.StatusCode} - {err}");
                    SyncStatusChanged?.Invoke("Invalid email or password.");
                    return false;
                }

                return await ProcessFirebaseLoginResponseAsync(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Email sign-in exception: {ex.Message}");
                SyncStatusChanged?.Invoke($"Sign-in failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SignUpWithEmailAsync(string email, string password)
        {
            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseApiKey}";
            var payload = new
            {
                email = email,
                password = password,
                returnSecureToken = true
            };

            try
            {
                SyncStatusChanged?.Invoke("Creating account...");
                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] Email sign-up failed: {response.StatusCode} - {err}");
                    SyncStatusChanged?.Invoke("Failed to create account (email may already exist or password too weak).");
                    return false;
                }

                return await ProcessFirebaseLoginResponseAsync(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Email sign-up exception: {ex.Message}");
                SyncStatusChanged?.Invoke($"Sign-up failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ProcessFirebaseLoginResponseAsync(string resJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(resJson);
                var root = doc.RootElement;

                string localId = root.GetProperty("localId").GetString() ?? "";
                string idToken = root.GetProperty("idToken").GetString() ?? "";
                string refreshToken = root.GetProperty("refreshToken").GetString() ?? "";

                if (string.IsNullOrEmpty(localId) || string.IsNullOrEmpty(refreshToken))
                {
                    LogHelper.Log("[FirebaseSync] Invalid credentials received from Firebase.");
                    return false;
                }

                WidgetConfig.Current.CalendarUserId = localId;
                WidgetConfig.Current.SaveRefreshToken(refreshToken);
                WidgetConfig.Save();

                _idToken = idToken;
                IsLoggedIn = true;

                LoginStatusChanged?.Invoke(true);
                SyncStatusChanged?.Invoke("Logged in successfully.");

                _syncTimer.Start();
                _ = SyncNowAsync();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Error processing login response: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StartGoogleSignInAsync()
        {
            int port = 5006;
            string redirectUri = $"http://localhost:{port}/";

            // --- PKCE: generate code_verifier and code_challenge ---
            byte[] verifierBytes = new byte[64];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(verifierBytes);
            }
            string codeVerifier = Convert.ToBase64String(verifierBytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");

            byte[] challengeBytes;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                challengeBytes = sha256.ComputeHash(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
            }
            string codeChallenge = Convert.ToBase64String(challengeBytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");

            string state = Guid.NewGuid().ToString("N");

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);

            try
            {
                listener.Start();
                LogHelper.Log($"[FirebaseSync] Starting OAuth PKCE loopback listener on {redirectUri}");

                // Authorization Code + PKCE flow (replaces blocked implicit flow)
                string googleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
                    $"?client_id={GoogleClientId}" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    "&response_type=code" +
                    "&scope=openid%20email%20profile" +
                    $"&state={state}" +
                    "&code_challenge_method=S256" +
                    $"&code_challenge={codeChallenge}" +
                    "&access_type=offline" +   // request a refresh_token
                    "&prompt=consent";          // always show consent to ensure refresh_token is returned

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(googleAuthUrl)
                {
                    UseShellExecute = true
                });

                SyncStatusChanged?.Invoke("Waiting for browser login...");

                // Google redirects back with ?code=...&state=... as plain query params — no JS needed
                var context = await listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                string? authCode = request.QueryString["code"];
                string? returnedState = request.QueryString["state"];

                // Respond with a tidy success/error page
                bool codeReceived = !string.IsNullOrEmpty(authCode) && returnedState == state;
                string htmlPage = codeReceived
                    ? GetOAuthCallbackHtml(true, "Sign-in Successful", "Sign-in Successful", "You can close this tab and return to the Widgets app.")
                    : GetOAuthCallbackHtml(false, "Sign-in Failed", "Authentication Failed", "Invalid or missing authorization code. Please try again.");

                byte[] htmlBytes = System.Text.Encoding.UTF8.GetBytes(htmlPage);
                response.ContentType = "text/html";
                response.ContentLength64 = htmlBytes.Length;
                await response.OutputStream.WriteAsync(htmlBytes, 0, htmlBytes.Length);
                response.OutputStream.Close();
                response.Close();

                if (!codeReceived)
                {
                    LogHelper.Log("[FirebaseSync] Error: auth code missing or state mismatch.");
                    SyncStatusChanged?.Invoke("Login cancelled or failed.");
                    return false;
                }

                // Exchange auth code for tokens
                SyncStatusChanged?.Invoke("Connecting to Google...");
                string? googleIdToken = await ExchangeCodeForIdTokenAsync(authCode!, codeVerifier, redirectUri);

                if (string.IsNullOrEmpty(googleIdToken))
                {
                    SyncStatusChanged?.Invoke("Login failed: token exchange error.");
                    return false;
                }

                // Exchange Google id_token for Firebase session
                SyncStatusChanged?.Invoke("Connecting to Firebase...");
                bool fbSuccess = await ExchangeGoogleTokenForFirebaseAsync(googleIdToken);
                if (fbSuccess)
                {
                    _syncTimer.Start();
                }
                return fbSuccess;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] OAuth listener exception: {ex.Message}");
                SyncStatusChanged?.Invoke($"Login failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
        }

        private string GetOAuthCallbackHtml(bool success, string title, string header, string description)
        {
            string accent = success ? "#6366f1" : "#ef4444";
            string accentHover = success ? "#4f46e5" : "#dc2626";
            string accentShadow = success ? "rgba(99, 102, 241, 0.3)" : "rgba(239, 68, 68, 0.2)";
            string statusColor = success ? "#10b981" : "#ef4444";
            string statusBg = success ? "rgba(16, 185, 129, 0.1)" : "rgba(239, 68, 68, 0.1)";
            string bgGlow = success
                ? "radial-gradient(circle at 50% 35%, rgba(99, 102, 241, 0.15) 0%, rgba(9, 9, 11, 0) 60%), #09090b"
                : "radial-gradient(circle at 50% 35%, rgba(239, 68, 68, 0.12) 0%, rgba(9, 9, 11, 0) 60%), #09090b";

            string statusIconMarkup = success
                ? @"<svg class=""status-svg"" viewBox=""0 0 52 52"">
                        <circle class=""status-circle"" cx=""26"" cy=""26"" r=""25"" fill=""none""/>
                        <path class=""status-path"" fill=""none"" d=""M14.1 27.2l7.1 7.2 16.7-16.8""/>
                    </svg>"
                : @"<svg class=""status-svg"" viewBox=""0 0 52 52"">
                        <circle class=""status-circle"" cx=""26"" cy=""26"" r=""25"" fill=""none""/>
                        <path class=""status-path"" fill=""none"" d=""M16 16l20 20""/>
                        <path class=""status-path-2"" fill=""none"" d=""M36 16L16 36""/>
                    </svg>";

            string pulseMarkup = success
                ? @"<div class=""connector-pulse""></div>"
                : "";

            string clockStrokeColor = success ? "#818cf8" : "#ef4444";

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title}</title>
    <link href=""https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&display=swap"" rel=""stylesheet"">
    <style>
        :root {{
            --bg-glow: {bgGlow};
            --accent: {accent};
            --accent-hover: {accentHover};
            --accent-shadow: {accentShadow};
            --status-color: {statusColor};
            --status-bg: {statusBg};
        }}
        
        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}
        
        body {{
            font-family: 'Outfit', -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
            background: var(--bg-glow);
            color: #ffffff;
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
            overflow: hidden;
        }}

        .card {{
            background: rgba(18, 18, 24, 0.6);
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border: 1px solid rgba(255, 255, 255, 0.08);
            border-radius: 24px;
            padding: 48px 32px;
            width: 100%;
            max-width: 420px;
            text-align: center;
            box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4), inset 0 1px 0 rgba(255, 255, 255, 0.1);
            animation: fadeIn 0.8s cubic-bezier(0.16, 1, 0.3, 1) forwards;
            opacity: 0;
            transform: translateY(20px);
        }}

        @keyframes fadeIn {{
            to {{
                opacity: 1;
                transform: translateY(0);
            }}
        }}

        .logo-container {{
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 16px;
            margin-bottom: 36px;
        }}

        .logo-icon {{
            display: flex;
            align-items: center;
            justify-content: center;
            width: 40px;
            height: 40px;
            background: rgba(255, 255, 255, 0.04);
            border: 1px solid rgba(255, 255, 255, 0.08);
            border-radius: 12px;
            color: rgba(255, 255, 255, 0.9);
        }}

        .connector {{
            display: flex;
            align-items: center;
            justify-content: center;
            width: 40px;
            height: 2px;
            background: rgba(255, 255, 255, 0.08);
            position: relative;
            border-radius: 1px;
            overflow: hidden;
        }}

        .connector-pulse {{
            position: absolute;
            height: 100%;
            width: 12px;
            background: linear-gradient(90deg, transparent, var(--accent), transparent);
            left: -12px;
            animation: pulseSlide 2s infinite ease-in-out;
        }}

        @keyframes pulseSlide {{
            0% {{ left: -12px; }}
            100% {{ left: 40px; }}
        }}

        .status-icon-wrapper {{
            width: 80px;
            height: 80px;
            margin: 0 auto 28px;
            display: flex;
            align-items: center;
            justify-content: center;
        }}

        .status-svg {{
            width: 80px;
            height: 80px;
            border-radius: 50%;
            display: block;
            stroke-width: 3.5;
            stroke: var(--status-color);
            stroke-miterlimit: 10;
            box-shadow: inset 0 0 0 var(--status-color);
            animation: fillCircle 0.4s ease-in-out 0.4s forwards, scaleUp 0.3s ease-in-out 0.8s cubic-bezier(0.175, 0.885, 0.32, 1.275) forwards;
        }}

        .status-circle {{
            stroke-dasharray: 166;
            stroke-dashoffset: 166;
            stroke-width: 3.5;
            stroke-miterlimit: 10;
            stroke: var(--status-color);
            fill: none;
            animation: strokeCircle 0.6s cubic-bezier(0.65, 0, 0.45, 1) forwards;
        }}

        .status-path {{
            transform-origin: 50% 50%;
            stroke-dasharray: 48;
            stroke-dashoffset: 48;
            animation: strokePath 0.3s cubic-bezier(0.65, 0, 0.45, 1) 0.6s forwards;
        }}

        .status-path-2 {{
            transform-origin: 50% 50%;
            stroke-dasharray: 48;
            stroke-dashoffset: 48;
            animation: strokePath 0.3s cubic-bezier(0.65, 0, 0.45, 1) 0.7s forwards;
        }}

        @keyframes strokeCircle {{
            100% {{ stroke-dashoffset: 0; }}
        }}

        @keyframes strokePath {{
            100% {{ stroke-dashoffset: 0; }}
        }}

        @keyframes fillCircle {{
            100% {{ box-shadow: inset 0 0 0 40px var(--status-bg); }}
        }}

        @keyframes scaleUp {{
            0%, 100% {{ transform: none; }}
            50% {{ transform: scale3d(1.08, 1.08, 1); }}
        }}

        h2 {{
            font-size: 24px;
            font-weight: 600;
            margin-bottom: 12px;
            letter-spacing: -0.5px;
            color: #ffffff;
        }}

        p {{
            font-size: 15px;
            color: #a1a1aa;
            line-height: 1.6;
            margin-bottom: 36px;
            font-weight: 400;
        }}

        .btn {{
            display: inline-flex;
            align-items: center;
            justify-content: center;
            background: linear-gradient(135deg, var(--accent) 0%, var(--accent-hover) 100%);
            border: none;
            color: #ffffff;
            padding: 14px 28px;
            border-radius: 14px;
            font-weight: 500;
            font-size: 15px;
            cursor: pointer;
            transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
            box-shadow: 0 4px 14px var(--accent-shadow);
            font-family: inherit;
            width: 100%;
            text-decoration: none;
            outline: none;
        }}

        .btn:hover {{
            transform: translateY(-2px);
            box-shadow: 0 6px 20px var(--accent-shadow), 0 0 0 2px rgba(255, 255, 255, 0.15);
        }}

        .btn:active {{
            transform: translateY(1px);
        }}

        .note {{
            font-size: 12px;
            color: #71717a;
            display: block;
            margin-top: 18px;
            line-height: 1.4;
            font-weight: 400;
        }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""logo-container"">
            <div class=""logo-icon"" title=""Chronos Widgets"">
                <svg width=""22"" height=""22"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                    <circle cx=""12"" cy=""12"" r=""10"" stroke=""rgba(255,255,255,0.7)"" />
                    <polyline points=""12 6 12 12 16 14"" stroke=""{clockStrokeColor}"" stroke-width=""2.5"" />
                </svg>
            </div>
            <div class=""connector"">
                {pulseMarkup}
            </div>
            <div class=""logo-icon"" title=""Google"">
                <svg width=""18"" height=""18"" viewBox=""0 0 24 24"">
                    <path fill=""#4285F4"" d=""M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z""/>
                    <path fill=""#34A853"" d=""M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z""/>
                    <path fill=""#FBBC05"" d=""M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.06H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.94l2.85-2.22.81-.63z""/>
                    <path fill=""#EA4335"" d=""M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52z""/>
                </svg>
            </div>
        </div>
        
        <div class=""status-icon-wrapper"">
            {statusIconMarkup}
        </div>

        <h2>{header}</h2>
        <p>{description}</p>
        
        <button class=""btn"" onclick=""window.close()"">Close Tab</button>
        <span class=""note"">If this tab doesn't close automatically, you can safely close it manually.</span>
    </div>
</body>
</html>";
        }

        /// <summary>
        /// Exchanges a PKCE authorization code for a Google id_token via the token endpoint.
        /// </summary>
        private async Task<string?> ExchangeCodeForIdTokenAsync(string code, string codeVerifier, string redirectUri)
        {
            try
            {
                var body = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["code"]          = code,
                    ["client_id"]     = GoogleClientId,
                    ["client_secret"] = GoogleClientSecret,
                    ["redirect_uri"]  = redirectUri,
                    ["grant_type"]    = "authorization_code",
                    ["code_verifier"] = codeVerifier
                };

                var content = new FormUrlEncodedContent(body);
                var resp = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content);
                string json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    LogHelper.Log($"[FirebaseSync] Code exchange failed: {resp.StatusCode} - {json}");
                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                string? idToken = doc.RootElement.GetProperty("id_token").GetString();
                LogHelper.Log($"[FirebaseSync] Code exchange succeeded. id_token received: {!string.IsNullOrEmpty(idToken)}");
                return idToken;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Exception in code exchange: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> ExchangeGoogleTokenForFirebaseAsync(string googleIdToken)
        {
            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={FirebaseApiKey}";
            var payload = new
            {
                postBody = $"id_token={googleIdToken}&providerId=google.com",
                requestUri = "http://localhost",
                returnIdpCredential = true,
                returnSecureToken = true
            };
            
            try
            {
                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] Firebase IDP sign-in failed: {response.StatusCode} - {err}");
                    SyncStatusChanged?.Invoke("Firebase connection failed.");
                    return false;
                }
                
                string resJson = await response.Content.ReadAsStringAsync();
                return await ProcessFirebaseLoginResponseAsync(resJson);
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Exception exchanging Google token: {ex.Message}");
                SyncStatusChanged?.Invoke($"Exchange error: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> RefreshAccessTokenAsync()
        {
            string refreshToken = WidgetConfig.Current.GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken))
            {
                IsLoggedIn = false;
                LoginStatusChanged?.Invoke(false);
                return null;
            }
            
            string url = $"https://securetoken.googleapis.com/v1/token?key={FirebaseApiKey}";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            });
            
            try
            {
                var response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] Token refresh failed: {response.StatusCode} - {err}");
                    
                    if (response.StatusCode == HttpStatusCode.BadRequest && err.Contains("INVALID_REFRESH_TOKEN"))
                    {
                        SignOut();
                    }
                    return null;
                }
                
                string resJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resJson);
                var root = doc.RootElement;
                
                string newIdToken = root.GetProperty("access_token").GetString() ?? "";
                string newRefreshToken = root.GetProperty("refresh_token").GetString() ?? "";
                
                if (!string.IsNullOrEmpty(newRefreshToken) && newRefreshToken != refreshToken)
                {
                    WidgetConfig.Current.SaveRefreshToken(newRefreshToken);
                    WidgetConfig.Save();
                }
                
                _idToken = newIdToken;
                IsLoggedIn = true;
                return newIdToken;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Exception refreshing token: {ex.Message}");
                return null;
            }
        }

        public async Task SyncNowAsync()
        {
            if (string.IsNullOrEmpty(WidgetConfig.Current.CalendarUserId))
            {
                SyncStatusChanged?.Invoke("Not signed in.");
                return;
            }
            
            SyncStatusChanged?.Invoke("Syncing...");
            
            try
            {
                string? idToken = await RefreshAccessTokenAsync();
                if (string.IsNullOrEmpty(idToken))
                {
                    SyncStatusChanged?.Invoke("Sync failed: Auth error.");
                    return;
                }
                
                string userId = WidgetConfig.Current.CalendarUserId;
                
                DateTime localNow = DateTime.Now;
                DateTime todayUtc = localNow.Date;
                
                var datesToFetch = new List<string>
                {
                    todayUtc.AddDays(-1).ToString("yyyy-MM-dd"),
                    todayUtc.ToString("yyyy-MM-dd"),
                    todayUtc.AddDays(1).ToString("yyyy-MM-dd"),
                    todayUtc.AddDays(2).ToString("yyyy-MM-dd")
                };
                
                var allEvents = new List<CalendarEvent>();
                
                // 1. Fetch Concrete Firestore Events
                foreach (var dateStr in datesToFetch)
                {
                    string url = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/calendar/{dateStr}";
                    
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                    
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var parsed = ParseFirestoreEvents(json);
                        allEvents.AddRange(parsed);
                    }
                }

                // 2. Fetch and Project Recurring Event Rules
                try
                {
                    string rulesUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/recurring_events";
                    var request = new HttpRequestMessage(HttpMethod.Get, rulesUrl);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                    
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var rules = ParseRecurringRules(json);
                        
                        // Project rules onto yesterday, today, tomorrow
                        foreach (var rule in rules)
                        {
                            if (!rule.Active) continue;
                            
                            foreach (var dateStr in datesToFetch)
                            {
                                DateTime day = DateTime.Parse(dateStr);
                                long dayStartTime = new DateTimeOffset(day.Date).ToUnixTimeMilliseconds();
                                
                                if (dayStartTime < rule.CreatedAt) continue;
                                
                                int dayOfWeek = (int)day.DayOfWeek; // DayOfWeek matches 0=Sunday, 6=Saturday
                                int dayOfMonth = day.Day;
                                
                                bool matches = false;
                                if (rule.Frequency == "daily")
                                {
                                    matches = true;
                                }
                                else if (rule.Frequency == "weekly" || rule.Frequency == "custom_days")
                                {
                                    matches = rule.FrequencyValue.Contains(dayOfWeek);
                                }
                                else if (rule.Frequency == "monthly")
                                {
                                    matches = rule.FrequencyValue.Contains(dayOfMonth);
                                }
                                
                                if (!matches) continue;
                                
                                // Check if override exists in database
                                bool hasConcreteInstance = allEvents.Exists(evt => 
                                    evt.RecurringRuleId == rule.Id && 
                                    (evt.OriginalDate == dateStr || string.IsNullOrEmpty(evt.OriginalDate))
                                );
                                
                                if (!hasConcreteInstance)
                                {
                                    allEvents.Add(new CalendarEvent
                                    {
                                        Id = $"virtual_rec_{rule.Id}_{dateStr}",
                                        Title = rule.Title,
                                        StartDate = dateStr,
                                        StartTime = rule.StartTime,
                                        Duration = rule.Duration,
                                        Color = rule.Color,
                                        Date = dateStr,
                                        RecurringRuleId = rule.Id,
                                        OriginalDate = dateStr
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[FirebaseSync] Error fetching/projecting recurring rules: {ex.Message}");
                }

                // 3. Fetch Google Calendar Events via Locally Stored Integration Tokens
                try
                {
                    foreach (var acc in WidgetConfig.Current.GoogleAccounts)
                    {
                        string googleRefreshToken = acc.GetRefreshToken();
                        if (string.IsNullOrEmpty(googleRefreshToken)) continue;
                        
                        string? accToken = await RefreshGoogleAccessTokenAsync(acc.Email, googleRefreshToken);
                        if (string.IsNullOrEmpty(accToken)) continue;
                        
                        var gEvents = await FetchGoogleEventsAsync(accToken!, localNow.Date, localNow.Date.AddDays(2));
                        foreach (var gev in gEvents)
                        {
                            gev.Id = $"gcal-{acc.Email}_{gev.Id}";
                            allEvents.Add(gev);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[FirebaseSync] Error syncing Google Calendar accounts locally: {ex.Message}");
                }
                
                DateTime todayStart = localNow.Date;
                DateTime tomorrowEnd = todayStart.AddDays(2).AddTicks(-1);
                
                var filteredEvents = new List<CalendarEvent>();
                var uniqueIds = new HashSet<string>();
                
                foreach (var ev in allEvents)
                {
                    if (uniqueIds.Contains(ev.Id)) continue;
                    if (ev.Status == "deleted") continue; // Filter out tombstones
                    
                    DateTime eventStartLocal;
                    DateTime eventEndLocal;
                    
                    if (string.IsNullOrEmpty(ev.StartTime))
                    {
                        if (DateTime.TryParse(ev.StartDate, out var parsedDate))
                        {
                            eventStartLocal = parsedDate.Date;
                            eventEndLocal = eventStartLocal.AddDays(1).AddTicks(-1);
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        bool isVirtual = ev.Id.StartsWith("virtual_rec_");
                        
                        if (isVirtual)
                        {
                            // Virtual recurring events are already created in local time, do not treat as UTC
                            if (DateTime.TryParse($"{ev.StartDate}T{ev.StartTime}:00", null, System.Globalization.DateTimeStyles.None, out var parsedLocal))
                            {
                                eventStartLocal = parsedLocal;
                                eventEndLocal = eventStartLocal.AddMinutes(ev.Duration);
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // Firestore database events and Google Calendar API events are UTC
                            if (DateTime.TryParse($"{ev.StartDate}T{ev.StartTime}:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var eventStartUtc))
                            {
                                eventStartLocal = eventStartUtc.ToLocalTime();
                                eventEndLocal = eventStartLocal.AddMinutes(ev.Duration);
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                    
                    if (eventStartLocal < tomorrowEnd && eventEndLocal > todayStart)
                    {
                        ev.StartDate = eventStartLocal.ToString("yyyy-MM-dd");
                        ev.StartTime = string.IsNullOrEmpty(ev.StartTime) ? "" : eventStartLocal.ToString("HH:mm");
                        
                        filteredEvents.Add(ev);
                        uniqueIds.Add(ev.Id);
                    }
                }
                
                filteredEvents.Sort((a, b) =>
                {
                    if (string.IsNullOrEmpty(a.StartTime) && string.IsNullOrEmpty(b.StartTime)) return 0;
                    if (string.IsNullOrEmpty(a.StartTime)) return -1;
                    if (string.IsNullOrEmpty(b.StartTime)) return 1;
                    return a.StartTime.CompareTo(b.StartTime);
                });
                
                string serializedCache = JsonSerializer.Serialize(filteredEvents);
                WidgetConfig.Current.CalendarEventsCache = serializedCache;
                WidgetConfig.Save();
                
                EventsUpdated?.Invoke(filteredEvents);

                // Sync Tasks
                await SyncTasksAsync(idToken, userId);

                SyncStatusChanged?.Invoke($"Synced: {DateTime.Now:h:mm tt}");
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Exception during SyncNow: {ex.Message}");
                SyncStatusChanged?.Invoke($"Sync error: {ex.Message}");
            }
        }

        private async Task<string?> RefreshGoogleAccessTokenAsync(string email, string refreshToken)
        {
            try
            {
                // Check if we have a valid cached token
                if (_googleTokenCache.TryGetValue(refreshToken, out var cached) && cached.ExpirationTime > DateTime.UtcNow.AddMinutes(5))
                {
                    return cached.AccessToken;
                }

                var body = new Dictionary<string, string>
                {
                    ["client_id"] = GoogleClientId,
                    ["client_secret"] = GoogleClientSecret,
                    ["refresh_token"] = refreshToken,
                    ["grant_type"] = "refresh_token"
                };

                var content = new FormUrlEncodedContent(body);
                var resp = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content);
                string resContent = await resp.Content.ReadAsStringAsync();
                
                if (!resp.IsSuccessStatusCode)
                {
                    LogHelper.Log($"[FirebaseSync] Google token refresh locally failed: {resp.StatusCode} - {resContent}");
                    
                    // Fallback to Firebase Cloud Function if unauthorized_client (cross-client token)
                    if (resContent.Contains("unauthorized_client") || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        LogHelper.Log($"[FirebaseSync] Attempting Cloud Function token refresh fallback for {email}...");
                        string? cfToken = await RefreshViaCloudFunctionAsync(email);
                        if (!string.IsNullOrEmpty(cfToken))
                        {
                            // Cache the cloud function token for 50 minutes (3000 seconds)
                            _googleTokenCache[refreshToken] = new GoogleTokenCache
                            {
                                AccessToken = cfToken,
                                ExpirationTime = DateTime.UtcNow.AddMinutes(50)
                            };
                            return cfToken;
                        }
                    }
                    return null;
                }

                using var doc = JsonDocument.Parse(resContent);
                var root = doc.RootElement;
                string accessToken = root.GetProperty("access_token").GetString() ?? "";
                
                int expiresIn = 3600; // Default Google token lifespan (1 hour)
                if (root.TryGetProperty("expires_in", out var expiresProp))
                {
                    expiresIn = expiresProp.GetInt32();
                }

                // Cache the token
                _googleTokenCache[refreshToken] = new GoogleTokenCache
                {
                    AccessToken = accessToken,
                    ExpirationTime = DateTime.UtcNow.AddSeconds(expiresIn)
                };

                LogHelper.Log($"[FirebaseSync] Refreshed and cached Google access token for 60 mins.");
                return accessToken;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Exception refreshing Google access token: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> RefreshViaCloudFunctionAsync(string email)
        {
            try
            {
                string? idToken = await RefreshAccessTokenAsync();
                if (string.IsNullOrEmpty(idToken)) return null;

                var payload = new { data = new { email = email } };
                string jsonPayload = JsonSerializer.Serialize(payload);

                var req = new HttpRequestMessage(HttpMethod.Post, "https://us-central1-chronos-9a892.cloudfunctions.net/refreshGoogleToken");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                req.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    string errStr = await resp.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] Cloud function token refresh HTTP error: {resp.StatusCode} - {errStr}");
                    return null;
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("result", out var resultProp) &&
                    resultProp.TryGetProperty("accessToken", out var tokenProp))
                {
                    string token = tokenProp.GetString() ?? "";
                    LogHelper.Log($"[FirebaseSync] Cloud function token refresh success for {email}.");
                    return token;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Cloud function token refresh exception: {ex.Message}");
            }
            return null;
        }

        private async Task<List<CalendarEvent>> FetchGoogleEventsAsync(string accessToken, DateTime start, DateTime end)
        {
            var list = new List<CalendarEvent>();
            try
            {
                string timeMin = Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                string timeMax = Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                string url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events?timeMin={timeMin}&timeMax={timeMax}&singleEvents=true&orderBy=startTime";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var resp = await _httpClient.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] Google Calendar fetch failed: {resp.StatusCode} - {err}");
                    return list;
                }

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("items", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var ev = new CalendarEvent();
                        if (item.TryGetProperty("id", out var idProp)) ev.Id = idProp.GetString() ?? "";
                        if (item.TryGetProperty("summary", out var sumProp)) ev.Title = sumProp.GetString() ?? "No Title";
                        ev.Color = "#4285F4"; // Google blue default

                        if (item.TryGetProperty("start", out var startProp) && item.TryGetProperty("end", out var endProp))
                        {
                            string startVal = startProp.TryGetProperty("dateTime", out var sDt) ? sDt.GetString() ?? "" : (startProp.TryGetProperty("date", out var sD) ? sD.GetString() ?? "" : "");
                            string endVal = endProp.TryGetProperty("dateTime", out var eDt) ? eDt.GetString() ?? "" : (endProp.TryGetProperty("date", out var eD) ? eD.GetString() ?? "" : "");

                            if (DateTime.TryParse(startVal, out var startTime) && DateTime.TryParse(endVal, out var endTime))
                            {
                                if (startVal.Contains("T"))
                                {
                                    var startTimeUtc = startTime.ToUniversalTime();
                                    ev.StartDate = startTimeUtc.ToString("yyyy-MM-dd");
                                    ev.StartTime = startTimeUtc.ToString("HH:mm");
                                    ev.Duration = (int)(endTime - startTime).TotalMinutes;
                                }
                                else
                                {
                                    ev.StartDate = startTime.ToString("yyyy-MM-dd");
                                    ev.StartTime = "";
                                    ev.Duration = 1440; // All day
                                }
                            }
                        }
                        
                        list.Add(ev);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Exception fetching Google events: {ex.Message}");
            }
            return list;
        }

        private class GoogleAccountInfo
        {
            public string Email { get; set; } = "";
            public string RefreshToken { get; set; } = "";
        }

        private List<GoogleAccountInfo> ParseGoogleAccounts(string json)
        {
            var list = new List<GoogleAccountInfo>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("fields", out var fields) &&
                    fields.TryGetProperty("accounts", out var accsProp) &&
                    accsProp.TryGetProperty("arrayValue", out var arrayValue) &&
                    arrayValue.TryGetProperty("values", out var values))
                {
                    foreach (var val in values.EnumerateArray())
                    {
                        if (val.TryGetProperty("mapValue", out var mapVal) &&
                            mapVal.TryGetProperty("fields", out var accFields))
                        {
                            var acc = new GoogleAccountInfo();
                            if (accFields.TryGetProperty("email", out var eProp) && eProp.TryGetProperty("stringValue", out var eVal))
                                acc.Email = eVal.GetString() ?? "";
                            if (accFields.TryGetProperty("refreshToken", out var rtProp) && rtProp.TryGetProperty("stringValue", out var rtVal))
                                acc.RefreshToken = rtVal.GetString() ?? "";
                            list.Add(acc);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Error parsing Google accounts doc: {ex.Message}");
            }
            return list;
        }

        private class RecurringRuleInfo
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public bool Active { get; set; } = true;
            public string Frequency { get; set; } = "";
            public List<int> FrequencyValue { get; set; } = new List<int>();
            public string StartTime { get; set; } = "";
            public int Duration { get; set; } = 60;
            public string Color { get; set; } = "";
            public long CreatedAt { get; set; } = 0;
        }

        private List<RecurringRuleInfo> ParseRecurringRules(string json)
        {
            var list = new List<RecurringRuleInfo>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("documents", out var docs))
                {
                    foreach (var d in docs.EnumerateArray())
                    {
                        if (d.TryGetProperty("fields", out var fields) && d.TryGetProperty("name", out var nameProp))
                        {
                            var rule = new RecurringRuleInfo();
                            string fullPath = nameProp.GetString() ?? "";
                            rule.Id = fullPath.Substring(fullPath.LastIndexOf('/') + 1);

                            if (fields.TryGetProperty("title", out var tProp) && tProp.TryGetProperty("stringValue", out var tVal))
                                rule.Title = tVal.GetString() ?? "";
                            if (fields.TryGetProperty("active", out var actProp) && actProp.TryGetProperty("booleanValue", out var actVal))
                                rule.Active = actVal.GetBoolean();
                            if (fields.TryGetProperty("frequency", out var freqProp) && freqProp.TryGetProperty("stringValue", out var freqVal))
                                rule.Frequency = freqVal.GetString() ?? "";
                            if (fields.TryGetProperty("startTime", out var stProp) && stProp.TryGetProperty("stringValue", out var stVal))
                                rule.StartTime = stVal.GetString() ?? "";
                            if (fields.TryGetProperty("color", out var colProp) && colProp.TryGetProperty("stringValue", out var colVal))
                                rule.Color = colVal.GetString() ?? "";

                            if (fields.TryGetProperty("duration", out var durProp))
                            {
                                if (durProp.TryGetProperty("integerValue", out var durValStr) && int.TryParse(durValStr.GetString(), out int durInt))
                                    rule.Duration = durInt;
                                else if (durProp.TryGetProperty("doubleValue", out var durValDouble))
                                    rule.Duration = (int)durValDouble.GetDouble();
                            }

                            if (fields.TryGetProperty("createdAt", out var crProp))
                            {
                                if (crProp.TryGetProperty("integerValue", out var crValStr) && long.TryParse(crValStr.GetString(), out long crInt))
                                    rule.CreatedAt = crInt;
                                else if (crProp.TryGetProperty("doubleValue", out var crValDouble))
                                    rule.CreatedAt = (long)crValDouble.GetDouble();
                            }

                            if (fields.TryGetProperty("frequencyValue", out var fvProp) &&
                                fvProp.TryGetProperty("arrayValue", out var arrVal) &&
                                arrVal.TryGetProperty("values", out var vals))
                            {
                                foreach (var val in vals.EnumerateArray())
                                {
                                    if (val.TryGetProperty("integerValue", out var vStr) && int.TryParse(vStr.GetString(), out int vInt))
                                        rule.FrequencyValue.Add(vInt);
                                }
                            }

                            list.Add(rule);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Error parsing recurring rules: {ex.Message}");
            }
            return list;
        }

        public static List<CalendarEvent> ParseFirestoreEvents(string json)
        {
            var list = new List<CalendarEvent>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("fields", out var fields) &&
                    fields.TryGetProperty("events", out var eventsProp) &&
                    eventsProp.TryGetProperty("arrayValue", out var arrayValue) &&
                    arrayValue.TryGetProperty("values", out var values))
                {
                    foreach (var val in values.EnumerateArray())
                    {
                        if (val.TryGetProperty("mapValue", out var mapValue) &&
                            mapValue.TryGetProperty("fields", out var eventFields))
                        {
                            var ev = new CalendarEvent();
                            
                            if (eventFields.TryGetProperty("id", out var idProp) && idProp.TryGetProperty("stringValue", out var idVal))
                                ev.Id = idVal.GetString() ?? "";
                                
                            if (eventFields.TryGetProperty("title", out var titleProp) && titleProp.TryGetProperty("stringValue", out var titleVal))
                                ev.Title = titleVal.GetString() ?? "";
                                
                            if (eventFields.TryGetProperty("startDate", out var sdProp) && sdProp.TryGetProperty("stringValue", out var sdVal))
                                ev.StartDate = sdVal.GetString() ?? "";
                                
                            if (eventFields.TryGetProperty("startTime", out var stProp) && stProp.TryGetProperty("stringValue", out var stVal))
                                ev.StartTime = stVal.GetString() ?? "";
                                
                            if (eventFields.TryGetProperty("duration", out var durProp))
                            {
                                if (durProp.TryGetProperty("integerValue", out var durValStr) && int.TryParse(durValStr.GetString(), out int durInt))
                                    ev.Duration = durInt;
                                else if (durProp.TryGetProperty("doubleValue", out var durValDouble))
                                    ev.Duration = (int)durValDouble.GetDouble();
                            }
                            
                            if (eventFields.TryGetProperty("color", out var colProp) && colProp.TryGetProperty("stringValue", out var colVal))
                                ev.Color = colVal.GetString() ?? "";
                                
                            if (eventFields.TryGetProperty("date", out var dProp) && dProp.TryGetProperty("stringValue", out var dVal))
                                ev.Date = dVal.GetString() ?? "";

                            if (eventFields.TryGetProperty("recurringRuleId", out var recProp) && recProp.TryGetProperty("stringValue", out var recVal))
                                ev.RecurringRuleId = recVal.GetString() ?? "";

                            if (eventFields.TryGetProperty("originalDate", out var origProp) && origProp.TryGetProperty("stringValue", out var origVal))
                                ev.OriginalDate = origVal.GetString() ?? "";

                            if (eventFields.TryGetProperty("status", out var statProp) && statProp.TryGetProperty("stringValue", out var statVal))
                                ev.Status = statVal.GetString() ?? "";
                                
                            list.Add(ev);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Parser] Error parsing events: {ex.Message}");
            }
            return list;
        }

        public async Task<List<string>> FetchGoogleAccountsListAsync()
        {
            var emails = new List<string>();
            try
            {
                // First try to load from local storage for fast access
                foreach (var acc in WidgetConfig.Current.GoogleAccounts)
                {
                    emails.Add(acc.Email);
                }

                // Sync from Firestore to fetch any new connections made from mobile/web
                string? idToken = await RefreshAccessTokenAsync();
                if (!string.IsNullOrEmpty(idToken))
                {
                    string userId = WidgetConfig.Current.CalendarUserId;
                    string integrationUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/integrations/google_calendar";
                    
                    var request = new HttpRequestMessage(HttpMethod.Get, integrationUrl);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                    
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var accounts = ParseGoogleAccounts(json);
                        
                        bool hasChanges = false;
                        foreach (var acc in accounts)
                        {
                            if (!emails.Contains(acc.Email))
                            {
                                emails.Add(acc.Email);
                                var newLocal = new GoogleAccountConfig { Email = acc.Email };
                                newLocal.SaveRefreshToken(acc.RefreshToken);
                                WidgetConfig.Current.GoogleAccounts.Add(newLocal);
                                hasChanges = true;
                            }
                        }
                        if (hasChanges)
                        {
                            WidgetConfig.Save();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Error fetching/syncing Google accounts: {ex.Message}");
            }
            return emails;
        }

        public async Task<bool> ConnectGoogleCalendarAccountAsync()
        {
            int port = 5006;
            string redirectUri = $"http://localhost:{port}/";

            // PKCE
            byte[] verifierBytes = new byte[64];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(verifierBytes);
            }
            string codeVerifier = Convert.ToBase64String(verifierBytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");

            byte[] challengeBytes;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                challengeBytes = sha256.ComputeHash(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
            }
            string codeChallenge = Convert.ToBase64String(challengeBytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");

            string state = Guid.NewGuid().ToString("N");

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);

            try
            {
                listener.Start();
                LogHelper.Log($"[FirebaseSync] Starting secondary Google OAuth listener for Calendar Integration on {redirectUri}");

                // Request offline access to get a refresh_token, and request both userInfo and Calendar events scopes
                string googleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
                    $"?client_id={GoogleClientId}" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    "&response_type=code" +
                    "&scope=openid%20email%20profile%20https://www.googleapis.com/auth/calendar.events.readonly" +
                    $"&state={state}" +
                    "&code_challenge_method=S256" +
                    $"&code_challenge={codeChallenge}" +
                    "&access_type=offline" +
                    "&prompt=consent";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(googleAuthUrl)
                {
                    UseShellExecute = true
                });

                SyncStatusChanged?.Invoke("Waiting for Google Calendar authorization in browser...");

                var context = await listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                string? authCode = request.QueryString["code"];
                string? returnedState = request.QueryString["state"];

                bool codeReceived = !string.IsNullOrEmpty(authCode) && returnedState == state;
                string htmlPage = codeReceived
                    ? GetOAuthCallbackHtml(true, "Connection Successful", "Account Connected!", "Google Calendar has been successfully linked. You can close this tab now.")
                    : GetOAuthCallbackHtml(false, "Connection Failed", "Connection Failed", "Invalid code or authorization rejected. Please try again.");

                byte[] htmlBytes = System.Text.Encoding.UTF8.GetBytes(htmlPage);
                response.ContentType = "text/html";
                response.ContentLength64 = htmlBytes.Length;
                await response.OutputStream.WriteAsync(htmlBytes, 0, htmlBytes.Length);
                response.OutputStream.Close();
                response.Close();

                if (!codeReceived) return false;

                // Exchange code for tokens
                SyncStatusChanged?.Invoke("Exchanging auth code...");
                var tokens = await ExchangeCodeForTokensAsync(authCode!, codeVerifier, redirectUri);
                if (tokens == null || string.IsNullOrEmpty(tokens.RefreshToken))
                {
                    SyncStatusChanged?.Invoke("Link failed: no refresh token returned (make sure to grant access offline).");
                    return false;
                }

                // Get User Info to find email
                string? email = await FetchGoogleUserEmailAsync(tokens.AccessToken);
                if (string.IsNullOrEmpty(email))
                {
                    SyncStatusChanged?.Invoke("Link failed: could not fetch account email.");
                    return false;
                }

                // Save locally first
                WidgetConfig.Current.GoogleAccounts.RemoveAll(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                var newAcc = new GoogleAccountConfig { Email = email };
                newAcc.SaveRefreshToken(tokens.RefreshToken);
                WidgetConfig.Current.GoogleAccounts.Add(newAcc);
                WidgetConfig.Save();

                // Save to Firestore
                string? idToken = await RefreshAccessTokenAsync();
                if (!string.IsNullOrEmpty(idToken))
                {
                    string userId = WidgetConfig.Current.CalendarUserId;
                    string integrationUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/integrations/google_calendar";
                    
                    var getReq = new HttpRequestMessage(HttpMethod.Get, integrationUrl);
                    getReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                    var getResp = await _httpClient.SendAsync(getReq);

                    var accountsList = new List<GoogleAccountInfo>();
                    if (getResp.IsSuccessStatusCode)
                    {
                        accountsList = ParseGoogleAccounts(await getResp.Content.ReadAsStringAsync());
                    }
                    accountsList.RemoveAll(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                    accountsList.Add(new GoogleAccountInfo { Email = email, RefreshToken = tokens.RefreshToken });

                    await SaveGoogleAccountsToFirestoreAsync(idToken, userId, accountsList);
                }

                SyncStatusChanged?.Invoke("Google account connected successfully!");
                _ = SyncNowAsync();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Google Calendar link exception: {ex.Message}");
                SyncStatusChanged?.Invoke($"Link failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
        }

        public async Task<bool> DisconnectGoogleCalendarAccountAsync(string email)
        {
            try
            {
                // Remove locally
                WidgetConfig.Current.GoogleAccounts.RemoveAll(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                WidgetConfig.Save();

                // Remove from Firestore
                string? idToken = await RefreshAccessTokenAsync();
                if (!string.IsNullOrEmpty(idToken))
                {
                    string userId = WidgetConfig.Current.CalendarUserId;
                    string integrationUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/integrations/google_calendar";
                    
                    var getReq = new HttpRequestMessage(HttpMethod.Get, integrationUrl);
                    getReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                    var getResp = await _httpClient.SendAsync(getReq);
                    if (getResp.IsSuccessStatusCode)
                    {
                        var accountsList = ParseGoogleAccounts(await getResp.Content.ReadAsStringAsync());
                        accountsList.RemoveAll(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                        await SaveGoogleAccountsToFirestoreAsync(idToken, userId, accountsList);
                    }
                }

                SyncStatusChanged?.Invoke("Google account disconnected.");
                _ = SyncNowAsync();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Disconnect integration exception: {ex.Message}");
            }
            return false;
        }

        private class GoogleTokensResponse
        {
            public string AccessToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
        }

        private async Task<GoogleTokensResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier, string redirectUri)
        {
            try
            {
                var body = new Dictionary<string, string>
                {
                    ["code"]          = code,
                    ["client_id"]     = GoogleClientId,
                    ["client_secret"] = GoogleClientSecret,
                    ["redirect_uri"]  = redirectUri,
                    ["grant_type"]    = "authorization_code",
                    ["code_verifier"] = codeVerifier
                };

                var content = new FormUrlEncodedContent(body);
                var resp = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content);
                string resStr = await resp.Content.ReadAsStringAsync();
                
                if (!resp.IsSuccessStatusCode)
                {
                    LogHelper.Log($"[FirebaseSync] ExchangeCodeForTokens failed status={resp.StatusCode} body={resStr}");
                    return null;
                }

                using var doc = JsonDocument.Parse(resStr);
                var root = doc.RootElement;
                
                // Print whether refresh token was actually received
                string rt = root.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() ?? "" : "";
                LogHelper.Log($"[FirebaseSync] ExchangeCodeForTokens success. Has refresh token: {!string.IsNullOrEmpty(rt)}");

                return new GoogleTokensResponse
                {
                    AccessToken = root.GetProperty("access_token").GetString() ?? "",
                    RefreshToken = rt
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> FetchGoogleUserEmailAsync(string accessToken)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("email").GetString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> SaveGoogleAccountsToFirestoreAsync(string idToken, string userId, List<GoogleAccountInfo> accounts)
        {
            try
            {
                string url = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/integrations/google_calendar?updateMask.fieldPaths=accounts";

                // Format structure matching the Firestore arrayValue fields
                var accountValuesList = new List<object>();
                foreach (var a in accounts)
                {
                    accountValuesList.Add(new
                    {
                        mapValue = new
                        {
                            fields = new
                            {
                                email = new { stringValue = a.Email },
                                refreshToken = new { stringValue = a.RefreshToken },
                                addedAt = new { integerValue = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
                            }
                        }
                    });
                }

                var payload = new
                {
                    fields = new
                    {
                        accounts = new
                        {
                            arrayValue = new
                            {
                                values = accountValuesList
                            }
                        }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                request.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] SaveGoogleAccountsToFirestoreAsync PATCH failed status={response.StatusCode} body={errBody}");
                }
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Failed to save Google integrations to Firestore: {ex.Message}");
                return false;
            }
        }

        private async Task SyncTasksAsync(string idToken, string userId)
        {
            try
            {
                LogHelper.Log("[FirebaseSync] SyncTasks started.");

                // 1. Fetch Focus Settings to get the active view ID from mobile
                string focusSettingsUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/preferences/focusSettings";
                var focusReq = new HttpRequestMessage(HttpMethod.Get, focusSettingsUrl);
                focusReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                var focusResp = await _httpClient.SendAsync(focusReq);
                string? activeViewId = null;

                if (focusResp.IsSuccessStatusCode)
                {
                    string focusJson = await focusResp.Content.ReadAsStringAsync();
                    using var focusDoc = JsonDocument.Parse(focusJson);
                    if (focusDoc.RootElement.TryGetProperty("fields", out var fields))
                    {
                        if (fields.TryGetProperty("activeViewId", out var avProp) && avProp.TryGetProperty("stringValue", out var avVal))
                            activeViewId = avVal.GetString();
                    }
                    LogHelper.Log($"[FirebaseSync] focusSettings activeViewId from mobile: '{activeViewId}'");
                }
                else
                {
                    LogHelper.Log($"[FirebaseSync] focusSettings fetch status code: {focusResp.StatusCode}");
                }

                // 1b. Fetch all custom views to populate settings options & resolve settings
                var customViews = new List<CustomViewItem>();
                try
                {
                    string customViewsUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/preferences/focusSettings/customViews";
                    var viewsReq = new HttpRequestMessage(HttpMethod.Get, customViewsUrl);
                    viewsReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                    var viewsResp = await _httpClient.SendAsync(viewsReq);

                    if (viewsResp.IsSuccessStatusCode)
                    {
                        string viewsJson = await viewsResp.Content.ReadAsStringAsync();
                        customViews = ParseFirestoreCustomViews(viewsJson);
                        LogHelper.Log($"[FirebaseSync] Successfully parsed {customViews.Count} custom views from Firestore.");
                        
                        // Cache custom views list for settings dropdown
                        WidgetConfig.Current.TasksCustomViewsCache = JsonSerializer.Serialize(customViews);
                        WidgetConfig.Save();
                    }
                    else
                    {
                        LogHelper.Log($"[FirebaseSync] customViews fetch status code: {viewsResp.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[FirebaseSync] Error fetching custom views list: {ex.Message}");
                }

                // 1c. Resolve settings based on user-selected option in task settings
                string groupOption = "none";
                string sortOption = "dueDate";
                string sortDirection = "asc";
                var filterCategories = new List<string>();
                var filterPriorities = new List<string>();
                var filterStatuses = new List<string>();

                string selectedViewId = WidgetConfig.Current.TasksSelectedViewId;
                string? targetViewId = null;

                if (selectedViewId == "FollowMobile")
                {
                    targetViewId = activeViewId;
                }
                else if (selectedViewId != "Plain")
                {
                    targetViewId = selectedViewId;
                }

                LogHelper.Log($"[FirebaseSync] SelectedViewSetting: '{selectedViewId}' -> Resolved targetViewId: '{targetViewId}'");

                if (!string.IsNullOrEmpty(targetViewId))
                {
                    var targetView = customViews.Find(v => v.Id == targetViewId);
                    if (targetView != null)
                    {
                        groupOption = targetView.GroupOption;
                        sortOption = targetView.SortOption;
                        sortDirection = targetView.SortDirection;
                        filterCategories = targetView.FilterCategories;
                        filterPriorities = targetView.FilterPriorities;
                        filterStatuses = targetView.FilterStatuses;

                        LogHelper.Log($"[FirebaseSync] Applied Custom View settings: group={groupOption}, sort={sortOption} ({sortDirection}), " +
                                     $"filterCategoriesCount={filterCategories.Count}, filterPrioritiesCount={filterPriorities.Count}, filterStatusesCount={filterStatuses.Count}");
                    }
                    else
                    {
                        LogHelper.Log($"[FirebaseSync] Selected view ID '{targetViewId}' not found. Falling back to plain view.");
                    }
                }

                // 2. Fetch Status Mappings
                string statusUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}/preferences/status";
                var statusReq = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                statusReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                var statusResp = await _httpClient.SendAsync(statusReq);
                var statusLabels = new Dictionary<string, string>
                {
                    { "1", "Todo" },
                    { "2", "In Progress" },
                    { "3", "Done" }
                };
                if (statusResp.IsSuccessStatusCode)
                {
                    string statusJson = await statusResp.Content.ReadAsStringAsync();
                    using var statusDoc = JsonDocument.Parse(statusJson);
                    if (statusDoc.RootElement.TryGetProperty("fields", out var fields))
                    {
                        foreach (var prop in fields.EnumerateObject())
                        {
                            if (prop.Value.TryGetProperty("stringValue", out var sv))
                            {
                                statusLabels[prop.Name] = sv.GetString() ?? prop.Name;
                            }
                        }
                    }
                }
                LogHelper.Log($"[FirebaseSync] Status labels mapping resolved: " +
                             $"1='{statusLabels["1"]}', 2='{statusLabels["2"]}', 3='{statusLabels["3"]}'");

                // 3. Fetch Tasks via runQuery (Only active tasks status 1 or 2)
                string tasksUrl = $"https://firestore.googleapis.com/v1/projects/chronos-9a892/databases/(default)/documents/users/{userId}:runQuery";
                var tasksReq = new HttpRequestMessage(HttpMethod.Post, tasksUrl);
                tasksReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
                
                string queryJson = "{\"structuredQuery\":{\"from\":[{\"collectionId\":\"tasks\"}],\"where\":{\"fieldFilter\":{\"field\":{\"fieldPath\":\"status\"},\"op\":\"IN\",\"value\":{\"arrayValue\":{\"values\":[{\"stringValue\":\"1\"},{\"stringValue\":\"2\"}]}}}},\"limit\":200}}";
                tasksReq.Content = new StringContent(queryJson, System.Text.Encoding.UTF8, "application/json");

                var tasksResp = await _httpClient.SendAsync(tasksReq);
                var tasks = new List<TaskItem>();
                if (tasksResp.IsSuccessStatusCode)
                {
                    string tasksJson = await tasksResp.Content.ReadAsStringAsync();
                    tasks = ParseFirestoreTasksRunQuery(tasksJson);
                    LogHelper.Log($"[FirebaseSync] Fetched {tasks.Count} raw active tasks from Firestore using runQuery.");
                }
                else
                {
                    string errContent = await tasksResp.Content.ReadAsStringAsync();
                    LogHelper.Log($"[FirebaseSync] Failed to fetch tasks via runQuery: {tasksResp.StatusCode} - {errContent}");
                }

                // 4. Process Tasks (Filter active tasks - Status "1" or "2")
                var activeStatuses = new HashSet<string> { "1", "2" };
                var activeTasks = tasks.FindAll(t => activeStatuses.Contains(t.Status));
                LogHelper.Log($"[FirebaseSync] Active tasks count (status '1' or '2'): {activeTasks.Count}");

                // Log names and status of active tasks for debugging
                foreach (var at in activeTasks)
                {
                    LogHelper.Log($"  -> Active Task: '{at.Title}' (ID={at.Id}, Status={at.Status}, Priority={at.Priority}, Category={at.Category})");
                }

                bool hasActiveView = !string.IsNullOrEmpty(targetViewId);

                if (hasActiveView)
                {
                    // Apply custom view multi-select filters
                    if (filterCategories.Count > 0)
                    {
                        var lowerCats = filterCategories.ConvertAll(c => c.ToLower());
                        activeTasks = activeTasks.FindAll(t => lowerCats.Contains((t.Category ?? "").ToLower()));
                        LogHelper.Log($"[FirebaseSync] Tasks after category filter (count={filterCategories.Count}): {activeTasks.Count}");
                    }
                    if (filterPriorities.Count > 0)
                    {
                        var lowerPris = filterPriorities.ConvertAll(p => p.ToLower());
                        activeTasks = activeTasks.FindAll(t => lowerPris.Contains((t.Priority ?? "").ToLower()));
                        LogHelper.Log($"[FirebaseSync] Tasks after priority filter (count={filterPriorities.Count}): {activeTasks.Count}");
                    }
                    if (filterStatuses.Count > 0)
                    {
                        activeTasks = activeTasks.FindAll(t => filterStatuses.Contains(t.Status));
                        LogHelper.Log($"[FirebaseSync] Tasks after status filter (count={filterStatuses.Count}): {activeTasks.Count}");
                    }

                    // Sort Tasks
                    activeTasks.Sort((a, b) =>
                    {
                        int comparison = 0;
                        switch (sortOption)
                        {
                            case "dueDate":
                                comparison = a.DueDate.CompareTo(b.DueDate);
                                break;
                            case "dateCreated":
                                comparison = a.CreatedAt.CompareTo(b.CreatedAt);
                                break;
                            case "priority":
                                int aPri = GetPriorityWeight(a.Priority);
                                int bPri = GetPriorityWeight(b.Priority);
                                comparison = aPri.CompareTo(bPri);
                                break;
                            case "alphabetical":
                                comparison = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                                break;
                            case "status":
                                comparison = string.Compare(a.Status, b.Status, StringComparison.Ordinal);
                                break;
                            default:
                                comparison = a.DueDate.CompareTo(b.DueDate);
                                break;
                        }
                        return sortDirection == "asc" ? comparison : -comparison;
                    });
                }
                else
                {
                    // Bypass grouping and sorting when no active custom view
                    groupOption = "none";
                }

                // Group Tasks
                var widgetItems = new List<WidgetTaskItem>();
                if (groupOption == "none")
                {
                    foreach (var task in activeTasks)
                    {
                        widgetItems.Add(ConvertToWidgetTask(task));
                    }
                }
                else
                {
                    var grouped = new Dictionary<string, List<TaskItem>>();
                    var groupOrder = new List<string>();

                    foreach (var task in activeTasks)
                    {
                        string groupKey = "Uncategorized";
                        switch (groupOption)
                        {
                            case "priority":
                                string pri = string.IsNullOrEmpty(task.Priority) ? "Low" : task.Priority;
                                groupKey = char.ToUpper(pri[0]) + pri.Substring(1).ToLower() + " Priority";
                                break;
                            case "category":
                                groupKey = string.IsNullOrEmpty(task.Category) ? "Uncategorized" : task.Category;
                                break;
                            case "status":
                                if (statusLabels.TryGetValue(task.Status, out var lbl))
                                    groupKey = lbl;
                                else
                                    groupKey = task.Status;
                                break;
                            case "date":
                                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                var today = DateTime.Today;
                                long todayStartMs = new DateTimeOffset(today).ToUnixTimeMilliseconds();
                                long tomorrowStartMs = new DateTimeOffset(today.AddDays(1)).ToUnixTimeMilliseconds();
                                long nextWeekStartMs = new DateTimeOffset(today.AddDays(7)).ToUnixTimeMilliseconds();

                                if (task.DueDate < todayStartMs) groupKey = "Overdue";
                                else if (task.DueDate < tomorrowStartMs) groupKey = "Today";
                                else if (task.DueDate < nextWeekStartMs) groupKey = "This Week";
                                else groupKey = "Later";
                                break;
                        }

                        if (!grouped.ContainsKey(groupKey))
                        {
                            grouped[groupKey] = new List<TaskItem>();
                            groupOrder.Add(groupKey);
                        }
                        grouped[groupKey].Add(task);
                    }

                    // For 'date' grouping, apply standard sorting order
                    if (groupOption == "date")
                    {
                        var standardOrder = new List<string> { "Overdue", "Today", "This Week", "Later" };
                        groupOrder.Clear();
                        foreach (var key in standardOrder)
                        {
                            if (grouped.ContainsKey(key)) groupOrder.Add(key);
                        }
                        foreach (var key in grouped.Keys)
                        {
                            if (!standardOrder.Contains(key)) groupOrder.Add(key);
                        }
                    }

                    // Add to widget items with header markers
                    foreach (var gName in groupOrder)
                    {
                        widgetItems.Add(new WidgetTaskItem
                        {
                            Type = "Header",
                            Title = $"── {gName} ──"
                        });

                        foreach (var task in grouped[gName])
                        {
                            widgetItems.Add(ConvertToWidgetTask(task));
                        }
                    }
                }

                LogHelper.Log($"[FirebaseSync] Prepared {widgetItems.Count} tasks/headers for the widget list.");

                // Save to Cache
                string serializedCache = JsonSerializer.Serialize(widgetItems);
                WidgetConfig.Current.TasksCache = serializedCache;
                WidgetConfig.Save();

                TasksUpdated?.Invoke(widgetItems);
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Exception during SyncTasks: {ex.Message}");
            }
        }

        private int GetPriorityWeight(string priority)
        {
            if (string.IsNullOrEmpty(priority)) return 0;
            return priority.ToLower() switch
            {
                "high" => 3,
                "medium" => 2,
                "low" => 1,
                _ => 0
            };
        }

        private WidgetTaskItem ConvertToWidgetTask(TaskItem task)
        {
            var widgetTask = new WidgetTaskItem
            {
                Id = task.Id,
                Title = task.Title,
                Type = "Task",
                Priority = task.Priority,
                Status = task.Status,
                Category = task.Category
            };

            // Parse subtasks checklist if present
            if (!string.IsNullOrEmpty(task.Description))
            {
                try
                {
                    using var doc = JsonDocument.Parse(task.Description);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("checklist", out var checklistProp) && checklistProp.ValueKind == JsonValueKind.Array)
                    {
                        int total = 0;
                        int done = 0;
                        foreach (var item in checklistProp.EnumerateArray())
                        {
                            total++;
                            if (item.TryGetProperty("completed", out var compProp) && compProp.GetBoolean())
                            {
                                done++;
                            }
                        }
                        if (total > 0)
                        {
                            widgetTask.ChecklistProgress = $"({done}/{total})";
                        }
                    }
                }
                catch
                {
                    // Ignore JSON parsing exceptions for plain-text descriptions
                }
            }

            return widgetTask;
        }

        private List<CustomViewItem> ParseFirestoreCustomViews(string json)
        {
            var viewsList = new List<CustomViewItem>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("documents", out var docsArr) && docsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var docEl in docsArr.EnumerateArray())
                    {
                        if (docEl.TryGetProperty("fields", out var fields))
                        {
                            var view = new CustomViewItem();
                            
                            if (fields.TryGetProperty("id", out var idProp) && idProp.TryGetProperty("stringValue", out var idVal))
                                view.Id = idVal.GetString() ?? "";
                            if (fields.TryGetProperty("name", out var nameProp) && nameProp.TryGetProperty("stringValue", out var nameVal))
                                view.Name = nameVal.GetString() ?? "";
                            if (fields.TryGetProperty("groupOption", out var goProp) && goProp.TryGetProperty("stringValue", out var goVal))
                                view.GroupOption = goVal.GetString() ?? "none";
                            if (fields.TryGetProperty("sortOption", out var soProp) && soProp.TryGetProperty("stringValue", out var soVal))
                                view.SortOption = soVal.GetString() ?? "dueDate";
                            if (fields.TryGetProperty("sortDirection", out var sdProp) && sdProp.TryGetProperty("stringValue", out var sdVal))
                                view.SortDirection = sdVal.GetString() ?? "asc";

                            if (fields.TryGetProperty("filterCategories", out var fcProp))
                                view.FilterCategories = ParseFirestoreArray(fcProp);
                            if (fields.TryGetProperty("filterPriorities", out var fpProp))
                                view.FilterPriorities = ParseFirestoreArray(fpProp);
                            if (fields.TryGetProperty("filterStatuses", out var fsProp))
                                view.FilterStatuses = ParseFirestoreArray(fsProp);

                            if (!string.IsNullOrEmpty(view.Id))
                            {
                                viewsList.Add(view);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Error parsing Firestore custom views: {ex.Message}");
            }
            return viewsList;
        }

        private List<TaskItem> ParseFirestoreTasks(string json)
        {
            var list = new List<TaskItem>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("documents", out var docs))
                {
                    foreach (var d in docs.EnumerateArray())
                    {
                        if (d.TryGetProperty("fields", out var fields) && d.TryGetProperty("name", out var nameProp))
                        {
                            var task = new TaskItem();
                            string fullPath = nameProp.GetString() ?? "";
                            task.Id = fullPath.Substring(fullPath.LastIndexOf('/') + 1);

                            if (fields.TryGetProperty("title", out var titleProp) && titleProp.TryGetProperty("stringValue", out var titleVal))
                                task.Title = titleVal.GetString() ?? "";

                            if (fields.TryGetProperty("status", out var statProp) && statProp.TryGetProperty("stringValue", out var statVal))
                                task.Status = statVal.GetString() ?? "1";

                            if (fields.TryGetProperty("priority", out var priProp) && priProp.TryGetProperty("stringValue", out var priVal))
                                task.Priority = priVal.GetString() ?? "low";

                            if (fields.TryGetProperty("category", out var catProp) && catProp.TryGetProperty("stringValue", out var catVal))
                                task.Category = catVal.GetString() ?? "";

                            if (fields.TryGetProperty("description", out var descProp) && descProp.TryGetProperty("stringValue", out var descVal))
                                task.Description = descVal.GetString() ?? "";

                            if (fields.TryGetProperty("startDate", out var sdProp))
                            {
                                if (sdProp.TryGetProperty("integerValue", out var sdStr) && long.TryParse(sdStr.GetString(), out long sdInt))
                                    task.StartDate = sdInt;
                                else if (sdProp.TryGetProperty("doubleValue", out var sdDouble))
                                    task.StartDate = (long)sdDouble.GetDouble();
                            }

                            if (fields.TryGetProperty("dueDate", out var ddProp))
                            {
                                if (ddProp.TryGetProperty("integerValue", out var ddStr) && long.TryParse(ddStr.GetString(), out long ddInt))
                                    task.DueDate = ddInt;
                                else if (ddProp.TryGetProperty("doubleValue", out var ddDouble))
                                    task.DueDate = (long)ddDouble.GetDouble();
                            }

                            if (fields.TryGetProperty("createdAt", out var crProp))
                            {
                                if (crProp.TryGetProperty("integerValue", out var crStr) && long.TryParse(crStr.GetString(), out long crInt))
                                    task.CreatedAt = crInt;
                                else if (crProp.TryGetProperty("doubleValue", out var crDouble))
                                    task.CreatedAt = (long)crDouble.GetDouble();
                            }

                            list.Add(task);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Error parsing firestore tasks: {ex.Message}");
            }
            return list;
        }

        private List<TaskItem> ParseFirestoreTasksRunQuery(string json)
        {
            var list = new List<TaskItem>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        if (element.TryGetProperty("document", out var d))
                        {
                            if (d.TryGetProperty("fields", out var fields) && d.TryGetProperty("name", out var nameProp))
                            {
                                var task = new TaskItem();
                                string fullPath = nameProp.GetString() ?? "";
                                task.Id = fullPath.Substring(fullPath.LastIndexOf('/') + 1);

                                if (fields.TryGetProperty("title", out var titleProp) && titleProp.TryGetProperty("stringValue", out var titleVal))
                                    task.Title = titleVal.GetString() ?? "";

                                if (fields.TryGetProperty("status", out var statProp) && statProp.TryGetProperty("stringValue", out var statVal))
                                    task.Status = statVal.GetString() ?? "1";

                                if (fields.TryGetProperty("priority", out var priProp) && priProp.TryGetProperty("stringValue", out var priVal))
                                    task.Priority = priVal.GetString() ?? "low";

                                if (fields.TryGetProperty("category", out var catProp) && catProp.TryGetProperty("stringValue", out var catVal))
                                    task.Category = catVal.GetString() ?? "";

                                if (fields.TryGetProperty("description", out var descProp) && descProp.TryGetProperty("stringValue", out var descVal))
                                    task.Description = descVal.GetString() ?? "";

                                if (fields.TryGetProperty("startDate", out var sdProp))
                                {
                                    if (sdProp.TryGetProperty("integerValue", out var sdStr) && long.TryParse(sdStr.GetString(), out long sdInt))
                                        task.StartDate = sdInt;
                                    else if (sdProp.TryGetProperty("doubleValue", out var sdDouble))
                                        task.StartDate = (long)sdDouble.GetDouble();
                                }

                                if (fields.TryGetProperty("dueDate", out var ddProp))
                                {
                                    if (ddProp.TryGetProperty("integerValue", out var ddStr) && long.TryParse(ddStr.GetString(), out long ddInt))
                                        task.DueDate = ddInt;
                                    else if (ddProp.TryGetProperty("doubleValue", out var ddDouble))
                                        task.DueDate = (long)ddDouble.GetDouble();
                                }

                                if (fields.TryGetProperty("createdAt", out var crProp))
                                {
                                    if (crProp.TryGetProperty("integerValue", out var crStr) && long.TryParse(crStr.GetString(), out long crInt))
                                        task.CreatedAt = crInt;
                                    else if (crProp.TryGetProperty("doubleValue", out var crDouble))
                                        task.CreatedAt = (long)crDouble.GetDouble();
                                }

                                list.Add(task);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[FirebaseSync] Error parsing firestore runQuery tasks: {ex.Message}");
            }
            return list;
        }

        private List<string> ParseFirestoreArray(JsonElement element)
        {
            var list = new List<string>();
            if (element.TryGetProperty("arrayValue", out var arrVal) && arrVal.TryGetProperty("values", out var vals))
            {
                foreach (var val in vals.EnumerateArray())
                {
                    if (val.TryGetProperty("stringValue", out var sv))
                    {
                        list.Add(sv.GetString() ?? "");
                    }
                }
            }
            return list;
        }
    }

    public class TaskItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "1";
        public string Priority { get; set; } = "low";
        public long StartDate { get; set; } = 0;
        public long DueDate { get; set; } = 0;
        public long CreatedAt { get; set; } = 0;
        public long LastUpdatedAt { get; set; } = 0;
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
    }

    public class WidgetTaskItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "Task"; // "Header" or "Task"
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public string Category { get; set; } = "";
        public string ChecklistProgress { get; set; } = "";
        public bool HasChecklist => !string.IsNullOrEmpty(ChecklistProgress);

        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Visibility HeaderVisibility => Type == "Header" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Visibility TaskVisibility => Type == "Task" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Visibility PriorityDotVisibility => (Type == "Task" && !string.IsNullOrEmpty(Priority)) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Visibility ChecklistProgressVisibility => (Type == "Task" && HasChecklist) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        [System.Text.Json.Serialization.JsonIgnore]
        public System.Windows.Media.Brush PriorityBrush
        {
            get
            {
                if (string.IsNullOrEmpty(Priority)) return System.Windows.Media.Brushes.Gray;
                return Priority.ToLower() switch
                {
                    "high" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFEF4444")),
                    "medium" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF59E0B")),
                    "low" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF3B82F6")),
                    _ => System.Windows.Media.Brushes.Gray
                };
            }
        }
    }

    public class CustomViewItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string GroupOption { get; set; } = "none";
        public string SortOption { get; set; } = "dueDate";
        public string SortDirection { get; set; } = "asc";
        public List<string> FilterCategories { get; set; } = new();
        public List<string> FilterPriorities { get; set; } = new();
        public List<string> FilterStatuses { get; set; } = new();
    }
}
