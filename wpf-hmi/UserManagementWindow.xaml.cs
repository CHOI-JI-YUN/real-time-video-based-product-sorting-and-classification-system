using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;
using VisiPickHMI.Data;
using VisiPickHMI.Models;

namespace VisiPickHMI
{
    public partial class UserManagementWindow : MetroWindow
    {
        private int? _editingUserId = null;
        private bool _passwordVisible = false;

        public UserManagementWindow()
        {
            InitializeComponent();
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            UserGrid.ItemsSource = AppDbContext.GetAllUsers();
        }

        private void UserGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UserGrid.SelectedItem is not User user) return;

            _editingUserId = user.Id;
            FormTitle.Text = "✏ 회원 수정";
            TxtId.Text = user.Username;
            TxtDisplayName.Text = user.DisplayName;
            TxtPw.Clear();

            // 권한 콤보 선택
            foreach (ComboBoxItem item in CmbRole.Items)
                if (item.Content.ToString() == user.Role)
                { CmbRole.SelectedItem = item; break; }

            BtnDelete.IsEnabled = !IsFixedAdmin(user);
            BtnDelete.Opacity = BtnDelete.IsEnabled ? 1.0 : 0.45;

            TxtFormError.Visibility = Visibility.Collapsed;
        }

        private static bool IsFixedAdmin(User user)
        {
            return user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)
                   || user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        }


        private void UserGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);

            // 회원 목록의 빈 공간을 누르면 선택 해제 → 신규 회원 추가 폼으로 복귀
            if (row == null)
            {
                ClearFormWithoutChangingFocus();
                return;
            }

            // 이미 선택된 회원을 다시 누르면 선택 해제 → 신규 회원 추가 폼으로 복귀
            if (row.IsSelected)
            {
                ClearFormWithoutChangingFocus();
                e.Handled = true;
            }
        }

        private void UserGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { }

        private void ActiveCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb &&
                cb.DataContext is Models.User user)
            {
                user.IsActive = cb.IsChecked ?? false;
                AppDbContext.UpdateUser(user);
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private void ClearFormWithoutChangingFocus()
        {
            _editingUserId = null;
            _passwordVisible = false;
            FormTitle.Text = "➕ 새 회원 추가";
            TxtId.Clear();
            TxtDisplayName.Clear();
            TxtPw.Clear();
            TxtPwVisible.Text = "";
            TxtPw.Visibility = Visibility.Visible;
            TxtPwVisible.Visibility = Visibility.Collapsed;
            BtnTogglePw.Text = "👁";
            CmbRole.SelectedIndex = 1;
            TxtFormError.Visibility = Visibility.Collapsed;
            UserGrid.SelectedItem = null;
            BtnDelete.IsEnabled = true;
            BtnDelete.Opacity = 1.0;
        }

        // ── 복사/붙여넣기 차단 ──
        private void BlockCopyPaste(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Copy ||
                e.Command == ApplicationCommands.Paste ||
                e.Command == ApplicationCommands.Cut)
            {
                e.Handled = true;
            }
        }

        // ── 비밀번호 보이기/숨기기 토글 ──
        private void TogglePassword_Click(object sender, MouseButtonEventArgs e)
        {
            _passwordVisible = !_passwordVisible;

            if (_passwordVisible)
            {
                TxtPwVisible.Text = TxtPw.Password;
                TxtPw.Visibility = Visibility.Collapsed;
                TxtPwVisible.Visibility = Visibility.Visible;
                TxtPwVisible.Focus();
                TxtPwVisible.CaretIndex = TxtPwVisible.Text.Length;
                BtnTogglePw.Text = "🙈";
            }
            else
            {
                TxtPw.Visibility = Visibility.Visible;
                TxtPwVisible.Visibility = Visibility.Collapsed;
                BtnTogglePw.Text = "👁";
                TxtPw.Focus();
            }
        }

        // ── PasswordBox 변경 시 TextBox 동기화 ──
        private void TxtPw_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_passwordVisible)
                TxtPwVisible.Text = TxtPw.Password;
        }

        private void SaveUser_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtId.Text.Trim();
            string displayName = TxtDisplayName.Text.Trim();
            string password = _passwordVisible ? TxtPwVisible.Text : TxtPw.Password;
            string role = (CmbRole.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Operator";

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(displayName))
            {
                ShowFormError("아이디와 이름을 입력해주세요.");
                return;
            }

            if (_editingUserId == null && string.IsNullOrEmpty(password))
            {
                ShowFormError("비밀번호를 입력해주세요.");
                return;
            }

            // 아이디 중복 체크
            var allUsers = AppDbContext.GetAllUsers();
            var duplicate = allUsers.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                && u.Id != _editingUserId);
            if (duplicate != null)
            {
                ShowFormError("이미 사용 중인 아이디입니다.");
                return;
            }

            try
            {
                if (_editingUserId == null)
                {
                    // 신규 추가
                    AppDbContext.AddUser(new User
                    {
                        Username = username,
                        PasswordHash = AppDbContext.HashPassword(password),
                        DisplayName = displayName,
                        Role = role,
                        IsActive = true
                    });
                }
                else
                {
                    // 수정
                    using var db = new AppDbContext();
                    var user = db.Users.Find(_editingUserId);
                    if (user != null)
                    {
                        user.Username = username;
                        user.DisplayName = displayName;
                        user.Role = role;
                        if (!string.IsNullOrEmpty(password))
                            user.PasswordHash = AppDbContext.HashPassword(password);
                        db.SaveChanges();
                    }
                }

                ClearForm_Click(sender, e);
                RefreshGrid();
            }
            catch (Exception ex)
            {
                ShowFormError($"저장 실패: {ex.Message}");
            }
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (UserGrid.SelectedItem is not User user) return;

            if (IsFixedAdmin(user))
            {
                MessageBox.Show("관리자 계정은 기본 계정이라 삭제할 수 없습니다.",
                    "삭제 불가", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"'{user.DisplayName}({user.Username})' 계정을 삭제하시겠습니까?",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                AppDbContext.DeleteUser(user.Id);
                ClearForm_Click(sender, e);
                RefreshGrid();
            }
        }

        private void ClearForm_Click(object sender, RoutedEventArgs e)
        {
            ClearFormWithoutChangingFocus();
        }

        private void ShowFormError(string msg)
        {
            TxtFormError.Text = msg;
            TxtFormError.Visibility = Visibility.Visible;
        }
    }
}
