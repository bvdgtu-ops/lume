using System.IO;
using LibVLCSharp.Shared;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace VividDesk.Core;

/// <summary>
/// Генерация миниатюр. Для видео — честный кадр через libvlc_video_take_snapshot (скрытое окно рендера),
/// для веб-обоев — программная градиентная заглушка с названием.
/// </summary>
public static class ThumbnailGenerator
{
    private static IntPtr _hiddenHwnd;

    /// <summary>Снимает кадр из видео (MP4/WebM/GIF/MKV…) в PNG-файл. Возвращает false при неудаче.</summary>
    public static bool FromVideo(string videoPath, string outputPng, int width = 512)
    {
        try
        {
            using var libvlc = new LibVLC("--no-audio", "--quiet", "--no-osd", "--no-video-title-show", "--vout=direct3d");
            using var player = new MediaPlayer(libvlc)
            {
                EnableHardwareDecoding = false,
                Scale = 0f,
            };
            player.Hwnd = EnsureHiddenWindow();
            using var media = new Media(libvlc, videoPath, FromType.FromPath);
            media.AddOption(":input-repeat=1");
            player.Play(media);

            // ждём, пока декодер выдаст первые кадры (до 6 секунд)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000)
            {
                if (player.Time > 700) break;                    // позиция воспроизведения уже внутри ролика
                System.Threading.Thread.Sleep(100);
            }

            bool ok = false;
            try { ok = player.TakeSnapshot(0, outputPng, (uint)width, 0); }
            catch { ok = false; }

            // takeSnapshot асинхронный — ждём появления файла
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (ok && !File.Exists(outputPng) && deadline.ElapsedMilliseconds < 4000)
                System.Threading.Thread.Sleep(100);

            try { player.Stop(); } catch { }
            return ok && File.Exists(outputPng);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Snapshot failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Рисует PNG-заглушку (градиент + текст) — для веб-обоев и при неудачном снимке видео.</summary>
    public static void DrawPlaceholder(string outputPng, string title, string subtitle,
        SD.Color from, SD.Color to)
    {
        const int w = 512, h = 288;
        using var bmp = new SD.Bitmap(w, h);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            using var brush = new SD.Drawing2D.LinearGradientBrush(new SD.Rectangle(0, 0, w, h), from, to, 35f);
            g.FillRectangle(brush, 0, 0, w, h);
            // лёгкая «звёздная» текстура, чтобы миниатюра не была плоской
            var rnd = new Random(title.GetHashCode());
            for (int i = 0; i < 90; i++)
            {
                int alpha = 40 + rnd.Next(160);
                int size = rnd.Next(3) == 0 ? 3 : 1;
                using var star = new SD.SolidBrush(SD.Color.FromArgb(alpha, SD.Color.White));
                g.FillEllipse(star, rnd.Next(w), rnd.Next(h), size, size);
            }
            g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAlias;
            using var titleFont = new SD.Font("Segoe UI", 20, SD.FontStyle.Bold);
            using var subFont = new SD.Font("Segoe UI", 11);
            using var textBrush = new SD.SolidBrush(SD.Color.White);
            g.DrawString(title, titleFont, textBrush, 18, h - 74);
            using var dim = new SD.SolidBrush(SD.Color.FromArgb(190, SD.Color.White));
            g.DrawString(subtitle, subFont, dim, 20, h - 40);
        }
        bmp.Save(outputPng, SDI.ImageFormat.Png);
    }

    /// <summary>Скрытое (не отображаемое) Win32-окно, в которое libvlc рендерит кадр для снимка.</summary>
    private static IntPtr EnsureHiddenWindow()
    {
        if (_hiddenHwnd != IntPtr.Zero && WinApi.FindWindow("STATIC", "VividDeskThumbVout") != IntPtr.Zero)
            return _hiddenHwnd;

        _hiddenHwnd = WinApi.FindWindow("STATIC", "VividDeskThumbVout");
        if (_hiddenHwnd == IntPtr.Zero)
        {
            _hiddenHwnd = CreateWindowStub("VividDeskThumbVout");
        }
        return _hiddenHwnd;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private static IntPtr CreateWindowStub(string name)
    {
        const uint WS_POPUP = 0x80000000;
        var hwnd = CreateWindowEx(0x00000080 /*WS_EX_TOOLWINDOW*/, "STATIC", name, WS_POPUP,
            -32000, -32000, 640, 360, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return hwnd;
    }
}
