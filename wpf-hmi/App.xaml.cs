using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;
using VisiPickHMI.Data;

namespace VisiPickHMI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 앱이 마지막 윈도우 닫힐 때 자동 종료되지 않도록
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDbContext.Initialize();

            LiveCharts.Configure(config =>
                config.HasGlobalSKTypeface(
                    SKFontManager.Default.MatchCharacter('가')
                )
            );

            ShowLogin();
        }

        /// <summary>
        /// 로그인 창 표시 (최초 + 로그아웃 후 재호출)
        /// </summary>
        public void ShowLogin()
        {
            var login = new LoginWindow();

            login.LoginSucceeded += (user) =>
            {
                var main = new MainWindow(user);
                main.Show();
                login.Close();
            };

            login.Closed += (_, _) =>
            {
                // 로그인 성공으로 MainWindow가 열려있으면 무시
                // MainWindow 없이 로그인 창만 닫히면 앱 종료
                if (Windows.Cast<Window>().All(w => w is LoginWindow || !w.IsVisible))
                    Shutdown();
            };

            login.Show();
        }
    }
}
