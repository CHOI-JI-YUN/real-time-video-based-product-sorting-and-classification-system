using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using MahApps.Metro.Controls;
using OpenCvSharp;
using VisiPickHMI.Data;
using VisiPickHMI.Models;
using VisiPickHMI.Services;

namespace VisiPickHMI
{
    public partial class LoginWindow : MetroWindow
    {
        public event Action<User>? LoginSucceeded;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                TxtUsername.Focus();
                _ = CheckCameraAsync();
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };
        }

        /// <summary>백그라운드에서 카메라 연결 여부를 확인하여 CCTV 버튼 점 색상을 갱신한다.</summary>
        private async Task CheckCameraAsync()
        {
            int idx = AppConfig.SiteCameraIndex;
            bool ok = await Task.Run(() =>
            {
                try
                {
                    using var cap = new VideoCapture(idx, VideoCaptureAPIs.DSHOW);
                    if (cap.IsOpened()) return true;
                    cap.Release();
                    using var cap2 = new VideoCapture(idx);
                    return cap2.IsOpened();
                }
                catch { return false; }
            });

            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.Invoke(() =>
                {
                    // DotCctv는 ControlTemplate 내부에 있으므로 Template.FindName 사용
                    if (BtnSiteCamera.Template.FindName("DotCctv", BtnSiteCamera) is System.Windows.Shapes.Ellipse dot)
                    {
                        dot.Fill = ok
                            ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))   // 초록
                            : new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));   // 빨강
                    }
                });
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

        private void SiteCamera_Click(object sender, RoutedEventArgs e)
        {
            var win = new SiteCameraWindow { Owner = this };
            win.ShowDialog();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) TryLogin();
        }

        private void TryLogin()
        {
            string username = TxtUsername.Text.Trim();
            string password = _passwordVisible ? TxtPasswordVisible.Text : TxtPassword.Password;

            ErrorBorder.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("아이디와 비밀번호를 입력해주세요.");
                return;
            }

            // 먼저 계정 존재 + 비밀번호 맞는지 확인 (활성 여부 무시)
            var allMatch = AppDbContext.FindUserByCredentials(username, password);
            if (allMatch != null && !allMatch.IsActive)
            {
                ShowError("비활성화된 계정입니다. 관리자에게 문의하세요.");
                return;
            }

            var user = AppDbContext.Authenticate(username, password);
            if (user != null)
            {
                LoginSucceeded?.Invoke(user);
            }
            else
            {
                ShowError("아이디 또는 비밀번호가 올바르지 않습니다.");
                TxtPassword.Clear();
                TxtPasswordVisible.Clear();
                if (_passwordVisible)
                    TxtPasswordVisible.Focus();
                else
                    TxtPassword.Focus();
            }
        }

        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            TxtPasswordPlaceholder.Visibility =
                string.IsNullOrEmpty(TxtPassword.Password)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ShowError(string msg)
        {
            TxtError.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        private void FindPassword_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MessageBox.Show(
                "비밀번호 초기화는 시스템 관리자(Admin)에게 요청해주세요.\n\n" +
                "📞 내선: 1234\n" +
                "📧 admin@visipick.com",
                "비밀번호 찾기",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private bool _passwordVisible = false;

        private void TogglePassword_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _passwordVisible = !_passwordVisible;

            if (_passwordVisible)
            {
                // 비밀번호 보이기
                TxtPasswordVisible.Text = TxtPassword.Password;
                TxtPassword.Visibility = Visibility.Collapsed;
                TxtPasswordVisible.Visibility = Visibility.Visible;

                TxtPasswordPlaceholder.Visibility =
                    string.IsNullOrEmpty(TxtPasswordVisible.Text)
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                TxtPasswordVisible.Focus();
                TxtPasswordVisible.CaretIndex = TxtPasswordVisible.Text.Length;
                BtnTogglePw.Text = "🙈";
            }
            else
            {
                // 비밀번호 숨기기
                TxtPassword.Password = TxtPasswordVisible.Text;
                TxtPasswordVisible.Visibility = Visibility.Collapsed;
                TxtPassword.Visibility = Visibility.Visible;

                TxtPasswordPlaceholder.Visibility =
                    string.IsNullOrEmpty(TxtPassword.Password)
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                TxtPassword.Focus();
                BtnTogglePw.Text = "👁";
            }
        }
    }
}
