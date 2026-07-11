using System;
using System.Windows;
using System.Windows.Input;

namespace WpfWidgets
{
    public partial class LoginWindow : Window
    {
        public bool LoginSucceeded { get; private set; } = false;
        private bool _isSignUpMode = false;

        private readonly FirebaseSyncService _syncService;

        public LoginWindow(FirebaseSyncService syncService)
        {
            InitializeComponent();
            _syncService = syncService;
            _syncService.SyncStatusChanged += OnSyncStatusChanged;
            UpdateUiMode();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            LoginSucceeded = false;
            System.Windows.Application.Current.Shutdown();
        }

        private void ToggleModeLink_Click(object sender, MouseButtonEventArgs e)
        {
            _isSignUpMode = !_isSignUpMode;
            UpdateUiMode();
        }

        private void UpdateUiMode()
        {
            if (_isSignUpMode)
            {
                SubtitleText.Text = "Create a new Chronos account";
                ToggleModeLink.Text = "Already have an account? Sign In";
                EmailSubmitButton.Content = "Sign Up";
            }
            else
            {
                SubtitleText.Text = "Sign in with your Chronos account";
                ToggleModeLink.Text = "Don't have an account? Sign Up";
                EmailSubmitButton.Content = "Sign In";
            }
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            SetLoadingState(true, "Opening Google sign-in...");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                bool success = await _syncService.StartGoogleSignInAsync();

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (success)
                    {
                        LoginSucceeded = true;
                        SetLoadingState(false, "");
                        Close();
                    }
                    else
                    {
                        SetLoadingState(false, "Sign-in failed or was cancelled.\nPlease try again.");
                    }
                }));
            });
        }

        private void EmailSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailInput.Text.Trim();
            string password = PasswordInput.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                SetLoadingState(false, "Please enter both email and password.");
                return;
            }

            SetLoadingState(true, _isSignUpMode ? "Creating account..." : "Signing in...");

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                bool success;
                if (_isSignUpMode)
                {
                    success = await _syncService.SignUpWithEmailAsync(email, password);
                }
                else
                {
                    success = await _syncService.SignInWithEmailAsync(email, password);
                }

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (success)
                    {
                        LoginSucceeded = true;
                        SetLoadingState(false, "");
                        Close();
                    }
                    else
                    {
                        // FirebaseSyncService updates status text via SyncStatusChanged, so we just reset loading.
                        SetLoadingState(false, null);
                    }
                }));
            });
        }

        private void OnSyncStatusChanged(string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (LoadingDots.Visibility == Visibility.Visible || status.Contains("failed") || status.Contains("Invalid"))
                {
                    StatusText.Text = status;
                    StatusText.Visibility = Visibility.Visible;
                }
            }));
        }

        private void SetLoadingState(bool isLoading, string? message)
        {
            SignInButton.IsEnabled = !isLoading;
            EmailSubmitButton.IsEnabled = !isLoading;
            EmailInput.IsEnabled = !isLoading;
            PasswordInput.IsEnabled = !isLoading;
            ToggleModeLink.IsEnabled = !isLoading;
            
            LoadingDots.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

            if (!string.IsNullOrEmpty(message))
            {
                StatusText.Text = message;
                StatusText.Visibility = Visibility.Visible;
            }
            else if (message == null)
            {
                // keep current status text visible (updated from service)
            }
            else
            {
                StatusText.Visibility = Visibility.Collapsed;
            }
        }
    }
}
