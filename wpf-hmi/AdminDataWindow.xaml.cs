using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using VisiPickHMI.Data;
using VisiPickHMI.Models;

namespace VisiPickHMI
{
    public partial class AdminDataWindow : MetroWindow
    {
        private readonly string _currentUser;

        public AdminDataWindow(string currentUserName)
        {
            InitializeComponent();
            _currentUser = currentUserName;
            LoadInspections();
        }

        // ═══════════════════════════
        //  Tab Switching
        // ═══════════════════════════
        private void Tab_Inspection_Click(object sender, RoutedEventArgs e) { ShowPanel(PanelInspection); LoadInspections(); }
        private void Tab_Events_Click(object sender, RoutedEventArgs e) { ShowPanel(PanelEvents); LoadEvents(); }
        private void Tab_Settings_Click(object sender, RoutedEventArgs e) { ShowPanel(PanelSettings); LoadSettings(); }
        private void Tab_Agv_Click(object sender, RoutedEventArgs e) { ShowPanel(PanelAgv); LoadMissions(); }

        private void ShowPanel(UIElement active)
        {
            PanelInspection.Visibility = Visibility.Collapsed;
            PanelEvents.Visibility = Visibility.Collapsed;
            PanelSettings.Visibility = Visibility.Collapsed;
            PanelAgv.Visibility = Visibility.Collapsed;
            active.Visibility = Visibility.Visible;
        }

        // ═══════════════════════════
        //  TAB 1: 검사 결과
        // ═══════════════════════════
        private List<InspectionResult> _inspections = new();

        private async void LoadInspections()
        {
            _inspections = await Task.Run(() =>
            {
                using var db = new AppDbContext();
                return db.InspectionResults.OrderByDescending(r => r.Id).ToList();
            });
            InspGrid.ItemsSource = _inspections;
            TxtInspCount.Text = $"{_inspections.Count}건";
        }

        private void RefreshInspection_Click(object sender, RoutedEventArgs e) => LoadInspections();

        private void EditInspection_Click(object sender, RoutedEventArgs e)
        {
            if (InspGrid.SelectedItem is not InspectionResult selected)
            {
                MessageBox.Show("수정할 항목을 선택하세요.", "알림"); return;
            }

            // 간단 편집 다이얼로그
            var dlg = new EditInspectionDialog(selected) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                AppDbContext.UpdateInspection(dlg.Result);
                LoadInspections();
            }
        }

        private void DeleteInspection_Click(object sender, RoutedEventArgs e)
        {
            var selected = InspGrid.SelectedItems.Cast<InspectionResult>().ToList();
            if (selected.Count == 0) { MessageBox.Show("삭제할 항목을 선택하세요.", "알림"); return; }

            var result = MessageBox.Show(
                $"선택된 {selected.Count}건을 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                "검사 결과 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                AppDbContext.DeleteInspections(selected.Select(s => s.Id).ToList());
                LoadInspections();
            }
        }

        // ═══════════════════════════
        //  TAB 2: 이벤트 로그
        // ═══════════════════════════
        private async void LoadEvents()
        {
            var events = await Task.Run(() =>
            {
                using var db = new AppDbContext();
                return db.SystemEvents.OrderByDescending(e => e.Id).ToList();
            });
            EventGrid.ItemsSource = events;
            TxtEventCount.Text = $"{events.Count}건";
        }

        private void RefreshEvents_Click(object sender, RoutedEventArgs e) => LoadEvents();

        private void DeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            var selected = EventGrid.SelectedItems.Cast<SystemEvent>().ToList();
            if (selected.Count == 0) { MessageBox.Show("삭제할 항목을 선택하세요.", "알림"); return; }

            if (MessageBox.Show($"{selected.Count}건 삭제?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var evt in selected) AppDbContext.DeleteEvent(evt.Id);
                LoadEvents();
            }
        }

