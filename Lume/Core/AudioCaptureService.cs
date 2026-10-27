using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace VividDesk.Core;

/// <summary>
/// Захват системного звука (WASAPI loopback) + FFT для аудио-визуализаторов.
/// Рассылает 48 нормированных полос спектра подписанным окнам обоев.
/// </summary>
public class AudioCaptureService : IDisposable
{
    private const int FftSize = 2048;          // 1024 полезных бина
    private const int Bands = 48;
    private readonly float[] _mono = new float[FftSize];
    private int _monoPos;
    private WasapiLoopbackCapture _capture;
    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly float[] _lastBands = new float[Bands];
    private readonly object _sync = new();
    private DateTime _lastEmit = DateTime.MinValue;
    private readonly System.Threading.Timer _restartTimer;

    /// <summary>Готов спектр полос (0..1) — вызывается в потоке захвата.</summary>
    public event Action<float[]> BandsReady;

    public bool IsRunning { get; private set; }

    public AudioCaptureService()
    {
        _restartTimer = new System.Threading.Timer(_ => { lock (_sync) { if (!IsRunning) Start(); } },
            null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning) return;
            try
            {
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += (s, e) => StopAndRestartLater();
                _capture.StartRecording();
                IsRunning = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("audiocap: " + ex.Message);
                IsRunning = false;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            IsRunning = false;
            try { _capture?.StopRecording(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _capture = null;
        }
    }

    private void StopAndRestartLater()
    {
        lock (_sync)
        {
            IsRunning = false;
            try { _capture?.Dispose(); } catch { }
            _capture = null;
        }
        // устройство вывода могло смениться — пробуем переподключиться через 2 секунды
        _restartTimer.Change(2000, Timeout.Infinite);
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int channels = _capture.WaveFormat.Channels;
        int sampleCount = e.BytesRecorded / bytesPerSample;

        lock (_sync)
        {
            for (int i = 0; i < sampleCount; i += channels)
            {
                float v = 0;
                for (int ch = 0; ch < channels && (i + ch) < sampleCount; ch++)
                {
                    int offset = (i + ch) * bytesPerSample;
                    if (bytesPerSample == 4) v += BitConverter.ToSingle(e.Buffer, offset);
                    else if (bytesPerSample == 2) v += BitConverter.ToInt16(e.Buffer, offset) / 32768f;
                }
                v /= channels;
                _mono[_monoPos++] = v;
                if (_monoPos >= FftSize) _monoPos = 0;
            }
        }

        // ограничиваем частоту рассылки ~33 Гц
        if ((DateTime.UtcNow - _lastEmit).TotalMilliseconds < 33) return;
        _lastEmit = DateTime.UtcNow;

        lock (_sync)
        {
            // кольцевой буфер -> линейный с окном Ханнинга
            for (int i = 0; i < FftSize; i++)
            {
                double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
                _fftBuffer[i].X = (float)(_mono[(_monoPos + i) % FftSize] * w);
                _fftBuffer[i].Y = 0f;
            }
        }

        FastFourierTransform.FFT(false, (int)Math.Log2(FftSize), _fftBuffer);

        int bins = FftSize / 2;
        var bands = new float[Bands];
        for (int b = 0; b < Bands; b++)
        {
            // логарифмическое распределение полос: низкие частоты — узкие, высокие — широкие
            int from = (int)Math.Pow(bins - 1, b / (double)Bands);
            int to = (int)Math.Pow(bins - 1, (b + 1) / (double)Bands);
            if (to <= from) to = from + 1;
            float max = 0;
            for (int i = from; i < to && i < bins; i++)
            {
                float mag = (float)Math.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X + _fftBuffer[i].Y * _fftBuffer[i].Y);
                if (mag > max) max = mag;
            }
            // нормировка с лёгким сглаживанием по времени
            float norm = Math.Min(1f, max / (bins * 0.12f));
            bands[b] = Math.Max(norm, _lastBands[b] * 0.72f);
            _lastBands[b] = bands[b];
        }
        BandsReady?.Invoke(bands);
    }

    public void Dispose()
    {
        _restartTimer?.Dispose();
        Stop();
    }
}
