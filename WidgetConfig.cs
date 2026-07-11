using System;
using System.IO;
using System.Text.Json;

namespace WpfWidgets
{
    public class WidgetSettings
    {
        public double ClockPositionX { get; set; } = -1; // -1 means default center-right
        public double ClockPositionY { get; set; } = -1;
        public double ClockWidth { get; set; } = 320;
        public double ClockHeight { get; set; } = 180;
        public bool ClockLocked { get; set; } = false;
        public string ClockTheme { get; set; } = "Light"; // Options: Light, Dark, System
        public bool Is24HourFormat { get; set; } = false;
        public bool ClockEnabled { get; set; } = true;
        public string Clock1Label { get; set; } = "Local Time";
        public string Clock1TimeZoneId { get; set; } = "Local";

        // Clock 2 Settings
        public bool Clock2Enabled { get; set; } = false;
        public string Clock2Label { get; set; } = "London";
        public string Clock2TimeZoneId { get; set; } = "GMT Standard Time";

        // Clock 3 Settings
        public bool Clock3Enabled { get; set; } = false;
        public string Clock3Label { get; set; } = "Tokyo";
        public string Clock3TimeZoneId { get; set; } = "Tokyo Standard Time";

        // Calendar Widget Settings
        public double CalendarPositionX { get; set; } = -1;
        public double CalendarPositionY { get; set; } = -1;
        public double CalendarWidth { get; set; } = 340;
        public double CalendarHeight { get; set; } = 400;
        public bool CalendarLocked { get; set; } = false;
        public bool CalendarEnabled { get; set; } = true;
        public string CalendarUserId { get; set; } = "";
        public string CalendarRefreshTokenEncrypted { get; set; } = "";
        public string CalendarEventsCache { get; set; } = "[]";

        // Tasks Widget Settings
        public double TasksPositionX { get; set; } = -1;
        public double TasksPositionY { get; set; } = -1;
        public double TasksWidth { get; set; } = 320;
        public double TasksHeight { get; set; } = 450;
        public bool TasksLocked { get; set; } = false;
        public bool TasksEnabled { get; set; } = true;
        public string TasksCache { get; set; } = "[]";
        public string TasksSelectedViewId { get; set; } = "FollowMobile";
        public string TasksCustomViewsCache { get; set; } = "[]";

        // Weather Widget Settings
        public double WeatherPositionX { get; set; } = -1;
        public double WeatherPositionY { get; set; } = -1;
        public double WeatherWidth { get; set; } = 320;
        public double WeatherHeight { get; set; } = 220;
        public bool WeatherLocked { get; set; } = false;
        public bool WeatherEnabled { get; set; } = true;
        public string WeatherLocationName { get; set; } = "Seattle, Washington";
        public double WeatherLatitude { get; set; } = 47.6062;
        public double WeatherLongitude { get; set; } = -122.3321;
        public bool WeatherAutoDetect { get; set; } = true;
        public bool UseCelsius { get; set; } = false;

        public double WidgetOpacity { get; set; } = 0.6;

        // Google Calendar Multi-Account local integrations
        public System.Collections.Generic.List<GoogleAccountConfig> GoogleAccounts { get; set; } = new();

        public void SaveRefreshToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                CalendarRefreshTokenEncrypted = "";
                return;
            }

            try
            {
                byte[] plaintextBytes = System.Text.Encoding.UTF8.GetBytes(token);
                byte[] ciphertextBytes = System.Security.Cryptography.ProtectedData.Protect(plaintextBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                CalendarRefreshTokenEncrypted = Convert.ToBase64String(ciphertextBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WidgetSettings] DPAPI encryption failed: {ex.Message}");
                CalendarRefreshTokenEncrypted = "B64_" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
            }
        }

        public string GetRefreshToken()
        {
            if (string.IsNullOrEmpty(CalendarRefreshTokenEncrypted))
            {
                return "";
            }

            try
            {
                if (CalendarRefreshTokenEncrypted.StartsWith("B64_"))
                {
                    return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(CalendarRefreshTokenEncrypted.Substring(4)));
                }

                byte[] ciphertextBytes = Convert.FromBase64String(CalendarRefreshTokenEncrypted);
                byte[] plaintextBytes = System.Security.Cryptography.ProtectedData.Unprotect(ciphertextBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(plaintextBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WidgetSettings] DPAPI decryption failed: {ex.Message}");
                return "";
            }
        }
    }

    public class GoogleAccountConfig
    {
        public string Email { get; set; } = "";
        public string RefreshTokenEncrypted { get; set; } = "";

        public void SaveRefreshToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                RefreshTokenEncrypted = "";
                return;
            }

            try
            {
                byte[] plaintextBytes = System.Text.Encoding.UTF8.GetBytes(token);
                byte[] ciphertextBytes = System.Security.Cryptography.ProtectedData.Protect(plaintextBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                RefreshTokenEncrypted = Convert.ToBase64String(ciphertextBytes);
            }
            catch
            {
                RefreshTokenEncrypted = "B64_" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
            }
        }

        public string GetRefreshToken()
        {
            if (string.IsNullOrEmpty(RefreshTokenEncrypted))
            {
                return "";
            }

            try
            {
                if (RefreshTokenEncrypted.StartsWith("B64_"))
                {
                    return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(RefreshTokenEncrypted.Substring(4)));
                }

                byte[] ciphertextBytes = Convert.FromBase64String(RefreshTokenEncrypted);
                byte[] plaintextBytes = System.Security.Cryptography.ProtectedData.Unprotect(ciphertextBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(plaintextBytes);
            }
            catch
            {
                return "";
            }
        }
    }

    public static class WidgetConfig
    {
        private static readonly string AppDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "WindowsWidgets"
        );
        
        private static readonly string ConfigFilePath = Path.Combine(AppDirectory, "widget-config.json");

        public static WidgetSettings Current { get; private set; } = new();

        /// <summary>
        /// Loads settings from the config file, fallback to defaults if not found.
        /// </summary>
        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var settings = JsonSerializer.Deserialize<WidgetSettings>(json);
                    if (settings != null)
                    {
                        Current = settings;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WidgetConfig] Failed to load config: {ex.Message}");
            }

            // Fallback to default values
            Current = new WidgetSettings();
        }

        /// <summary>
        /// Saves the current configuration to the config file.
        /// </summary>
        public static void Save()
        {
            try
            {
                if (!Directory.Exists(AppDirectory))
                {
                    Directory.CreateDirectory(AppDirectory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WidgetConfig] Failed to save config: {ex.Message}");
            }
        }
    }
}
