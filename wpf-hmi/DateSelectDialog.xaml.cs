using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using MahApps.Metro.Controls;

namespace VisiPickHMI
{
    public partial class DateSelectDialog : MetroWindow
    {
        public string? SelectedDate { get; private set; }

        public DateSelectDialog(List<string> dates)
        {
            InitializeComponent();
            DateListBox.DataContext = dates;
            if (dates.Count > 0)
                DateListBox.SelectedIndex = 0;
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            if (DateListBox.SelectedItem is string date)
            {
                SelectedDate = date;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("날짜를 선택해주세요.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void DateListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Load_Click(sender, e);
        }
    }
}
