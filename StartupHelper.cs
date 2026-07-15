using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WpfWidgets
{
    public static class StartupHelper
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ChronosWidgets";
        private const string OldAppName = "WindowsWidgets";
        private const string PackagedTaskId = "ChronosWidgetsStartup";

        public static bool IsPackaged()
        {
            try
            {
                return Windows.ApplicationModel.Package.Current != null && 
                       Windows.ApplicationModel.Package.Current.Id != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<bool> IsStartupEnabledAsync()
        {
            if (IsPackaged())
            {
                try
                {
                    var task = await Windows.ApplicationModel.StartupTask.GetAsync(PackagedTaskId);
                    return task.State == Windows.ApplicationModel.StartupTaskState.Enabled || 
                           task.State == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[StartupHelper] Failed to read packaged startup state: {ex.Message}");
                    return false;
                }
            }
            else
            {
                try
                {
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                    {
                        if (key != null)
                        {
                            object? value = key.GetValue(AppName) ?? key.GetValue(OldAppName);
                            if (value == null) return false;

                            bool isEnabled = true;
                            try
                            {
                                using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                                {
                                    if (approvedKey != null)
                                    {
                                        byte[]? approvedBytes = approvedKey.GetValue(AppName) as byte[] ?? approvedKey.GetValue(OldAppName) as byte[];
                                        if (approvedBytes != null && approvedBytes.Length > 0)
                                        {
                                            if ((approvedBytes[0] & 1) != 0)
                                            {
                                                isEnabled = false;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogHelper.Log($"[StartupHelper] Failed to read StartupApproved key: {ex.Message}");
                            }
                            return isEnabled;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[StartupHelper] Failed to read registry startup: {ex.Message}");
                }
                return false;
            }
        }

        public static async Task<bool> SetStartupStateAsync(bool enable)
        {
            if (IsPackaged())
            {
                try
                {
                    var task = await Windows.ApplicationModel.StartupTask.GetAsync(PackagedTaskId);
                    if (enable)
                    {
                        var state = await task.RequestEnableAsync();
                        return state == Windows.ApplicationModel.StartupTaskState.Enabled || 
                               state == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                    }
                    else
                    {
                        task.Disable();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[StartupHelper] Failed to set packaged startup: {ex.Message}");
                    return false;
                }
            }
            else
            {
                try
                {
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                    {
                        if (key != null)
                        {
                            if (enable)
                            {
                                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfWidgets.exe");
                                key.SetValue(AppName, $"\"{exePath}\"");

                                try
                                {
                                    using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                                    {
                                        if (approvedKey != null)
                                        {
                                            approvedKey.DeleteValue(AppName, false);
                                            approvedKey.DeleteValue(OldAppName, false);
                                        }
                                    }
                                }
                                catch { }
                            }
                            else
                            {
                                key.DeleteValue(AppName, false);
                                try
                                {
                                    using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                                    {
                                        if (approvedKey != null)
                                        {
                                            approvedKey.DeleteValue(AppName, false);
                                            approvedKey.DeleteValue(OldAppName, false);
                                        }
                                    }
                                }
                                catch { }
                            }
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[StartupHelper] Failed to set registry startup: {ex.Message}");
                }
                return false;
            }
        }
    }
}
