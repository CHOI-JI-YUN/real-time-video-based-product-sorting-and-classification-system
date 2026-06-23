using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using VisiPickHMI.Models;
using VisiPickHMI.Services;
using VisiPickHMI.Data;

namespace VisiPickHMI.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        // ── Services ──
        private readonly MqttService _mqttService;
        // 통신 구조 A: 제어=REST, 영상=MJPEG (둘 다 HttpClient 기반)
        private readonly RestService _restService;
        private readonly CameraStreamService _cameraStream;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _elapsedTimer;
        private readonly DispatcherTimer _deviceWatchdogTimer;
        private DateTime _lastDbCheckTime = DateTime.MinValue;
        private DateTime _startTime;
        private bool _isRunning;
        private string _currentDate = DateTime.Now.ToString("yyyy-MM-dd");

        // ── AGV 상태 변경 추적 (중복 로그 방지) ──
        private readonly Dictionary<int, string> _lastAgvStates = new();
        private readonly Dictionary<int, int> _activeAgvMissionIds = new();

        // ── Device Status ──
        public ObservableCollection<DeviceStatus> Devices { get; } = new();

        // ── Classification Summary ──
        private ClassificationSummary _summary = new();
        public ClassificationSummary Summary
        {
            get => _summary;
            set { _summary = value; OnPropertyChanged(); }
        }

        // ── Camera Frames (2개) ──
        private BitmapSource? _cameraFrame1;
        public BitmapSource? CameraFrame1
        {
            get => _cameraFrame1;
            set { _cameraFrame1 = value; OnPropertyChanged(); }
        }

        private BitmapSource? _cameraFrame2;
        public BitmapSource? CameraFrame2
        {
            get => _cameraFrame2;
            set { _cameraFrame2 = value; OnPropertyChanged(); }
        }

        // 하위 호환: 기존 CameraFrame → CameraFrame1로 연결
        public BitmapSource? CameraFrame
        {
            get => _cameraFrame1;
            set { CameraFrame1 = value; OnPropertyChanged(); }
        }

        private int _selectedCameraTab;
        public int SelectedCameraTab
        {
            get => _selectedCameraTab;
            set { _selectedCameraTab = value; OnPropertyChanged(); }
        }

        // ── Last Detection ──
        private string _lastComponent = "—";
        public string LastComponent { get => _lastComponent; set { _lastComponent = value; OnPropertyChanged(); } }

        private string _lastClass = "—";
        public string LastClass { get => _lastClass; set { _lastClass = value; OnPropertyChanged(); } }

        private double _lastConfidence;
        public double LastConfidence { get => _lastConfidence; set { _lastConfidence = value; OnPropertyChanged(); } }

        private string _lastResult = "—";
        public string LastResult { get => _lastResult; set { _lastResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastResultColor)); OnPropertyChanged(nameof(LastResultDisplay)); } }

        /// <summary>
        /// LastResult 값에 따른 색상 (Defect=빨강, Pass=초록)
        /// </summary>
        public string LastResultColor
        {
            get
            {
                switch (_lastResult?.ToUpper())
                {
                    case "DEFECT": case "불량": return "#EF5350";
                    default: return "#66BB6A";
                }
            }
        }

        /// <summary>
        /// LastResult 값에 따른 표시 텍스트
        /// </summary>
        public string LastResultDisplay
        {
            get
            {
                switch (_lastResult?.ToUpper())
                {
                    case "DEFECT": case "불량": return "DEFECT";
                    case "PASS": case "양품": return "PASS";
                    default: return _lastResult;
                }
            }
        }

        private int _lastGate;
        public int LastGate { get => _lastGate; set { _lastGate = value; OnPropertyChanged(); } }

        // ── AGV Status ──
        public ObservableCollection<AgvStatus> AgvList { get; } = new();

        // ── Event Log ──
        public ObservableCollection<SystemEvent> EventLog { get; } = new();

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set { _autoScroll = value; OnPropertyChanged(); }
        }

        // ── Control Bar ──
        private int _conveyorSpeed = 15;
        public int ConveyorSpeed
        {
            get => _conveyorSpeed;
            set { _conveyorSpeed = value; OnPropertyChanged(); SetConveyorSpeed(value); }
        }

        private int _currentCycle;
        public int CurrentCycle
        {
            get => _currentCycle;
            set { _currentCycle = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentCycleText)); }
        }
        public string CurrentCycleText => _currentCycle.ToString("D4");

        private int _targetCycle = 50;
        public int TargetCycle
        {
            get => _targetCycle;
            set { _targetCycle = value; OnPropertyChanged(); OnPropertyChanged(nameof(TargetCycleText)); }
        }
        public string TargetCycleText => _targetCycle.ToString("D4");

        private string _elapsedTime = "00:00";
        public string ElapsedTime { get => _elapsedTime; set { _elapsedTime = value; OnPropertyChanged(); } }

        private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
        public string CurrentTime { get => _currentTime; set { _currentTime = value; OnPropertyChanged(); } }

        private string _systemMode = "AUTO";
        public string SystemMode { get => _systemMode; set { _systemMode = value; OnPropertyChanged(); } }

        private string _systemState = "대기";
        public string SystemState { get => _systemState; set { _systemState = value; OnPropertyChanged(); } }

        // ══════════════════════════════════════════
        //   ② 분류현황 / 불량현황 탭 전환
        // ══════════════════════════════════════════

        private int _selectedStatsTab;
        public int SelectedStatsTab
        {
            get => _selectedStatsTab;
            set { _selectedStatsTab = value; OnPropertyChanged(); }
        }

        // ── 불량 통계 (Flask 대체) ──
        private double _defectRate;
        public double DefectRate { get => _defectRate; set { _defectRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(DefectRateText)); } }
        public string DefectRateText => _defectRate.ToString("F1");

        private string _topDefectCode = "—";
        public string TopDefectCode { get => _topDefectCode; set { _topDefectCode = value; OnPropertyChanged(); } }

        // 클래스별 불량 수
        private int _defectClassA;
        public int DefectClassA { get => _defectClassA; set { _defectClassA = value; OnPropertyChanged(); } }

        private int _defectClassB;
        public int DefectClassB { get => _defectClassB; set { _defectClassB = value; OnPropertyChanged(); } }

        private int _defectClassC;
        public int DefectClassC { get => _defectClassC; set { _defectClassC = value; OnPropertyChanged(); } }

        private int _defectClassD;
        public int DefectClassD { get => _defectClassD; set { _defectClassD = value; OnPropertyChanged(); } }

        // 불량 유형별 카운트 (파레토용 — 기존 호환)
        public ObservableCollection<DefectTypeCount> DefectPareto { get; } = new();

        // 부품별 불량 유형 카운트 (도넛 차트용: 파손/핀휨)
        // Key: "A_파손", "A_핀휨", "B_파손", ... 형태로 딕셔너리 관리
        private readonly Dictionary<string, int> _classDefectCounts = new();

        public int GetClassDefectCount(string cls, string defectType)
        {
            return _classDefectCounts.TryGetValue($"{cls}_{defectType}", out var v) ? v : 0;
        }

        public void IncrementClassDefect(string cls, string defectType)
        {
            var key = $"{cls}_{defectType}";
            if (!_classDefectCounts.ContainsKey(key))
                _classDefectCounts[key] = 0;
            _classDefectCounts[key]++;
            ClassDefectCountsVersion++;
        }

        // 버전 카운터: 도넛 차트 갱신 트리거용
        private int _classDefectCountsVersion;
        public int ClassDefectCountsVersion
        {
            get => _classDefectCountsVersion;
            set { _classDefectCountsVersion = value; OnPropertyChanged(); }
        }

        public void ResetClassDefectCounts()
        {
            _classDefectCounts.Clear();
            ClassDefectCountsVersion++;
        }

        /// <summary>
        /// DefectCode → 불량 유형 매핑 (파손/핀휨)
        /// BROKEN, DENTED → 파손
        /// BENT_PIN → 핀휨
        /// </summary>
        public static string MapDefectType(string defectCode)
        {
            switch (defectCode?.ToUpper())
            {
                case "BENT_PIN":
                    return "핀휨";
                default:
                    return "파손";
            }
        }

        // 최근 불량 이력
        public ObservableCollection<InspectionResult> RecentDefects { get; } = new();
        public ObservableCollection<TraySlot> TraySlots { get; } = new();

        // ── LiveCharts2: 시간대별 생산 추이 차트 ──
        private ISeries[] _productionSeries = Array.Empty<ISeries>();
        public ISeries[] ProductionSeries
        {
            get => _productionSeries;
            set { _productionSeries = value; OnPropertyChanged(); }
        }

        private Axis[] _productionXAxes = Array.Empty<Axis>();
        public Axis[] ProductionXAxes
        {
            get => _productionXAxes;
            set { _productionXAxes = value; OnPropertyChanged(); }
        }

        private Axis[] _productionYAxes = Array.Empty<Axis>();
        public Axis[] ProductionYAxes
        {
            get => _productionYAxes;
            set { _productionYAxes = value; OnPropertyChanged(); }
        }

        // ── 불량 유형 차트 (LiveCharts2) ──
        private ISeries[] _defectSeries = Array.Empty<ISeries>();
        public ISeries[] DefectSeries
        {
            get => _defectSeries;
            set { _defectSeries = value; OnPropertyChanged(); }
        }

        private Axis[] _defectXAxes = Array.Empty<Axis>();
        public Axis[] DefectXAxes
        {
            get => _defectXAxes;
            set { _defectXAxes = value; OnPropertyChanged(); }
        }

        private Axis[] _defectYAxes = Array.Empty<Axis>();
        public Axis[] DefectYAxes
        {
            get => _defectYAxes;
            set { _defectYAxes = value; OnPropertyChanged(); }
        }

        // ── 비상정지 이벤트 (View에서 팝업/경보음 처리) ──
        public event Action? EmergencyOccurred;

        // ── Constructor ──
        public DashboardViewModel()
        {
            _mqttService = new MqttService();
            _restService = new RestService();

            // 영상 수신: MJPEG/스냅샷 폴링 → CameraFrame1 갱신
            _cameraStream = new CameraStreamService();
            _cameraStream.OnFrame += (camId, bmp) =>
            {
                if (camId == 2) CameraFrame2 = bmp;
                else CameraFrame1 = bmp;
                UpdateDeviceStatus($"Camera{camId}", "Connected");
            };
            _cameraStream.OnError += (camId, err) =>
            {
                UpdateDeviceStatus($"Camera{camId}", "Disconnected");
                if (camId == 2)
                    System.Diagnostics.Debug.WriteLine($"[Camera2 ERROR] {err}");
            };

            // 시계 타이머
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) =>
            {
                CurrentTime = DateTime.Now.ToString("HH:mm:ss");
                // 날짜 변경 감지 → 자동 초기화
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (_currentDate != today)
                {
                    _currentDate = today;
                    DailyReset();
                }
            };
            _clockTimer.Start();

            // 경과 시간 타이머
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, _) =>
            {
                if (_isRunning)
                {
                    var elapsed = DateTime.Now - _startTime;
                    ElapsedTime = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
                }
            };

            InitializeDevices();

            // 트레이 슬롯 초기화
            TraySlots.Add(new TraySlot { SlotId = "A", Label = "IC칩" });
            TraySlots.Add(new TraySlot { SlotId = "B", Label = "터미널" });
            TraySlots.Add(new TraySlot { SlotId = "C", Label = "방열판" });
            TraySlots.Add(new TraySlot { SlotId = "D", Label = "커패시터" });
            InitializeAgv();

            _deviceWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _deviceWatchdogTimer.Tick += (_, _) => CheckRealDeviceConnections();
            _deviceWatchdogTimer.Start();
            RefreshDbConnectionStatus();

            // DefectPareto 변경 시 차트 자동 갱신 — LoadMockData 전에 구독
            DefectPareto.CollectionChanged += (_, _) => RebuildDefectChart();

            LoadMockData();
        }

        private void InitializeDevices()
        {
            Devices.Add(new DeviceStatus { Name = "Camera1", State = "Connected" });
            Devices.Add(new DeviceStatus { Name = "Camera2", State = "Connected" });
            Devices.Add(new DeviceStatus { Name = "myCobot", State = "Connected" });
            Devices.Add(new DeviceStatus { Name = "ESP32", State = "Connected" });
            Devices.Add(new DeviceStatus { Name = "AGV1", State = "Connected" });
            Devices.Add(new DeviceStatus { Name = "AGV2", State = "Connected" });
            Devices.Add(new DeviceStatus { Name = "Python", State = "Connected" });
            Devices.Add(new DeviceStatus { Name = "DB", State = "Connected" });
        }

        private void InitializeAgv()
        {
            AgvList.Add(new AgvStatus
            {
                AgvId = 1,
                State = "대기",
                PositionX = 120,
                PositionY = 100,
                Destination = "—",
                TrayClass = "—",
                ItemCount = 0
            });
            AgvList.Add(new AgvStatus
            {
                AgvId = 2,
                State = "대기",
                PositionX = 60,
                PositionY = 180,
                Destination = "—",
                TrayClass = "—",
                ItemCount = 0,
                Node = 7  // H1에서 출발
            });

            // AGV 초기 상태 기록
            _lastAgvStates[1] = "대기";
            _lastAgvStates[2] = "대기";
        }

        private void LoadMockData()
        {
            // ── DB 기반 집계 ──
            using var db = new AppDbContext();
            var all = db.InspectionResults.ToList();

            int total = all.Count;
            int pass = all.Count(r => r.Result == "양품");
            int defect = all.Count(r => r.Result != "양품");

            int classA = all.Count(r => r.Class == "A");
            int classB = all.Count(r => r.Class == "B");
            int classC = all.Count(r => r.Class == "C");
            int classD = all.Count(r => r.Class == "D");

            int defA = all.Count(r => r.Class == "A" && r.Result != "양품");
            int defB = all.Count(r => r.Class == "B" && r.Result != "양품");
            int defC = all.Count(r => r.Class == "C" && r.Result != "양품");
            int defD = all.Count(r => r.Class == "D" && r.Result != "양품");

            // 분류 현황
            Summary = new ClassificationSummary
            {
                TotalCount = total,
                PassCount = pass,
                DefectCount = defect,
                ClassACount = classA,
                ClassBCount = classB,
                ClassCCount = classC,
                ClassDCount = classD
            };
            Summary.YieldRate = total > 0 ? Math.Round(100.0 * pass / total, 1) : 0;

            // 마지막 검출
            var last = all.OrderByDescending(r => r.Timestamp).FirstOrDefault();
            if (last != null)
            {
                LastComponent = last.ComponentType;
                LastClass = last.Class;
                LastConfidence = last.Confidence;
                LastResult = last.Result == "양품" ? "PASS" : "DEFECT";
                LastGate = last.GateUsed;
            }

            // 사이클 초기화
            CurrentCycle = total;

            // 불량 통계
            DefectRate = total > 0 ? Math.Round(100.0 * defect / total, 1) : 0;
            var topDefect = all.Where(r => r.DefectCode != "PASS")
                .GroupBy(r => r.DefectCode)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            TopDefectCode = topDefect?.Key ?? "—";
            DefectClassA = defA;
            DefectClassB = defB;
            DefectClassC = defC;
            DefectClassD = defD;

            // 파레토
            var defectGroups = all.Where(r => r.DefectCode != "PASS")
                .GroupBy(r => r.DefectCode)
                .OrderByDescending(g => g.Count());
            foreach (var g in defectGroups)
                DefectPareto.Add(new DefectTypeCount { DefectCode = g.Key, Count = g.Count() });

            // 부품별 불량 유형 (도넛 차트용)
            _classDefectCounts["A_파손"] = all.Count(r => r.Class == "A" && r.DefectCode == "BROKEN");
            _classDefectCounts["A_핀휨"] = all.Count(r => r.Class == "A" && r.DefectCode == "BENT_PIN");
            _classDefectCounts["B_핀휨"] = all.Count(r => r.Class == "B" && r.DefectCode == "BENT_PIN");
            _classDefectCounts["C_파손"] = all.Count(r => r.Class == "C" && (r.DefectCode == "DENTED" || r.DefectCode == "BROKEN"));
            _classDefectCounts["D_파손"] = all.Count(r => r.Class == "D" && r.DefectCode == "BROKEN");
            ClassDefectCountsVersion++;

            // ── 시간대별 생산 추이 (오늘 기준) ──
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            var todayItems = all.Where(r => r.Timestamp.StartsWith(today)).ToList();
            var prodByHour = new int[12]; // 08~19시
            var defByHour = new int[12];
            foreach (var r in todayItems)
            {
                if (int.TryParse(r.Timestamp.Substring(11, 2), out int hr))
                {
                    int idx = hr - 8;
                    if (idx >= 0 && idx < 12)
                    {
                        prodByHour[idx]++;
                        if (r.Result != "양품") defByHour[idx]++;
                    }
                }
            }

            ProductionSeries = new ISeries[]
            {
                new LineSeries<int>
                {
                    Values = prodByHour,
                    Name = "생산량",
                    Stroke = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 2.5f },
                    Fill = new SolidColorPaint(new SKColor(0x3B, 0x82, 0xF6, 0x30)),
                    GeometrySize = 0,
                    LineSmoothness = 0.4
                },
                new LineSeries<int>
                {
                    Values = defByHour,
                    Name = "불량",
                    Stroke = new SolidColorPaint(SKColors.IndianRed) { StrokeThickness = 2f },
                    Fill = new SolidColorPaint(new SKColor(0xEF, 0x53, 0x50, 0x20)),
                    GeometrySize = 0,
                    LineSmoothness = 0.4
                }
            };

            ProductionXAxes = new Axis[]
            {
                new Axis
                {
                    Labels = Enumerable.Range(8, 12)
                        .Select(x => $"{x:00}시")
                        .ToArray(),
                    LabelsPaint = new SolidColorPaint(new SKColor(0x94, 0xA3, 0xB8))
                    {
                        SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic")
                    },
                    TextSize = 10,
                    SeparatorsPaint = new SolidColorPaint(new SKColor(0x1E, 0x2D, 0x4A))
                    {
                        StrokeThickness = 0.5f
                    }
                }
            };

            ProductionYAxes = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(new SKColor(0x94, 0xA3, 0xB8)),
                    TextSize = 10,
                    SeparatorsPaint = new SolidColorPaint(new SKColor(0x1E, 0x2D, 0x4A)) { StrokeThickness = 0.5f },
                    MinLimit = 0
                }
            };

            // 오늘 이벤트 로그 로드
            var todayEvents = db.SystemEvents
                .Where(e => e.Timestamp.StartsWith(today))
                .OrderByDescending(e => e.Timestamp)
                .Take(50)
                .ToList();
            foreach (var evt in todayEvents)
                EventLog.Add(evt);
        }

        // ═══════════════════════════════════════
        //   PUBLIC API METHODS
        // ═══════════════════════════════════════

        /// <summary>
        /// 카메라 프레임 업데이트는 CameraStreamService(MJPEG/HTTP)가 담당한다.
        /// 통신 구조 A에서 영상은 MQTT(base64)로 받지 않으므로, 기존 base64
        /// 디코딩 메서드(UpdateCameraFrame)는 제거되었다.
        /// </summary>

        /// <summary>
        /// 분류 결과 수신 시 호출
        /// </summary>
        public void UpdateClassificationResult(ClassificationMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LastComponent = msg.Name;
                LastClass = msg.Class;
                LastConfidence = msg.Confidence;
                LastResult = msg.Result;
                LastGate = msg.GateUsed;

                Summary.TotalCount++;

                bool isDefect = false;
                string resultUpper = msg.Result?.ToUpper() ?? "";
                switch (resultUpper)
                {
                    case "PASS":
                    case "양품":
                    case "NEEDED":
                        Summary.PassCount++;
                        // 양품 → 트레이 슬롯 채우기 (part_type → 슬롯 매핑)
                        string slotId = msg.Name switch
                        {
                            "IC칩" => "A",
                            "터미널블록" => "B",
                            "방열판" => "C",
                            "커패시터" => "D",
                            _ => ""
                        };
                        var slot = TraySlots.FirstOrDefault(s => s.SlotId == slotId);
                        slot?.Fill(slotId);
                        // 4개 다 채워지면 3초 후 자동 리셋
                        if (TraySlots.All(s => s.IsFilled))
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(3000);
                                Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    foreach (var s in TraySlots) s.Clear();
                                });
                            });
                        }
                        break;
                    case "DEFECT":
                    case "불량":
                        Summary.DefectCount++;
                        isDefect = true;
                        break;
                    case "DUPLICATE":
                        // 중복 검출 — 통계 제외, DB에는 기록
                        Summary.TotalCount--;
                        AppDbContext.InsertInspection(new InspectionResult
                        {
                            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            ComponentType = msg.Name,
                            Class = msg.Class,
                            DefectCode = msg.DefectCode,
                            Result = msg.Result,
                            Confidence = msg.Confidence,
                            CycleTimeMs = msg.CycleTimeMs,
                            GateUsed = msg.GateUsed
                        });
                        return;
                    case "UNCERTAIN":
                        // 판정 보류 — 통계 제외, DB에는 기록
                        Summary.TotalCount--;
                        AppDbContext.InsertInspection(new InspectionResult
                        {
                            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            ComponentType = msg.Name,
                            Class = msg.Class,
                            DefectCode = msg.DefectCode,
                            Result = msg.Result,
                            Confidence = msg.Confidence,
                            CycleTimeMs = msg.CycleTimeMs,
                            GateUsed = msg.GateUsed
                        });
                        return;
                }

                switch (msg.Class)
                {
                    case "A": Summary.ClassACount++; break;
                    case "B": Summary.ClassBCount++; break;
                    case "C": Summary.ClassCCount++; break;
                    case "D": Summary.ClassDCount++; break;
                }

                // 불량 통계 업데이트
                if (isDefect)
                {
                    switch (msg.Class)
                    {
                        case "A": DefectClassA++; break;
                        case "B": DefectClassB++; break;
                        case "C": DefectClassC++; break;
                        case "D": DefectClassD++; break;
                    }

                    // 파레토 업데이트
                    var existing = DefectPareto.FirstOrDefault(d => d.DefectCode == msg.DefectCode);
                    if (existing != null)
                    {
                        existing.Count++;
                        // ObservableCollection은 아이템 속성 변경을 감지 못하므로 직접 호출
                        RebuildDefectChart();
                    }
                    else
                    {
                        DefectPareto.Add(new DefectTypeCount { DefectCode = msg.DefectCode, Count = 1 });
                        // Add는 CollectionChanged → RebuildDefectChart 자동 호출됨
                    }

                    // 최다 불량 코드 갱신
                    TopDefectCode = DefectPareto.OrderByDescending(d => d.Count).First().DefectCode;

                    // 부품별 불량 유형 카운트 업데이트 (도넛 차트용)
                    var defectType = MapDefectType(msg.DefectCode);
                    IncrementClassDefect(msg.Class, defectType);

                    // 최근 불량 리스트 (최대 30건)
                    var defectResult = new InspectionResult
                    {
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ComponentType = msg.Name,
                        Class = msg.Class,
                        DefectCode = msg.DefectCode,
                        Result = msg.Result,
                        Confidence = msg.Confidence,
                        GateUsed = msg.GateUsed
                    };
                    RecentDefects.Insert(0, defectResult);
                    while (RecentDefects.Count > 30)
                        RecentDefects.RemoveAt(RecentDefects.Count - 1);
                }

                // 불량률 갱신
                DefectRate = Summary.TotalCount > 0
                    ? Math.Round((double)Summary.DefectCount / Summary.TotalCount * 100, 1)
                    : 0;

                string snapshotPath = SaveSnapshotImage();

                AppDbContext.InsertInspection(new InspectionResult
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ComponentType = msg.Name,
                    Class = msg.Class,
                    DefectCode = msg.DefectCode,
                    Result = msg.Result,
                    Confidence = msg.Confidence,
                    CycleTimeMs = msg.CycleTimeMs,
                    GateUsed = msg.GateUsed
                });
            });
        }

        /// <summary>
        /// 장비 연결 상태 업데이트
        /// </summary>
        public void UpdateDeviceStatus(string deviceName, string state)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var device = Devices.FirstOrDefault(d => d.Name == deviceName);
                if (device != null)
                {
                    device.State = state;
                    device.LastHeartbeat = DateTime.Now;
                }
            });
        }

        /// <summary>
        /// 실제 통신 기준 연결 상태 확인.
        /// - Camera / myCobot / ESP32 / AGV: 각 장비의 실제 메시지나 heartbeat가 들어온 경우만 초록
        /// - 일정 시간 메시지가 없으면 자동 빨강
        /// - Python: MQTT broker 연결 상태 기준
        /// - DB: SQLite DB 접속 가능 여부 기준
        /// </summary>
        private void CheckRealDeviceConnections()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var now = DateTime.Now;

                foreach (var device in Devices)
                {
                    // MQTT 메시지가 없는 장비는 워치독 제외
                    if (device.Name is "Python" or "DB" or "myCobot" or "ESP32" or "AGV1" or "AGV2")
                        continue;

                    if (device.IsConnected && device.LastHeartbeat != DateTime.MinValue &&
                        (now - device.LastHeartbeat).TotalSeconds > 7)
                    {
                        device.State = "Disconnected";
                    }
                }

                SetDeviceStateOnly("Python", _mqttService.IsConnected ? "Connected" : "Disconnected");

                if ((now - _lastDbCheckTime).TotalSeconds >= 5)
                    RefreshDbConnectionStatus();
            });
        }

        private void SetDeviceStateOnly(string deviceName, string state)
        {
            var device = Devices.FirstOrDefault(d => d.Name == deviceName);
            if (device != null)
                device.State = state;
        }

        private void RefreshDbConnectionStatus()
        {
            _lastDbCheckTime = DateTime.Now;
            try
            {
                using var db = new AppDbContext();
                SetDeviceStateOnly("DB", db.Database.CanConnect() ? "Connected" : "Disconnected");
            }
            catch
            {
                SetDeviceStateOnly("DB", "Disconnected");
            }
        }

        /// <summary>
        /// 이벤트 로그 추가
        /// </summary>
        public void AddLog(string source, string eventType, string message)
        {
            var evt = new SystemEvent
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Source = source,
                EventType = eventType,
                Message = message
            };

            // 오늘 날짜인 경우만 화면 EventLog에 추가
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (evt.Timestamp.StartsWith(today))
            {
                if (Application.Current?.Dispatcher.CheckAccess() == true)
                    EventLog.Insert(0, evt);
                else
                    Application.Current?.Dispatcher.Invoke(() => EventLog.Insert(0, evt));
            }

            AppDbContext.InsertEvent(evt);
        }

        private string SaveSnapshotImage()
        {
            if (CameraFrame1 == null)
                return "";

            string folder = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "snapshots");

            Directory.CreateDirectory(folder);

            string fileName =
                $"snapshot_{DateTime.Now:yyyyMMdd_HHmmssfff}.png";

            string path = Path.Combine(folder, fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CameraFrame1));

            using var stream = new FileStream(path, FileMode.Create);

            encoder.Save(stream);

            return path;
        }

        /// <summary>
        /// Python 백엔드에 제어 명령 전송 (통신 구조 A: REST POST).
        /// MQTT publish와 달리 HTTP 응답으로 성공/실패를 확인한다.
        /// action: start / stop / pause / reset / estop ...
        /// </summary>
        public async void SendCommandToPython(string action)
        {
            // action → REST 경로 매핑 (인수인계 문서 기준)
            string path = action switch
            {
                "start" => "api/vision/start",
                "stop" => "api/vision/stop",
                "pause" => "api/vision/stop",
                "conveyor_start" => "api/conveyor/start",
                "conveyor_stop" => "api/conveyor/stop",
                "reset" => "api/reset",
                "emergency_stop" => "api/emergency_stop",
                "emergency_stop_robot" => "api/emergency_stop",
                "emergency_stop_conveyor" => "api/emergency_stop",
                "robot_transfer" => "api/robot/transfer",
                _ => $"api/{action}"
            };

            var result = await _restService.PostAsync(path);
            LogCommandResult(action, result);
        }

        /// <summary>REST 응답(200/409 등)을 로그·UI에 반영</summary>
        private void LogCommandResult(string action, RestService.CommandResult result)
        {
            if (result.Ok)
                AddLog("REST", "INFO", $"명령 성공: {action} (200)");
            else if (result.Conflict)
                AddLog("REST", "WARNING", $"명령 거부: {action} (409 — 현재 상태에서 불가)");
            else
                AddLog("REST", "ERROR", $"명령 실패: {action} ({result.StatusCode}) {result.Message}");
        }

        /// <summary>
        /// 컨베이어 속도 설정 (REST: POST /api/conveyor/start 또는 /api/conveyor/stop)
        /// </summary>
        public async void SetConveyorSpeed(int speed)
        {
            RestService.CommandResult result;
            if (speed > 0)
                result = await _restService.ConveyorStartAsync();
            else
                result = await _restService.ConveyorStopAsync();
            LogCommandResult($"conveyor/{(speed > 0 ? "start" : "stop")}", result);
        }

        // ── AGV MQTT 명령 전송 ──

        /// <summary>
        /// AGV에 MQTT 명령 전송 (visipick/agv/{id}/command)
        /// payload: GO_WAREHOUSE_1, GO_WAREHOUSE_2, TRAYS_READY_3,
        ///          LEAVE_HOME1_TO_START, LEAVE_HOME2_TO_START, LEAVE_HOME3_TO_START
        /// </summary>
        public async void SendAgvCommand(int agvId, string command)
        {
            string topic = $"visipick/agv/{agvId}/command";
            await _mqttService.PublishAsync(topic, command);
            AddLog($"AGV{agvId}", "INFO", $"명령 전송: {command}");
        }

        /// <summary>목적지 설정 (AGV 아직 출발 안 함)</summary>
        public void AgvSetWarehouse(int agvId, int warehouseNumber)
            => SendAgvCommand(agvId, $"GO_WAREHOUSE_{warehouseNumber}");

        /// <summary>트레이 3개 적재 완료 → AGV 출발 허가</summary>
        public void AgvTraysReady(int agvId)
            => SendAgvCommand(agvId, "TRAYS_READY_3");

        /// <summary>홈에서 출발점 복귀 명령 (홈번호별)</summary>
        public void AgvLeaveHomeToStart(int agvId, int homeNumber)
            => SendAgvCommand(agvId, $"LEAVE_HOME{homeNumber}_TO_START");

        /// <summary>
        /// CSV 내보내기
        /// </summary>
        public void ExportLogsToCsv()
        {
            try
            {
                // 당일 로그만 필터링
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                var todayLogs = new System.Collections.ObjectModel.ObservableCollection<SystemEvent>(
                    EventLog.Where(e => e.Timestamp.StartsWith(today)));

                string path = CsvExportService.ExportLogs(todayLogs);
                AddLog("System", "INFO", $"CSV 내보내기 완료 ({today}, {todayLogs.Count}건): {path}");
                MessageBox.Show($"오늘({today}) 로그 {todayLogs.Count}건 저장:\n{path}", "CSV Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog("System", "ERROR", $"CSV 내보내기 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// DB에서 오늘 날짜 로그만 불러와 EventLog에 적재
        /// </summary>
        public void LoadTodayLogs()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            LoadLogsByDate(today);
        }

        /// <summary>
        /// DB에서 특정 날짜 로그 불러오기
        /// </summary>
        public void LoadLogsByDate(string date)
        {
            try
            {
                using var db = new AppDbContext();
                // StartsWith → EF Core SQLite에서 LIKE 'date%' 로 변환됨
                var logs = db.SystemEvents
                    .Where(e => e.Timestamp.StartsWith(date))
                    .OrderByDescending(e => e.Timestamp)
                    .ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    EventLog.Clear();
                    foreach (var log in logs)
                        EventLog.Add(log);
                });

                AddLog("System", "INFO", $"{date} 로그 {logs.Count}건 불러옴");
            }
            catch (Exception ex)
            {
                AddLog("System", "ERROR", $"로그 불러오기 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// DB에 저장된 날짜 목록 반환
        /// </summary>
        public List<string> GetAvailableLogDates()
        {
            try
            {
                using var db = new AppDbContext();
                // Substring은 EF Core SQLite 미지원 → ToList() 후 클라이언트에서 처리
                return db.SystemEvents
                    .Select(e => e.Timestamp)
                    .ToList()
                    .Select(t => t.Length >= 10 ? t[..10] : t)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// 비상정지 — 로봇팔 + 컨베이어만 정지, 카메라는 계속 동작
        /// </summary>
        public async void EmergencyStop()
        {
            _isRunning = false;
            _elapsedTimer.Stop();
            SystemState = "비상정지";
            SystemMode = "E-STOP";

            // 비상정지: 단일 REST 호출 (POST /api/emergency_stop) — 전체 정지
            var estop = await _restService.EmergencyStopAsync();
            LogCommandResult("emergency_stop", estop);

            // AGV 즉시 정지 (REST)
            var agvStop = await _restService.AgvStopAsync();
            LogCommandResult("agv/stop", agvStop);

            // 컨베이어 속도 0
            _conveyorSpeed = 0;
            OnPropertyChanged(nameof(ConveyorSpeed));
            SetConveyorSpeed(0);

            // 로봇·컨베이어·AGV만 Error, 카메라·Python·DB는 유지
            foreach (var d in Devices)
            {
                if (d.Name is "myCobot" or "AGV1" or "AGV2" or "ESP32")
                    d.State = "Error";
            }

            AddLog("System", "ERROR", "⚠ 비상정지 발동");

            // View에 팝업 + 경보음
            Application.Current.Dispatcher.Invoke(() => EmergencyOccurred?.Invoke());
        }

        /// <summary>
        /// 비상정지 해제 — 컨베이어 시작 시 호출하여 에러 상태 복구
        /// </summary>
        public void RecoverFromEmergency()
        {
            if (SystemMode != "E-STOP") return;
            SystemMode = "AUTO";
            SystemState = "대기";
            foreach (var d in Devices)
            {
                if (d.State == "Error")
                    d.State = "Disconnected";
            }
            AddLog("System", "INFO", "비상정지 해제 — 시스템 복구");
        }

        /// <summary>
        /// 로그아웃/창 닫힐 때 모든 리소스 정리
        /// </summary>
        public async void Cleanup()
        {
            _clockTimer.Stop();
            _elapsedTimer.Stop();
            _deviceWatchdogTimer.Stop();
            _cameraStream.StopAll();
            await _mqttService.DisconnectAsync();
        }

        /// <summary>
        /// 외부(View)에서 강제 갱신 요청 — 탭 전환 시 Collapsed→Visible 시점에 호출
        /// </summary>
        public void ForceRebuildDefectChart() => RebuildDefectChart();

        /// <summary>
        /// DefectPareto 컬렉션 기반으로 파레토 차트를 재구성
        /// </summary>
        private void RebuildDefectChart()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var sorted = DefectPareto.OrderByDescending(d => d.Count).ToList();
                if (sorted.Count == 0)
                {
                    DefectSeries = Array.Empty<ISeries>();
                    DefectXAxes = Array.Empty<Axis>();
                    return;
                }

                DefectSeries = new ISeries[]
                {
                    new ColumnSeries<int>
                    {
                        Values      = sorted.Select(d => d.Count).ToArray(),
                        Name        = "불량 건수",
                        Fill        = new SolidColorPaint(new SKColor(0xEF, 0x53, 0x50)),
                        MaxBarWidth = 30,
                        Rx = 4, Ry = 4,
                        DataLabelsPaint = new SolidColorPaint(new SKColor(0xE2, 0xE8, 0xF0)),
                        DataLabelsSize  = 9,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top
                    }
                };

                DefectXAxes = new Axis[]
                {
                    new Axis
                    {
                        Labels      = sorted.Select(d => d.DefectCode).ToArray(),
                        LabelsPaint = new SolidColorPaint(new SKColor(0x94, 0xA3, 0xB8)),
                        TextSize    = 9,
                        SeparatorsPaint = new SolidColorPaint(new SKColor(0x1E, 0x2D, 0x4A)) { StrokeThickness = 0.5f }
                    }
                };

                DefectYAxes = new Axis[]
                {
                    new Axis
                    {
                        LabelsPaint = new SolidColorPaint(new SKColor(0x94, 0xA3, 0xB8)),
                        TextSize    = 10,
                        SeparatorsPaint = new SolidColorPaint(new SKColor(0x1E, 0x2D, 0x4A)) { StrokeThickness = 0.5f },
                        MinLimit    = 0
                    }
                };
            });
        }

        // ── Control Commands ──

        public void StartSystem()
        {
            _isRunning = true;
            _startTime = DateTime.Now;
            _elapsedTimer.Start();
            SystemState = "운전중";
            SystemMode = "AUTO";
            SendCommandToPython("start");
            AddLog("System", "INFO", "시스템 시작");
        }

        public void PauseSystem()
        {
            _isRunning = false;
            _elapsedTimer.Stop();
            SystemState = "일시정지";
            SendCommandToPython("pause");
            AddLog("System", "WARNING", "시스템 일시정지");
        }

        public void StopSystem()
        {
            _isRunning = false;
            _elapsedTimer.Stop();
            SystemState = "정지";
            SendCommandToPython("stop");
            AddLog("System", "INFO", "시스템 정지");
        }

        public void ResetSystem()
        {
            _isRunning = false;
            _elapsedTimer.Stop();
            ElapsedTime = "00:00";
            CurrentCycle = 0;
            SystemState = "대기";
            SystemMode = "AUTO";

            Summary = new ClassificationSummary();

            // 불량 통계도 초기화
            DefectRate = 0;
            TopDefectCode = "—";
            DefectClassA = 0;
            DefectClassB = 0;
            DefectClassC = 0;
            DefectClassD = 0;
            DefectPareto.Clear();
            ResetClassDefectCounts();
            RecentDefects.Clear();
            foreach (var slot in TraySlots) slot.Clear();

            foreach (var d in Devices)
                d.State = "Disconnected";

            _lastAgvStates.Clear();
            _lastAgvStates[1] = "";
            _lastAgvStates[2] = "";

            SendCommandToPython("reset");
            AddLog("System", "INFO", "시스템 리셋");
        }

        /// <summary>
        /// 날짜 변경 시 자동 초기화 — 통계·사이클·로그 전부 리셋 후 오늘 로그 로드
        /// </summary>
        private void DailyReset()
        {
            _isRunning = false;
            _elapsedTimer.Stop();
            ElapsedTime = "00:00";
            CurrentCycle = 0;
            SystemState = "대기";
            SystemMode = "AUTO";

            Summary = new ClassificationSummary();

            DefectRate = 0;
            TopDefectCode = "—";
            DefectClassA = 0;
            DefectClassB = 0;
            DefectClassC = 0;
            DefectClassD = 0;
            DefectPareto.Clear();
            ResetClassDefectCounts();
            RecentDefects.Clear();
            foreach (var slot in TraySlots) slot.Clear();

            // 이벤트 로그 — 오늘 날짜로 새로 로드
            LoadTodayLogs();

            AddLog("System", "INFO", "일일 자동 초기화 완료");
        }

        public void ClearLogs()
        {
            EventLog.Clear();
            AddLog("System", "INFO", "로그 초기화");
        }

        /// <summary>
        /// MQTT 연결 시도
        /// </summary>
        public async Task ConnectMqtt()
        {
            // 이전 로그 DB에서 불러오기
            LoadTodayLogs();

            // ── visipick/inspection — 검사 결과 ──
            _mqttService.OnInspectionReceived += (obj) =>
            {
                var msg = new ClassificationMessage
                {
                    PartType = obj["part_type"]?.ToString() ?? "",
                    Classification = obj["classification"]?.ToString() ?? "NEEDED",
                    DefectCode = obj["defect_code"]?.ToString() ?? "NONE",
                    Confidence = obj["confidence"] != null
                                        ? Convert.ToDouble(obj["confidence"]) : 0,
                    GateAction = obj["gate_action"]?.ToString() ?? "PASS_THROUGH",
                    CycleTimeMs = obj["cycle_time_ms"] != null
                                        ? Convert.ToInt32(obj["cycle_time_ms"]) : 0,
                    RecipeSessionId = obj["recipe_session_id"] != null
                                        ? Convert.ToInt32(obj["recipe_session_id"]) : 0,
                    Timestamp = obj["timestamp"]?.ToString() ?? ""
                };

                UpdateClassificationResult(msg);

                // 사이클 카운트 증가
                Application.Current.Dispatcher.Invoke(() => CurrentCycle++);

                // 카메라 장비 상태 갱신 (검사 결과가 들어오면 카메라 살아있음)
                UpdateDeviceStatus("Camera1", "Connected");
            };

            // ── visipick/agv/{id}/status — AGV 상태 (ESP32 JSON) ──
            _mqttService.OnAgvStatusReceived += (obj) =>
            {
                string agvIdStr = obj["agv_id"]?.ToString() ?? "";
                string digits = new string(agvIdStr.Where(char.IsDigit).ToArray());
                int.TryParse(digits, out int agvId);

                // 일반 상태 메시지 처리
                string status = obj["status"]?.ToString() ?? "Unknown";
                string nextAction = obj["next_action"]?.ToString() ?? "";
                string mission = obj["mission"]?.ToString() ?? "";

                // status 값에서 노드 위치 추론
                int node = status switch
                {
                    "AT_START" => 0,
                    "BRANCH_LEFT" => 2,
                    "BRANCH_RIGHT" => 2,
                    "ARRIVED_WH1" => 5,
                    "ARRIVED_WH2" => 6,
                    "HOME_AREA" => 1,
                    "HOME_1_DONE" => 7,  // H1
                    "HOME_2_DONE" => 8,  // H2
                    "HOME_3_DONE" => 9,  // H3
                    "RETURNING" => -1,
                    "GOING_START" => -1,
                    _ => -1  // 변경 없음
                };

                bool home1Free = obj["home1_free"]?.ToObject<bool>() ?? true;
                bool home2Free = obj["home2_free"]?.ToObject<bool>() ?? true;
                bool home3Free = obj["home3_free"]?.ToObject<bool>() ?? true;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var agv = AgvList.FirstOrDefault(a => a.AgvId == agvId);
                    if (agv != null)
                    {
                        agv.State = status;
                        if (node >= 0) agv.Node = node;
                        agv.NextAction = nextAction;
                        agv.Mission = mission;
                        agv.Home1Free = home1Free;
                        agv.Home2Free = home2Free;
                        agv.Home3Free = home3Free;
                        agv.Destination = mission;
                    }

                    string deviceName = $"AGV{agvId}";
                    string deviceState = status switch
                    {
                        "STANDBY" or "IDLE" or "PENDING_WH1" or "PENDING_WH2" => "Connected",
                        "TRACKING" or "RETURNING" or "GOING_START" or "HOME_EXIT" => "Moving",
                        "OBSTACLE_STOP" => "Blocked",
                        "EMERGENCY" => "Error",
                        _ => "Connected"
                    };
                    UpdateDeviceStatus(deviceName, deviceState);

                    _lastAgvStates.TryGetValue(agvId, out string? prevState);
                    if (prevState != status)
                    {
                        _lastAgvStates[agvId] = status;
                        AddLog($"AGV{agvId}", "INFO",
                            $"상태: {status} | 임무: {mission}");

                        // ── AGV 미션 DB 기록 ──
                        if (status == "MISSION_WH1" || status == "MISSION_WH2")
                        {
                            var m = new AgvMission
                            {
                                AgvId = agvId,
                                StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                Source = "적재 포인트",
                                Destination = status == "MISSION_WH1" ? "공정 1" : "공정 2",
                                TrayClass = agv?.TrayClass ?? "—",
                                ItemCount = 3,
                                Status = "운반중"
                            };
                            AppDbContext.InsertMission(m);
                            // 방금 삽입한 미션 ID 추적
                            using var dbq = new AppDbContext();
                            var last = dbq.AgvMissions.OrderByDescending(x => x.Id).FirstOrDefault();
                            if (last != null) _activeAgvMissionIds[agvId] = last.Id;
                        }
                        else if (status.StartsWith("HOME_") && status.EndsWith("_DONE") ||
                                 status == "AT_START")
                        {
                            if (_activeAgvMissionIds.TryGetValue(agvId, out int missionId))
                            {
                                using var dbq = new AppDbContext();
                                var m = dbq.AgvMissions.Find(missionId);
                                if (m != null)
                                {
                                    m.EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                    m.Status = "완료";
                                    dbq.SaveChanges();
                                }
                                _activeAgvMissionIds.Remove(agvId);
                            }
                        }
                    }
                });
            };

            // ── visipick/agv/{id}/rfid — RFID UID 감지 ──
            _mqttService.OnAgvRfidReceived += (obj) =>
            {
                string agvIdStr = obj["agv_id"]?.ToString() ?? "";
                string digits = new string(agvIdStr.Where(char.IsDigit).ToArray());
                int.TryParse(digits, out int agvId);

                string uid = obj["rfid_uid"]?.ToString()?.ToUpper() ?? "";

                // UID → 노드 매핑
                int rfidNode = uid switch
                {
                    "D6:B9:39:F4" => 0,  // START
                    "3B:47:3D:5A" => 1,  // HOME_JUNCTION
                    "3B:47:42:5A" => 2,  // JUNCTION
                    "E7:C0:3B:27" => 3,  // SELECT_LEFT
                    "3B:47:44:5A" => 4,  // SELECT_RIGHT
                    "3B:47:45:5A" => 5,  // WAREHOUSE_1
                    "2A:7A:61:E1" => 6,  // WAREHOUSE_2
                    _ => -1
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var agv = AgvList.FirstOrDefault(a => a.AgvId == agvId);
                    if (agv != null)
                    {
                        agv.RfidUid = uid;
                        if (rfidNode >= 0) agv.Node = rfidNode;

                        if (rfidNode == 2)
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(5000);
                                Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    if (agv.Node == 2) agv.Node = 0;
                                });
                            });
                        }
                    }
                    AddLog($"AGV{agvId}", "INFO", $"RFID: {uid} → 노드 {rfidNode}");
                });
            };

            // ── visipick/system/state — FSM 상태 전이 ──
            _mqttService.OnSystemStateReceived += (obj) =>
            {
                string fsmState = obj["state"]?.ToString() ?? "";
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // FSM → UI 상태 매핑
                    SystemState = fsmState switch
                    {
                        "IDLE" => "대기",
                        "RUNNING" => "운전중",
                        "TRAY_TRANSFER" => "트레이 이재중",
                        "COMPLETE" => "완료",
                        "ERROR" => "오류",
                        "EMERGENCY_STOP" => "비상정지",
                        _ => fsmState
                    };

                    // Python 서버가 응답한다 = 연결됨
                    UpdateDeviceStatus("Python", "Connected");

                    // FSM 상태에 따라 경과 타이머 시작/정지
                    if (fsmState == "EMERGENCY_STOP" && SystemMode != "E-STOP")
                    {
                        SystemMode = "E-STOP";
                        _isRunning = false;
                        _elapsedTimer.Stop();
                        foreach (var d in Devices)
                        {
                            if (d.Name is "myCobot" or "AGV1" or "AGV2" or "ESP32")
                                d.State = "Error";
                        }
                        AddLog("System", "ERROR", "⚠ 비상정지 감지 (하드웨어 스위치)");
                        EmergencyOccurred?.Invoke();
                    }

                    if (fsmState == "RUNNING" && !_isRunning)
                    {
                        _isRunning = true;
                        _startTime = DateTime.Now;
                        _elapsedTimer.Start();
                    }
                    else if (fsmState != "RUNNING" && _isRunning)
                    {
                        _isRunning = false;
                        _elapsedTimer.Stop();
                    }

                    AddLog("System", "INFO", $"FSM 상태 전이 → {fsmState}");
                });
            };

            // ── visipick/system/event — 이벤트 로그 ──
            _mqttService.OnSystemEventReceived += (obj) =>
            {
                string source = obj["source"]?.ToString() ?? "System";
                string eventType = obj["event_type"]?.ToString() ?? "INFO";
                string message = obj["message"]?.ToString() ?? "";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddLog(source, eventType, message);

                    // 장비별 상태 추론: 이벤트가 도착하면 해당 장비는 살아있음
                    if (source == "Camera")
                        UpdateDeviceStatus("Camera1", "Connected");
                    else if (source == "Robot")
                        UpdateDeviceStatus("myCobot", "Connected");
                    else if (source == "Gate" || source == "Conveyor")
                        UpdateDeviceStatus("ESP32", "Connected");
                });
            };

            try
            {
                await _mqttService.ConnectAsync(AppConfig.Host, AppConfig.MqttPort);

                UpdateDeviceStatus("Python", "Connected");
                RefreshDbConnectionStatus();

                AddLog("MQTT", "INFO", $"MQTT Broker 연결 완료 ({AppConfig.Host}:{AppConfig.MqttPort})");
            }
            catch (Exception ex)
            {
                AddLog("MQTT", "ERROR", $"MQTT 연결 실패: {ex.Message}");
            }

            // 카메라 영상 폴링 시작 — 상부(top) + 측면(side) 동시
            _cameraStream.StartBoth(fps: 10);
            AddLog("Camera", "INFO",
                $"영상 수신 시작 (top: {CameraStreamService.GetStreamUrl(1)}, " +
                $"side: {CameraStreamService.GetStreamUrl(2)})");
        }

        // ── INotifyPropertyChanged ──
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 불량 유형별 카운트
    /// </summary>
    public class DefectTypeCount : INotifyPropertyChanged
    {
        private string _defectCode = "";
        private int _count;

        public string DefectCode
        {
            get => _defectCode;
            set { _defectCode = value; OnPropertyChanged(); }
        }

        public int Count
        {
            get => _count;
            set { _count = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}