using System;
using System.IO;
using System.Threading.Tasks;

namespace VividDesk.Core;

/// <summary>
/// Ставит картинку на экран блокировки Windows.
///
/// Почему не UserProfilePersonalizationSettings.TrySetLockScreenImageAsync:
/// из неупакованного exe (без package identity) он молча возвращает false
/// (внутри RoOriginateError 0x80073D54) — проверено на Win10 19045.
/// Рабочий путь — LockScreen.SetImageFileAsync: помечен устаревшим, но для
/// desktop-приложений работает и прав администратора не требует.
///
/// Живое видео/веб на локскрине невозможно: его рисует LogonUI в защищённом
/// десктопе, сторонний процесс туда не попадает. Поэтому на экран блокировки
/// уходит статичный кадр текущих обоев.
/// </summary>
public static class LockScreenService
{
    /// <summary>Картинка, которую показываем на экране блокировки.</summary>
    public static string ImagePath =>
        Path.Combine(ConfigService.AppDataDir, "lock", "lockscreen.png");

    /// <summary>Отдаёт картинку системе. true — Windows её приняла.</summary>
    public static async Task<bool> ApplyAsync(string imagePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return false;
            if (new FileInfo(imagePath).Length < 1024) return false;   // пустой/битый кадр

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
            await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file);
            return true;   // без исключения — Windows картинку приняла
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Lock screen image failed: " + ex.Message);
            return false;
        }
    }
    /// <summary>
    /// Годен ли кадр для экрана блокировки: отбраковываем однотонные заливки
    /// и «зелёный экран», который libvlc отдаёт при аппаратном декодировании.
    /// </summary>
    public static bool IsUsableFrame(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            if (new FileInfo(path).Length < 2048) return false;

            using var bmp = new System.Drawing.Bitmap(path);
            int w = bmp.Width, h = bmp.Height;
            if (w < 32 || h < 32) return false;

            const int cols = 32, rows = 18;
            double sumR = 0, sumG = 0, sumB = 0, sumL = 0, sumL2 = 0;
            int n = 0;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var c = bmp.GetPixel(x * (w - 1) / (cols - 1), y * (h - 1) / (rows - 1));
                    double l = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    sumR += c.R; sumG += c.G; sumB += c.B;
                    sumL += l; sumL2 += l * l;
                    n++;
                }
            }

            double mr = sumR / n, mg = sumG / n, mb = sumB / n;
            double ml = sumL / n;
            double std = Math.Sqrt(Math.Max(0, sumL2 / n - ml * ml));

            if (std < 4.0) return false;                                  // однотонная заливка
            if (mg > 60 && mg > mr * 1.6 && mg > mb * 1.6 && std < 18) return false; // зелёный кадр
            return true;
        }
        catch { return false; }
    }

}
