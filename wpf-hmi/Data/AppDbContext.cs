using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VisiPickHMI.Models;

namespace VisiPickHMI.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<InspectionResult> InspectionResults { get; set; } = null!;
        public DbSet<SystemEvent> SystemEvents { get; set; } = null!;
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<AgvMission> AgvMissions { get; set; } = null!;
        public DbSet<SystemSettings> SystemSettings { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            var dbPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "visipick.db");
            options.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InspectionResult>().ToTable("InspectionResults");
            modelBuilder.Entity<SystemEvent>().ToTable("SystemEvents");
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<AgvMission>().ToTable("AgvMissions");
            modelBuilder.Entity<SystemSettings>(e =>
            {
                e.ToTable("SystemSettings");
                e.HasKey(s => s.Key);
            });
        }

        // ═══════════════════════════════════
        //   Initialize
        // ═══════════════════════════════════
        public static void Initialize()
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

            // Users 테이블
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "Users" (
                    "Id"           INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
                    "Username"     TEXT    NOT NULL,
                    "PasswordHash" TEXT    NOT NULL,
                    "DisplayName"  TEXT    NOT NULL,
                    "Role"         TEXT    NOT NULL,
                    "CreatedAt"    TEXT    NOT NULL,
                    "IsActive"     INTEGER NOT NULL
                );
                """);

            // AgvMissions 테이블
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "AgvMissions" (
                    "Id"          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "AgvId"       INTEGER NOT NULL,
                    "StartTime"   TEXT    NOT NULL,
                    "EndTime"     TEXT,
                    "Source"      TEXT    NOT NULL,
                    "Destination" TEXT    NOT NULL,
                    "TrayClass"   TEXT    NOT NULL,
                    "ItemCount"   INTEGER NOT NULL,
                    "Status"      TEXT    NOT NULL
                );
                """);

            // SystemSettings 테이블
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "SystemSettings" (
                    "Key"         TEXT NOT NULL PRIMARY KEY,
                    "Value"       TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Category"    TEXT NOT NULL,
                    "UpdatedAt"   TEXT NOT NULL,
                    "UpdatedBy"   TEXT NOT NULL
                );
                """);

            // 기본 Admin 계정
            if (!db.Users.Any())
            {
                db.Users.Add(new User
                {
                    Username = "admin",
                    PasswordHash = HashPassword("admin1234"),
                    DisplayName = "관리자",
                    Role = "Admin",
                    IsActive = true
                });
                db.SaveChanges();
            }

            // 기본 시스템 설정값 시드
            SeedDefaultSettings(db);

            // 시연용 데모 데이터 시드 (6/10, 6/11, 6/12)
            SeedDemoData(db);
        }

        private static void SeedDefaultSettings(AppDbContext db)
        {
            var defaults = new (string Key, string Value, string Desc, string Cat)[]
            {
                ("conv_speed_default",  "15",   "컨베이어 기본 속도 (mm/s)",         "Conveyor"),
                ("conv_speed_min",      "5",    "컨베이어 최소 속도 (mm/s)",         "Conveyor"),
                ("conv_speed_max",      "30",   "컨베이어 최대 속도 (mm/s)",         "Conveyor"),
                ("classify_area_min",   "500",  "분류 최소 면적 (px²)",             "Vision"),
                ("classify_area_max",   "50000","분류 최대 면적 (px²)",             "Vision"),
                ("classify_conf_min",   "0.70", "분류 최소 신뢰도 (0~1)",           "Vision"),
                ("target_cycle",        "50",   "목표 사이클 수",                   "System"),
                ("heartbeat_interval",  "2",    "장비 Heartbeat 주기 (초)",         "System"),
            };

            foreach (var (key, val, desc, cat) in defaults)
            {
                if (!db.SystemSettings.Any(s => s.Key == key))
                {
                    db.SystemSettings.Add(new SystemSettings
                    {
                        Key = key,
                        Value = val,
                        Description = desc,
                        Category = cat,
                        UpdatedBy = "System"
                    });
                }
            }
            db.SaveChanges();
        }

        // ═══════════════════════════════════
        //   User CRUD
        // ═══════════════════════════════════
        public static string HashPassword(string password)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes).ToLower();
        }

        public static User? Authenticate(string username, string password)
        {
            using var db = new AppDbContext();
            string hash = HashPassword(password);
            return db.Users.FirstOrDefault(u =>
                u.Username == username &&
                u.PasswordHash == hash &&
                u.IsActive);
        }

        /// <summary>
        /// 비밀번호 일치 여부만 확인 (활성 여부 무시) — 비활성 계정 안내용
        /// </summary>
        public static User? FindUserByCredentials(string username, string password)
        {
            using var db = new AppDbContext();
            string hash = HashPassword(password);
            return db.Users.FirstOrDefault(u =>
                u.Username == username &&
                u.PasswordHash == hash);
        }

        public static List<User> GetAllUsers()
        {
            using var db = new AppDbContext();
            return db.Users.OrderBy(u => u.Id).ToList();
        }

        public static void AddUser(User user)
        {
            using var db = new AppDbContext();
            db.Users.Add(user);
            db.SaveChanges();
        }

        public static void UpdateUser(User user)
        {
            using var db = new AppDbContext();
            db.Users.Update(user);
            db.SaveChanges();
        }

        public static void DeleteUser(int id)
        {
            using var db = new AppDbContext();
            var u = db.Users.Find(id);
            if (u != null) { db.Users.Remove(u); db.SaveChanges(); }
        }

        // ═══════════════════════════════════
        //   Event / Inspection Insert
        // ═══════════════════════════════════
        public static void InsertEvent(SystemEvent evt)
        {
            try
            {
                using var db = new AppDbContext();
                db.SystemEvents.Add(new SystemEvent
                {
                    Timestamp = evt.Timestamp,
                    Source = evt.Source,
                    EventType = evt.EventType,
                    Message = evt.Message
                });
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("이벤트 저장 실패: " + ex.Message);
            }
        }

        public static void InsertInspection(InspectionResult result)
        {
            try
            {
                using var db = new AppDbContext();
                db.InspectionResults.Add(new InspectionResult
                {
                    Timestamp = result.Timestamp,
                    ComponentType = result.ComponentType,
                    Class = result.Class,
                    DefectCode = result.DefectCode,
                    Result = result.Result,
                    Confidence = result.Confidence,
                    CycleTimeMs = result.CycleTimeMs,
                    GateUsed = result.GateUsed
                });
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("검사결과 저장 실패: " + ex.Message);
            }
        }

        // ═══════════════════════════════════
        //   AGV Mission
        // ═══════════════════════════════════
        public static void InsertMission(AgvMission m)
        {
            using var db = new AppDbContext();
            db.AgvMissions.Add(m);
            db.SaveChanges();
        }

        public static void UpdateMission(AgvMission m)
        {
            using var db = new AppDbContext();
            db.AgvMissions.Update(m);
            db.SaveChanges();
        }

        public static void DeleteMission(int id)
        {
            using var db = new AppDbContext();
            var m = db.AgvMissions.Find(id);
            if (m != null) { db.AgvMissions.Remove(m); db.SaveChanges(); }
        }

        // ═══════════════════════════════════
        //   Admin: Inspection CRUD
        // ═══════════════════════════════════
        public static void UpdateInspection(InspectionResult r)
        {
            using var db = new AppDbContext();
            db.InspectionResults.Update(r);
            db.SaveChanges();
        }

        public static void DeleteInspection(int id)
        {
            using var db = new AppDbContext();
            var r = db.InspectionResults.Find(id);
            if (r != null) { db.InspectionResults.Remove(r); db.SaveChanges(); }
        }

        public static void DeleteInspections(List<int> ids)
        {
            using var db = new AppDbContext();
            var items = db.InspectionResults.Where(r => ids.Contains(r.Id)).ToList();
            db.InspectionResults.RemoveRange(items);
            db.SaveChanges();
        }

        // ═══════════════════════════════════
        //   Admin: Event CRUD
        // ═══════════════════════════════════
        public static void DeleteEvent(int id)
        {
            using var db = new AppDbContext();
            var e = db.SystemEvents.Find(id);
            if (e != null) { db.SystemEvents.Remove(e); db.SaveChanges(); }
        }

        public static int DeleteEventsByDate(string date)
        {
            using var db = new AppDbContext();
            var items = db.SystemEvents.Where(e => e.Timestamp.StartsWith(date)).ToList();
            db.SystemEvents.RemoveRange(items);
            db.SaveChanges();
            return items.Count;
        }

        public static int DeleteAllEvents()
        {
            using var db = new AppDbContext();
            var count = db.SystemEvents.Count();
            db.SystemEvents.RemoveRange(db.SystemEvents);
            db.SaveChanges();
            return count;
        }

        // ═══════════════════════════════════
        //   Settings CRUD
        // ═══════════════════════════════════
        public static List<SystemSettings> GetAllSettings()
        {
            using var db = new AppDbContext();
            return db.SystemSettings.OrderBy(s => s.Category).ThenBy(s => s.Key).ToList();
        }

        public static string GetSetting(string key, string defaultValue = "")
        {
            using var db = new AppDbContext();
            var s = db.SystemSettings.Find(key);
            return s?.Value ?? defaultValue;
        }

        public static void SaveSetting(string key, string value, string updatedBy)
        {
            using var db = new AppDbContext();
            var s = db.SystemSettings.Find(key);
            if (s != null)
            {
                s.Value = value;
                s.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                s.UpdatedBy = updatedBy;
                db.SaveChanges();
            }
        }

        // ═══════════════════════════════════
        //   Demo Data Seed (시연용)
        // ═══════════════════════════════════
        private static void SeedDemoData(AppDbContext db)
        {
            // 이미 데모 데이터가 있으면 스킵
            if (db.InspectionResults.Any(r => r.Timestamp.StartsWith("2026-06-10")))
                return;

            var rng = new Random(42); // 고정 시드 → 매번 동일 데이터

            // 부품 정의: (ComponentType, Class, GateUsed, 가능한 불량코드)
            var parts = new[]
            {
                ("IC칩",      "A", 1, new[] { "BENT_PIN", "BROKEN" }),
                ("터미널블록", "B", 2, new[] { "BENT_PIN" }),
                ("방열판",     "C", 3, new[] { "DENTED" }),
                ("커패시터",   "D", 4, new[] { "BROKEN" }),
            };

            var inspections = new List<InspectionResult>();
            var events = new List<SystemEvent>();
            var missions = new List<AgvMission>();

            var dayConfigs = new[]
            {
                (Date: "2026-06-10", Total: 200, Defects: 11, StartH: 8, EndH: 19),
                (Date: "2026-06-11", Total: 190, Defects: 11, StartH: 8, EndH: 19),
                (Date: "2026-06-12", Total: 100, Defects:  3, StartH: 8, EndH: 11),
                (Date: "2026-06-13", Total:  80, Defects:  3, StartH: 8, EndH: 11),
            };

            int missionId = 1;

            foreach (var day in dayConfigs)
            {
                // 불량 위치를 미리 결정
                var defectIndices = new HashSet<int>();
                while (defectIndices.Count < day.Defects)
                    defectIndices.Add(rng.Next(day.Total));

                for (int i = 0; i < day.Total; i++)
                {
                    var part = parts[rng.Next(parts.Length)];
                    bool isDefect = defectIndices.Contains(i);

                    // 시간 분배
                    double hoursSpan = day.EndH - day.StartH;
                    double h = day.StartH + (hoursSpan * i / day.Total) + rng.NextDouble() * 0.05;
                    int hour = Math.Min((int)h, day.EndH - 1);
                    int minute = rng.Next(60);
                    int second = rng.Next(60);
                    string ts = $"{day.Date} {hour:D2}:{minute:D2}:{second:D2}";

                    string defectCode = isDefect
                        ? part.Item4[rng.Next(part.Item4.Length)]
                        : "PASS";
                    string result = isDefect ? "불량" : "양품";
                    double conf = isDefect
                        ? Math.Round(0.55 + rng.NextDouble() * 0.30, 2)
                        : Math.Round(0.88 + rng.NextDouble() * 0.11, 2);

                    inspections.Add(new InspectionResult
                    {
                        Timestamp = ts,
                        ComponentType = part.Item1,
                        Class = part.Item2,
                        DefectCode = defectCode,
                        Result = result,
                        Confidence = conf,
                        CycleTimeMs = rng.Next(900, 2200),
                        GateUsed = part.Item3
                    });

                    // 주요 이벤트 로그 (모든 건 말고 일부만)
                    if (i % 8 == 0)
                    {
                        events.Add(new SystemEvent
                        {
                            Timestamp = ts,
                            Source = "Camera",
                            EventType = "INFO",
                            Message = $"{part.Item1} 검출 — Class {part.Item2}, Conf: {conf:F2}"
                        });

                        if (isDefect)
                        {
                            events.Add(new SystemEvent
                            {
                                Timestamp = ts,
                                Source = "Vision",
                                EventType = "WARNING",
                                Message = $"불량 감지: {part.Item1} — {defectCode}"
                            });
                        }
                    }
                }

                // AGV 미션 (하루에 3~4건)
                int missionsPerDay = day.Date == "2026-06-12" ? 1 : 3;
                for (int m = 0; m < missionsPerDay; m++)
                {
                    int wh = rng.Next(1, 3); // 1 or 2
                    int startHour = day.StartH + 2 + m * 3;
                    string mStart = $"{day.Date} {startHour:D2}:{rng.Next(10, 50):D2}:00";
                    string mEnd = $"{day.Date} {startHour:D2}:{rng.Next(50, 59):D2}:00";
                    string[] classes = { "A", "B", "C" };

                    missions.Add(new AgvMission
                    {
                        AgvId = 1,
                        StartTime = mStart,
                        EndTime = mEnd,
                        Source = "적재 포인트",
                        Destination = $"공정 {wh}",
                        TrayClass = classes[m % 3],
                        ItemCount = 3,
                        Status = "완료"
                    });
                }

                // 시스템 시작/종료 로그
                events.Add(new SystemEvent
                {
                    Timestamp = $"{day.Date} {day.StartH:D2}:00:05",
                    Source = "System",
                    EventType = "INFO",
                    Message = "시스템 시작 — AUTO 모드"
                });
                events.Add(new SystemEvent
                {
                    Timestamp = $"{day.Date} {day.StartH:D2}:00:10",
                    Source = "MQTT",
                    EventType = "INFO",
                    Message = "MQTT Broker 연결 완료"
                });
                events.Add(new SystemEvent
                {
                    Timestamp = $"{day.Date} {day.StartH:D2}:01:00",
                    Source = "Camera",
                    EventType = "INFO",
                    Message = "영상 수신 시작 (top + side)"
                });

                if (day.Date != "2026-06-12") // 오늘은 아직 운영중
                {
                    events.Add(new SystemEvent
                    {
                        Timestamp = $"{day.Date} {day.EndH:D2}:00:00",
                        Source = "System",
                        EventType = "INFO",
                        Message = $"일일 생산 완료 — 총 {day.Total}건, 불량 {day.Defects}건"
                    });
                }

                // ESP32, 로봇 이벤트 추가
                for (int e = 0; e < 4; e++)
                {
                    int eh = day.StartH + 1 + e * 2;
                    if (eh >= day.EndH) break;

                    events.Add(new SystemEvent
                    {
                        Timestamp = $"{day.Date} {eh:D2}:{rng.Next(10, 50):D2}:00",
                        Source = "Robot",
                        EventType = "INFO",
                        Message = $"Pick 버퍼{(char)('A' + e % 4)} → 트레이 적재 완료"
                    });
                    events.Add(new SystemEvent
                    {
                        Timestamp = $"{day.Date} {eh:D2}:{rng.Next(50, 59):D2}:00",
                        Source = "ESP32",
                        EventType = "INFO",
                        Message = $"Gate{(e % 4) + 1} OPEN → 슈트 {(char)('A' + e % 4)}"
                    });
                }
            }

            db.InspectionResults.AddRange(inspections);
            db.SystemEvents.AddRange(events);
            db.AgvMissions.AddRange(missions);
            db.SaveChanges();
        }
    }
}