        private void DeleteEventsByDate_Click(object sender, RoutedEventArgs e)
        {
            // 날짜 목록 조회
            List<string> dates;
            using (var db = new AppDbContext())
            {
                dates = db.SystemEvents.Select(ev => ev.Timestamp).ToList()
                    .Select(t => t.Length >= 10 ? t[..10] : t)
                    .Distinct().OrderByDescending(d => d).ToList();
            }

            if (dates.Count == 0) { MessageBox.Show("삭제할 로그가 없습니다.", "알림"); return; }

            var dlg = new DateSelectDialog(dates) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedDate != null)
            {
                if (MessageBox.Show($"{dlg.SelectedDate} 날짜의 모든 로그를 삭제하시겠습니까?",
                    "날짜별 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    int count = AppDbContext.DeleteEventsByDate(dlg.SelectedDate);
                    MessageBox.Show($"{count}건 삭제 완료", "완료");
                    LoadEvents();
                }
            }
        }

        private void DeleteAllEvents_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("⚠ 전체 이벤트 로그를 삭제합니까?\n이 작업은 되돌릴 수 없습니다!",
                "전체 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                int count = AppDbContext.DeleteAllEvents();
                MessageBox.Show($"{count}건 삭제 완료", "완료");
                LoadEvents();
            }
        }

        // ═══════════════════════════
        //  TAB 3: 시스템 설정
        // ═══════════════════════════
        private List<SystemSettings> _settings = new();

        private async void LoadSettings()
        {
            _settings = await Task.Run(() => AppDbContext.GetAllSettings());
            SettingsGrid.ItemsSource = _settings;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            int changed = 0;
            foreach (var s in _settings)
            {
                var orig = AppDbContext.GetSetting(s.Key);
                if (orig != s.Value)
                {
                    AppDbContext.SaveSetting(s.Key, s.Value, _currentUser);
                    changed++;
                }
            }

            if (changed > 0)
            {
                MessageBox.Show($"{changed}개 설정값이 저장되었습니다.", "저장 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                LoadSettings();
            }
            else
            {
                MessageBox.Show("변경된 설정이 없습니다.", "알림");
            }
        }

        // ═══════════════════════════
        //  TAB 4: AGV 미션
        // ═══════════════════════════
        private async void LoadMissions()
        {
            var missions = await Task.Run(() =>
            {
                using var db = new AppDbContext();
                return db.AgvMissions.OrderByDescending(m => m.Id).ToList();
            });
            AgvGrid.ItemsSource = missions;
            TxtAgvCount.Text = $"{missions.Count}건";
        }

        private void RefreshMissions_Click(object sender, RoutedEventArgs e) => LoadMissions();

        private void EditMission_Click(object sender, RoutedEventArgs e)
        {
            if (AgvGrid.SelectedItem is not AgvMission selected)
            {
                MessageBox.Show("수정할 항목을 선택하세요.", "알림"); return;
            }

            // 상태만 간단히 수정
            string[] statuses = { "대기", "운반중", "완료", "오류" };
            int currentIdx = Array.IndexOf(statuses, selected.Status);
            int nextIdx = (currentIdx + 1) % statuses.Length;

            if (MessageBox.Show($"상태를 '{selected.Status}' → '{statuses[nextIdx]}'(으)로 변경?",
                "상태 수정", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                selected.Status = statuses[nextIdx];
                if (statuses[nextIdx] == "완료" && string.IsNullOrEmpty(selected.EndTime))
                    selected.EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                AppDbContext.UpdateMission(selected);
                LoadMissions();
            }
        }

        private void DeleteMission_Click(object sender, RoutedEventArgs e)
        {
            var selected = AgvGrid.SelectedItems.Cast<AgvMission>().ToList();
            if (selected.Count == 0) { MessageBox.Show("삭제할 항목을 선택하세요.", "알림"); return; }

            if (MessageBox.Show($"{selected.Count}건 삭제?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var m in selected) AppDbContext.DeleteMission(m.Id);
                LoadMissions();
            }
        }
    }
}
