using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using VisiPickHMI.Models;

namespace VisiPickHMI
{
    public partial class EditInspectionDialog : MetroWindow
    {
        public InspectionResult Result { get; private set; }

        public EditInspectionDialog(InspectionResult original)
        {
            InitializeComponent();
            Result = original;

            TxtId.Text = original.Id.ToString();
            TxtTime.Text = original.Timestamp;
            TbComponent.Text = original.ComponentType;

            // 결과 콤보박스 선택
            foreach (ComboBoxItem item in CbResult.Items)
            {
                if (item.Content.ToString() == original.Result)
                { item.IsSelected = true; break; }
            }

            // 불량유형 콤보박스 — DefectTypeDisplay 기준으로 선택
            var displayType = original.DefectTypeDisplay;
            foreach (ComboBoxItem item in CbDefect.Items)
            {
                if (item.Content.ToString() == displayType)
                { item.IsSelected = true; break; }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Result.ComponentType = TbComponent.Text.Trim();
            Result.Result = (CbResult.SelectedItem as ComboBoxItem)?.Content.ToString() ?? Result.Result;

            // 불량유형 → DefectCode 역매핑
            var selectedType = (CbDefect.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "—";
            switch (selectedType)
            {
                case "파손": Result.DefectCode = "CRACK"; break;
                case "핀휨": Result.DefectCode = "BENT_PIN"; break;
                default: Result.DefectCode = "NONE"; break;
            }

            // 양품/PASS로 변경 시 불량코드도 NONE으로
            if (Result.Result is "양품" or "PASS")
                Result.DefectCode = "NONE";

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
