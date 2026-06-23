using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using VisiPickHMI.Data;
using VisiPickHMI.Models;
using VisiPickHMI.Services;

namespace VisiPickHMI
{
    public partial class LogHistoryWindow : MetroWindow
    {
        public LogHistoryWindow()
        {
            InitializeComponent();
            LoadDates();
        }

        private void LoadDates()
        {
            try
            {
                using var db = new AppDbContext();
                var dates = db.SystemEvents
                    .Select(e => e.Timestamp)
                    .ToList()
                    .Select(t => t.Length >= 10 ? t[..10] : t)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToList();

                DateList.ItemsSource = dates;
                TxtDateCount.Text = $"{dates.Count}일 기록";

                if (dates.Count > 0)
                    DateList.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                TxtDateCount.Text = $"오류: {ex.Message}";
            }
        }

        private async void DateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DateList.SelectedItem is not string date) return;

            TxtSelectedDate.Text = $"📅 {date}";
            TxtLogCount.Text = "불러오는 중...";
            HistoryGrid.ItemsSource = null;

            // 백그라운드 스레드에서 DB 조회 (UI 블로킹 방지)
            var logs = await Task.Run(() =>
            {
                using var db = new AppDbContext();
                return db.SystemEvents
                    .Where(ev => ev.Timestamp.StartsWith(date))
                    .OrderByDescending(ev => ev.Timestamp)
                    .ToList();
            });

            HistoryGrid.ItemsSource = logs;
            TxtLogCount.Text = $"{logs.Count}건";
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryGrid.ItemsSource is not List<SystemEvent> logs || logs.Count == 0)
            {
                MessageBox.Show("내보낼 로그가 없습니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var collection = new ObservableCollection<SystemEvent>(logs);
                string date = DateList.SelectedItem as string ?? "unknown";
                string path = CsvExportService.ExportLogs(collection,
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"VisiPick_Log_{date}.csv"));

                MessageBox.Show($"저장 완료:\n{path}", "CSV Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"내보내기 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
