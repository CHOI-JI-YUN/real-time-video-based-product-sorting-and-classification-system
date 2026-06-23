using System.Windows;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VisiPickHMI
{
    public partial class EmergencyAlertWindow : MetroWindow
    {
        private WaveOutEvent? _waveOut;
        private CancellationTokenSource? _cts;

        public EmergencyAlertWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => StartSiren();
            Closed += (_, _) => StopSiren();
        }

        private void StartSiren()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(() =>
            {
                const int sampleRate = 44100;

                // 사이렌 파형: 주파수가 저음→고음→저음 반복 (sweep)
                // 한 사이클 = 0.6초 (600ms)
                var provider = new SirenSampleProvider(sampleRate);

                _waveOut = new WaveOutEvent { DesiredLatency = 80 };
                _waveOut.Init(provider);
                _waveOut.Play();

                // 취소될 때까지 재생 유지
                token.WaitHandle.WaitOne();

                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }, token);
        }

        private void StopSiren()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            StopSiren();
            Close();
        }
    }

    /// <summary>
    /// 무한 사이렌 사인파 생성기
    /// 저음(700Hz) → 고음(1400Hz) → 저음(700Hz) 주기적 sweep
    /// </summary>
    internal class SirenSampleProvider : ISampleProvider
    {
        private readonly WaveFormat _format;
        private double _phase;
        private long _sampleIndex;

        // 사이렌 한 사이클: 0.7초
        private const double CycleSeconds = 0.7;
        private const double FreqLow  = 700.0;
        private const double FreqHigh = 1500.0;

        public SirenSampleProvider(int sampleRate)
        {
            _format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat => _format;

        public int Read(float[] buffer, int offset, int count)
        {
            int sr = _format.SampleRate;
            double cycleLen = CycleSeconds * sr; // 사이클 샘플 수

            for (int i = 0; i < count; i++)
            {
                // 사이클 내 위치 (0.0 ~ 1.0)
                double t = (_sampleIndex % (long)cycleLen) / cycleLen;

                // 삼각파로 주파수 sweep: 0→1→0
                double freqT = t < 0.5 ? t * 2.0 : (1.0 - t) * 2.0;
                double freq = FreqLow + (FreqHigh - FreqLow) * freqT;

                // 사인파 생성
                _phase += 2.0 * Math.PI * freq / sr;
                if (_phase > 2.0 * Math.PI) _phase -= 2.0 * Math.PI;

                buffer[offset + i] = (float)(Math.Sin(_phase) * 0.7);
                _sampleIndex++;
            }

            return count; // 무한 재생
        }
    }
}
