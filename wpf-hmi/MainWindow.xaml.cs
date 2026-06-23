using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using VisiPickHMI.ViewModels;
using System.Windows.Threading;

namespace VisiPickHMI
{
    public partial class MainWindow : MetroWindow
    {
        private readonly DashboardViewModel _vm;
        private DispatcherTimer _agvMoveTimer;
        private Dictionary<int, System.Windows.Point> _agvVisualPos = new();
        private Dictionary<int, int> _agvPrevNode = new();
        private Dictionary<int, List<System.Windows.Point>> _agvPath = new();

        private LogHistoryWindow? _logHistoryWindow;
        private AllCameraView? _allCameraView;
        private readonly Models.User _currentUser;

        public MainWindow(Models.User loggedInUser)
        {
            InitializeComponent();
            _vm = new DashboardViewModel();
            DataContext = _vm;
            _currentUser = loggedInUser;

            TxtLoggedInUser.Text = $"🔑 {loggedInUser.DisplayName} ({loggedInUser.Role})";

            _vm.EmergencyOccurred += () =>
            {
                var alert = new EmergencyAlertWindow { Owner = this };
                alert.ShowDialog();
            };

            if (loggedInUser.Role == "Admin")
            {
                BtnUserMgmt.Visibility = Visibility.Visible;
                BtnDataMgmt.Visibility = Visibility.Visible;
                BtnClearLogs.Visibility = Visibility.Visible;
                BtnReset.Visibility = Visibility.Visible;
            }
            else
            {
                BtnUserMgmt.Visibility = Visibility.Collapsed;
                BtnDataMgmt.Visibility = Visibility.Collapsed;
                BtnClearLogs.Visibility = Visibility.Collapsed;
                BtnReset.Visibility = Visibility.Collapsed;
            }

            Loaded += async (_, _) =>
            {
                await _vm.ConnectMqtt();
            };

            ((INotifyCollectionChanged)_vm.EventLog).CollectionChanged += (_, _) =>
            {
                if (_vm.AutoScroll && LogGrid.Items.Count > 0)
                {
                    try { LogGrid.ScrollIntoView(LogGrid.Items[0]); } catch { }
                }
            };

            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_vm.ClassDefectCountsVersion))
                {
                    Dispatcher.Invoke(RefreshDonutCharts);
                }
            };

            _agvMoveTimer = new DispatcherTimer();
            _agvMoveTimer.Interval = TimeSpan.FromMilliseconds(300);
            _agvMoveTimer.Tick += (_, _) => { DrawAgvMap(); };
            _agvMoveTimer.Start();
        }

        private void UserMgmt_Click(object sender, RoutedEventArgs e)
        {
            new UserManagementWindow { Owner = this }.ShowDialog();
        }

        private void ConveyorStart_Click(object sender, RoutedEventArgs e)
        {
            _vm.RecoverFromEmergency();
            _vm.SendCommandToPython("conveyor_start");
            _vm.SendCommandToPython("start");
        }
        private void ConveyorStop_Click(object sender, RoutedEventArgs e) => _vm.SendCommandToPython("conveyor_stop");
        private void Reset_Click(object sender, RoutedEventArgs e) => _vm.ResetSystem();
        private void EmergencyStop_Click(object sender, RoutedEventArgs e) => _vm.EmergencyStop();
        private void ExportCsv_Click(object sender, RoutedEventArgs e) => _vm.ExportLogsToCsv();
        private void ClearLogs_Click(object sender, RoutedEventArgs e) => _vm.ClearLogs();

        private void LoadLogsByDate_Click(object sender, RoutedEventArgs e)
        {
            if (_logHistoryWindow is { IsLoaded: true })
            {
                _logHistoryWindow.Activate();
                return;
            }
            _logHistoryWindow = new LogHistoryWindow { Owner = this };
            _logHistoryWindow.Show();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("로그아웃 하시겠습니까?", "로그아웃",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _vm.Cleanup();
                ((App)Application.Current).ShowLogin();
                Close();
            }
        }

        private void DataMgmt_Click(object sender, RoutedEventArgs e)
        {
            new AdminDataWindow(_currentUser.DisplayName) { Owner = this }.Show();
        }

        private void CameraTab1_Click(object sender, RoutedEventArgs e)
        {
            Camera1Panel.Visibility = Visibility.Visible;
            Camera2Panel.Visibility = Visibility.Collapsed;
            DualCameraPanel.Visibility = Visibility.Collapsed;
            _vm.SelectedCameraTab = 0;
        }

        private void CameraTab2_Click(object sender, RoutedEventArgs e)
        {
            Camera1Panel.Visibility = Visibility.Collapsed;
            Camera2Panel.Visibility = Visibility.Visible;
            DualCameraPanel.Visibility = Visibility.Collapsed;
            _vm.SelectedCameraTab = 1;
        }

        private void CameraTabDual_Click(object sender, RoutedEventArgs e)
        {
            Camera1Panel.Visibility = Visibility.Collapsed;
            Camera2Panel.Visibility = Visibility.Collapsed;
            DualCameraPanel.Visibility = Visibility.Visible;
            _vm.SelectedCameraTab = 2;
        }

        private void AllCameraWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_allCameraView is { IsLoaded: true })
            {
                _allCameraView.Activate();
                return;
            }
            _allCameraView = new AllCameraView(_vm) { Owner = this };
            _allCameraView.Show();
        }

        private void StatsTab_Classification_Click(object sender, RoutedEventArgs e)
        {
            ClassificationPanel.Visibility = Visibility.Visible;
            DefectPanel.Visibility = Visibility.Collapsed;
            _vm.SelectedStatsTab = 0;
        }

        private void StatsTab_Defect_Click(object sender, RoutedEventArgs e)
        {
            ClassificationPanel.Visibility = Visibility.Collapsed;
            DefectPanel.Visibility = Visibility.Visible;
            _vm.SelectedStatsTab = 1;
            RefreshDonutCharts();
        }

        // ═══ Donut Chart Drawing ═══

        private DispatcherTimer? _donutDebounce;

        private void DonutChart_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Canvas c) c.SizeChanged += DonutChart_SizeChanged;
        }

        private void DonutChart_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_donutDebounce == null)
            {
                _donutDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _donutDebounce.Tick += (_, _) => { _donutDebounce.Stop(); RefreshDonutCharts(); };
            }
            _donutDebounce.Stop();
            _donutDebounce.Start();
        }

        private void RefreshDonutCharts()
        {
            if (DonutA.ActualWidth < 1 || DonutA.ActualHeight < 1) return;
            DrawDonut(DonutA, "A", Color.FromRgb(0x60, 0xA5, 0xFA));
            DrawDonut(DonutB, "B", Color.FromRgb(0x34, 0xD3, 0x99));
            DrawDonut(DonutC, "C", Color.FromRgb(0xFB, 0xBF, 0x24));
            DrawDonut(DonutD, "D", Color.FromRgb(0xC0, 0x84, 0xFC));
            UpdateDonutLabels();
        }

        private void UpdateDonutLabels()
        {
            TxtA_Crack.Text = FmtDefectPct("A", "파손");
            TxtA_BentPin.Text = FmtDefectPct("A", "핀휨");
            TxtB_BentPin.Text = FmtDefectPct("B", "핀휨");
            TxtC_Crack.Text = FmtDefectPct("C", "파손");
            TxtD_Crack.Text = FmtDefectPct("D", "파손");
        }

        private string FmtDefectPct(string cls, string defectType)
        {
            int count = _vm.GetClassDefectCount(cls, defectType);
            int crack = _vm.GetClassDefectCount(cls, "파손");
            int bent = _vm.GetClassDefectCount(cls, "핀휨");
            int total = crack + bent;
            if (total == 0) return "0%";
            return $"{(double)count / total * 100:F0}%";
        }

        private void DrawDonut(Canvas canvas, string cls, Color accentColor)
        {
            canvas.Children.Clear();
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 80;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 80;
            double size = Math.Min(w, h);
            double cx = w / 2, cy = h / 2;
            double outerR = size / 2 - 8, innerR = outerR * 0.68;

            int crack = _vm.GetClassDefectCount(cls, "파손");
            int bent = _vm.GetClassDefectCount(cls, "핀휨");
            int total = crack + bent;

            var crackColor = Color.FromRgb(0xC4, 0x8B, 0x9F);
            var bentColor = Color.FromRgb(0x6B, 0x9A, 0xB8);
            var bgTrack = Color.FromRgb(0x14, 0x1D, 0x30);

            canvas.Children.Add(CreateFilledDonutArc(cx, cy, outerR, innerR, -90, 360, new SolidColorBrush(bgTrack)));

            if (total > 0)
            {
                double gapDeg = (crack > 0 && bent > 0) ? 4.0 : 0;
                double startAngle = -90;
                if (crack > 0)
                {
                    double sweepDeg = 360.0 * crack / total - gapDeg;
                    if (sweepDeg < 1) sweepDeg = 1;
                    canvas.Children.Add(CreateFilledDonutArc(cx, cy, outerR, innerR, startAngle, sweepDeg, new SolidColorBrush(crackColor)));
                    startAngle += sweepDeg + gapDeg;
                }
                if (bent > 0)
                {
                    double sweepDeg = 360.0 * bent / total - gapDeg;
                    if (sweepDeg < 1) sweepDeg = 1;
                    canvas.Children.Add(CreateFilledDonutArc(cx, cy, outerR, innerR, startAngle, sweepDeg, new SolidColorBrush(bentColor)));
                }
            }

            int classTotal = cls switch
            {
                "A" => _vm.Summary.ClassACount,
                "B" => _vm.Summary.ClassBCount,
                "C" => _vm.Summary.ClassCCount,
                "D" => _vm.Summary.ClassDCount,
                _ => 0
            };
            double defectPct = classTotal > 0 ? (double)total / classTotal * 100 : 0;

            var totalText = new TextBlock
            {
                Text = $"{defectPct:F1}%",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                TextAlignment = TextAlignment.Center
            };
            totalText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(totalText, cx - totalText.DesiredSize.Width / 2);
            Canvas.SetTop(totalText, cy - totalText.DesiredSize.Height / 2 - 2);
            canvas.Children.Add(totalText);

            var subText = new TextBlock
            {
                Text = "불량률",
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextAlignment = TextAlignment.Center
            };
            subText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(subText, cx - subText.DesiredSize.Width / 2);
            Canvas.SetTop(subText, cy + totalText.DesiredSize.Height / 2 - 2);
            canvas.Children.Add(subText);
        }

        private static System.Windows.Shapes.Path CreateFilledDonutArc(
            double cx, double cy, double outerR, double innerR,
            double startAngleDeg, double sweepAngleDeg, Brush fill)
        {
            if (sweepAngleDeg >= 359.99)
            {
                var outer = new EllipseGeometry(new System.Windows.Point(cx, cy), outerR, outerR);
                var inner = new EllipseGeometry(new System.Windows.Point(cx, cy), innerR, innerR);
                return new System.Windows.Shapes.Path { Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner), Fill = fill };
            }
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;
            bool isLarge = sweepAngleDeg > 180;
            var outerStart = new System.Windows.Point(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
            var outerEnd = new System.Windows.Point(cx + outerR * Math.Cos(endRad), cy + outerR * Math.Sin(endRad));
            var innerEnd = new System.Windows.Point(cx + innerR * Math.Cos(endRad), cy + innerR * Math.Sin(endRad));
            var innerStart = new System.Windows.Point(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));
            var figure = new PathFigure { StartPoint = outerStart, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new ArcSegment { Point = outerEnd, Size = new Size(outerR, outerR), IsLargeArc = isLarge, SweepDirection = SweepDirection.Clockwise });
            figure.Segments.Add(new LineSegment { Point = innerEnd });
            figure.Segments.Add(new ArcSegment { Point = innerStart, Size = new Size(innerR, innerR), IsLargeArc = isLarge, SweepDirection = SweepDirection.Counterclockwise });
            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            return new System.Windows.Shapes.Path { Data = geo, Fill = fill };
        }

        // ═══ AGV 2D Map Drawing ═══

        private void AgvMapCanvas_Loaded(object sender, RoutedEventArgs e) => DrawAgvMap();

        private void DrawAgvMap()
        {
            var canvas = AgvMapCanvas;
            canvas.Children.Clear();

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 500;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 200;

            var gridBrush = new SolidColorBrush(Color.FromArgb(0x18, 0x3B, 0x82, 0xF6));
            for (double gx = 0; gx < w; gx += 30) AddLine(canvas, gx, 0, gx, h, gridBrush, 0.5);
            for (double gy = 0; gy < h; gy += 30) AddLine(canvas, 0, gy, w, gy, gridBrush, 0.5);

            double rowT = h * 0.18, rowC = h * 0.50, rowB = h * 0.82;
            double shiftX = 70;
            double startX = shiftX, startY = h * 0.68;
            double c1X = w * 0.14 + shiftX;
            double c2X = w * 0.30 + shiftX;
            double c3X = w * 0.57 + shiftX;
            double c4X = w * 0.73 + shiftX;

            var tb = new SolidColorBrush(Color.FromArgb(0x70, 0x3B, 0x82, 0xF6));

            AddCurvePts(canvas, startX, startY, w * 0.13, rowC - 10, c1X, rowC, tb, 3);
            AddLine(canvas, c1X, rowC, c3X, rowC, tb, 2.5);
            AddCurvePts(canvas, c1X, rowC, c2X - 100, rowT + 20, c2X, rowT, tb, 2.5);
            AddCurvePts(canvas, c1X, rowC, c2X - 100, rowB - 20, c2X, rowB, tb, 2.5);
            AddLine(canvas, c2X, rowT, c3X, rowT, tb, 2.5);
            AddLine(canvas, c2X, rowB, c3X, rowB, tb, 2.5);
            AddCurvePts(canvas, c3X - 55, rowT, c3X - 95, (rowT + rowC) / 2, c3X - 55, rowC, tb, 2.5);
            AddCurvePts(canvas, c3X - 105, rowC, c3X - 145, (rowC + rowB) / 2, c3X - 105, rowB, tb, 2.5);

            double barV = h * 0.06;
            AddLine(canvas, c3X, rowT - barV, c3X, rowT + barV, tb, 3);
            AddLine(canvas, c3X, rowB - barV, c3X, rowB + barV, tb, 3);
            AddLine(canvas, c3X, rowT, c4X - 180, rowT, tb, 2.5);
            AddLine(canvas, c3X, rowC, c4X, rowC, tb, 2.5);
            AddLine(canvas, c3X, rowB, c4X - 180, rowB, tb, 2.5);
            AddLine(canvas, c4X, rowT - barV, c4X, rowT + barV, tb, 3);
            AddLine(canvas, c4X, rowC - barV, c4X, rowC + barV, tb, 3);
            AddLine(canvas, c4X, rowB - barV, c4X, rowB + barV, tb, 3);
            AddCurvePts(canvas, c4X, rowT, c4X - 140, h * 0.50, c4X, rowB, tb, 2.5);

            var agv1 = _vm.AgvList.Count > 0 ? _vm.AgvList[0] : null;
            DrawHomeBay(canvas, c4X, rowT, "H1", agv1?.Home1Free ?? true);
            DrawHomeBay(canvas, c4X, rowC, "H2", agv1?.Home2Free ?? true);
            DrawHomeBay(canvas, c4X, rowB, "H3", agv1?.Home3Free ?? true);

            AddNode(canvas, startX, startY, "출발", "#22C55E", 7);
            AddNode(canvas, c1X - 15, rowC, "N2", "#EF4444", 7);
            AddNode(canvas, c2X, rowT, "N3", "#EF4444", 7);
            AddNode(canvas, c2X, rowB, "N4", "#EF4444", 7);
            AddNode(canvas, c3X, rowT, "공정1", "#22D3EE", 7);
            AddNode(canvas, c3X, rowC, "N1", "#EF4444", 7);
            AddNode(canvas, c3X, rowB, "공정2", "#22D3EE", 7);

            var nodePositions = new Dictionary<int, System.Windows.Point>
            {
                { 0, new System.Windows.Point(startX, startY) },
                { 1, new System.Windows.Point(c3X, rowC) },
                { 2, new System.Windows.Point(c1X - 15, rowC) },
                { 3, new System.Windows.Point(c2X, rowT) },
                { 4, new System.Windows.Point(c2X, rowB) },
                { 5, new System.Windows.Point(c3X, rowT) },
                { 6, new System.Windows.Point(c3X, rowB) },
                { 7, new System.Windows.Point(c4X, rowT) },   // H1
                { 8, new System.Windows.Point(c4X, rowC) },   // H2
                { 9, new System.Windows.Point(c4X, rowB) },   // H3
            };

            var midpoints = new Dictionary<(int, int), System.Windows.Point[]>
            {
                {(0,2), new[]{ new System.Windows.Point(w*0.13, rowC-10) }},
                {(2,0), new[]{ new System.Windows.Point(w*0.13, rowC-10) }},
                {(2,3), new[]{ new System.Windows.Point(c2X-100, rowT+20) }},
                {(3,2), new[]{ new System.Windows.Point(c2X-100, rowT+20) }},
                {(2,4), new[]{ new System.Windows.Point(c2X-100, rowB-20) }},
                {(4,2), new[]{ new System.Windows.Point(c2X-100, rowB-20) }},
                {(5,1), new[]{ new System.Windows.Point(c3X-75, (rowT+rowC)/2) }},
                {(1,5), new[]{ new System.Windows.Point(c3X-75, (rowT+rowC)/2) }},
                {(6,1), new[]{ new System.Windows.Point(c3X-125, (rowC+rowB)/2) }},
                {(1,6), new[]{ new System.Windows.Point(c3X-125, (rowC+rowB)/2) }},
            };

            foreach (var agv in _vm.AgvList)
            {
                int node = agv.Node;
                if (node < 0) node = 0;
                if (!nodePositions.TryGetValue(node, out var target))
                    target = nodePositions[0];

                if (!_agvVisualPos.ContainsKey(agv.AgvId))
                    _agvVisualPos[agv.AgvId] = target;
                if (!_agvPrevNode.ContainsKey(agv.AgvId))
                    _agvPrevNode[agv.AgvId] = node;

                // 노드 변경 시 즉시 해당 위치로 이동
                if (_agvPrevNode[agv.AgvId] != node)
                {
                    _agvPrevNode[agv.AgvId] = node;
                    _agvVisualPos[agv.AgvId] = target;
                }

                double ox = 0;
                double oy = 0;
                string color = agv.AgvId == 1 ? "#FFD600" : "#B388FF";
                DrawAgvIcon(canvas, _agvVisualPos[agv.AgvId].X + ox, _agvVisualPos[agv.AgvId].Y + oy, agv.AgvId.ToString(), color);
            }
        }

        private void AddCurvePts(Canvas canvas,
            double x0, double y0, double cx, double cy, double x1, double y1,
            Brush stroke, double thickness)
        {
            var fig = new PathFigure { StartPoint = new System.Windows.Point(x0, y0), IsClosed = false };
            fig.Segments.Add(new QuadraticBezierSegment(
                new System.Windows.Point(cx, cy), new System.Windows.Point(x1, y1), true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = geo
            });
        }

        private void DrawHomeBay(Canvas canvas, double x, double y, string label, bool isFree)
        {
            var fillColor = isFree ? Color.FromArgb(0x30, 0x00, 0xE6, 0x76) : Color.FromArgb(0x30, 0xEF, 0x44, 0x44);
            var borderColor = isFree ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0xEF, 0x44, 0x44);

            var rect = new Rectangle
            {
                Width = 36,
                Height = 14,
                Fill = new SolidColorBrush(fillColor),
                Stroke = new SolidColorBrush(borderColor),
                StrokeThickness = 1,
                RadiusX = 3,
                RadiusY = 3
            };
            Canvas.SetLeft(rect, x - 18); Canvas.SetTop(rect, y - 8);
            canvas.Children.Add(rect);

            var tb2 = new TextBlock
            {
                Text = label,
                FontSize = 7,
                Foreground = new SolidColorBrush(borderColor),
                TextAlignment = TextAlignment.Center,
                Width = 36
            };
            Canvas.SetLeft(tb2, x - 18); Canvas.SetTop(tb2, y - 6);
            canvas.Children.Add(tb2);
        }

        private void DrawAgvIcon(Canvas canvas, double x, double y, string id, string colorHex)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            var brush = new SolidColorBrush(color);

            var glow = new Ellipse
            {
                Width = 44,
                Height = 44,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B))
            };
            Canvas.SetLeft(glow, x - 22); Canvas.SetTop(glow, y - 22);
            canvas.Children.Add(glow);

            var outer = new Ellipse
            {
                Width = 32,
                Height = 32,
                Fill = new SolidColorBrush(Color.FromArgb(0x60, color.R, color.G, color.B)),
                Stroke = brush,
                StrokeThickness = 2.5
            };
            Canvas.SetLeft(outer, x - 16); Canvas.SetTop(outer, y - 16);
            canvas.Children.Add(outer);

            var label = new TextBlock
            {
                Text = id,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = brush
            };
            Canvas.SetLeft(label, x - 5); Canvas.SetTop(label, y - 10);
            canvas.Children.Add(label);

            var nameLabel = new TextBlock
            {
                Text = $"AGV{id}",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = brush
            };
            Canvas.SetLeft(nameLabel, x - 14); Canvas.SetTop(nameLabel, y + 18);
            canvas.Children.Add(nameLabel);
        }

        private void AddNode(Canvas canvas, double x, double y, string label, string colorHex, double r = 10)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            var fill = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
            var stroke = new SolidColorBrush(color);

            var circle = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = 1.5 };
            Canvas.SetLeft(circle, x - r); Canvas.SetTop(circle, y - r);
            canvas.Children.Add(circle);

            if (!string.IsNullOrEmpty(label))
            {
                var tb2 = new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(color),
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetLeft(tb2, x - label.Length * 2.5); Canvas.SetTop(tb2, y - r - 26);
                canvas.Children.Add(tb2);
            }
        }

        private void AddLine(Canvas canvas, double x1, double y1, double x2, double y2,
            Brush stroke, double thickness, bool dashed = false)
        {
            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            if (dashed) line.StrokeDashArray = new DoubleCollection { 4, 3 };
            canvas.Children.Add(line);
        }
    }

    public class NullToVisibleConverter : IValueConverter
    {
        public static readonly NullToVisibleConverter Instance = new();
        public object Convert(object? value, Type t, object? p, CultureInfo c) => value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    public class CountToBarWidth : IValueConverter
    {
        public static readonly CountToBarWidth Instance = new();
        public object Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is int count) return Math.Min(200.0, (count / 100.0) * 200.0);
            return 0.0;
        }
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    public class ProgressWidthConverter : IValueConverter
    {
        public static readonly ProgressWidthConverter Instance = new();
        public object Convert(object? value, Type t, object? p, CultureInfo c) => value ?? 0.0;
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    public class CountToBarHeight : IValueConverter
    {
        public static readonly CountToBarHeight Instance = new();
        public object Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is int count && count > 0) return Math.Max(8.0, Math.Min(120.0, count * 6.0));
            return 8.0;
        }
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    public class BoolToHomeBayBrushConverter : IValueConverter
    {
        public static readonly BoolToHomeBayBrushConverter Instance = new();
        private static readonly Brush FreeBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
        private static readonly Brush OccupiedBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        public object Convert(object? value, Type t, object? p, CultureInfo c) => value is true ? FreeBrush : OccupiedBrush;
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}