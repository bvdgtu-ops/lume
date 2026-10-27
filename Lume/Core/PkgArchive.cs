using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VividDesk.Core;

/// <summary>
/// Чтение контейнера Wallpaper Engine (.pkg, сигнатура PKGV).
/// Полноценно отрендерить сцену нельзя — движка WE у нас нет, но из пакета
/// можно вытащить встроенное видео или картинку и показать их как обычные обои.
/// </summary>
public static class PkgArchive
{
    public record Entry(string Name, long Offset, int Length);

    private static readonly string[] MediaExts =
        { ".mp4", ".webm", ".avi", ".mov", ".mkv", ".gif", ".jpg", ".jpeg", ".png" };

    /// <summary>Читает индекс пакета. Возвращает null, если это не PKGV или файл битый.</summary>
    public static List<Entry> ReadIndex(string pkgPath)
    {
        try
        {
            using var fs = File.OpenRead(pkgPath);
            using var br = new BinaryReader(fs);

            uint version = br.ReadUInt32();
            if (version == 0) return null;

            string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != "PKGV") return null;

            br.ReadBytes(4);                       // тег сборки, например "0024"
            uint count = br.ReadUInt32();
            if (count == 0 || count > 100000) return null;

            var list = new List<Entry>((int)count);
            for (uint i = 0; i < count; i++)
            {
                int nameLen = br.ReadInt32();
                if (nameLen <= 0 || nameLen > 1024) return null;
                string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
                uint offset = br.ReadUInt32();
                uint length = br.ReadUInt32();
                if (offset + (long)length > fs.Length) return null;
                list.Add(new Entry(name, offset, (int)length));
            }

            long dataStart = fs.Position;
            foreach (var e in list) e.GetType(); // no-op, индекс уже прочитан
            return TagDataStart(list, dataStart);
        }
        catch { return null; }
    }

    // позиция начала данных нужна при извлечении — храним её в первом «служебном» поле
    private static long _dataStart;

    private static List<Entry> TagDataStart(List<Entry> list, long dataStart)
    {
        _dataStart = dataStart;
        return list;
    }

    /// <summary>Ищет внутри пакета первое медиа (видео приоритетнее картинки).</summary>
    public static Entry FindMedia(string pkgPath)
    {
        var index = ReadIndex(pkgPath);
        if (index == null) return null;

        Entry best = null;
        foreach (var e in index)
        {
            string ext = Path.GetExtension(e.Name).ToLowerInvariant();
            if (!MediaExts.Contains(ext)) continue;

            bool isVideo = ext is ".mp4" or ".webm" or ".avi" or ".mov" or ".mkv" or ".gif";
            if (isVideo) return e;

            if (best == null || e.Length > best.Length) best = e;
        }
        return best;
    }

    /// <summary>Достаёт запись из пакета в файл. Поддерживает zlib и «как есть».</summary>
    public static bool Extract(string pkgPath, Entry entry, string destPath)
    {
        try
        {
            var index = ReadIndex(pkgPath);
            if (index == null) return false;

            using var fs = File.OpenRead(pkgPath);
            fs.Seek(_dataStart + entry.Offset, SeekOrigin.Begin);
            var raw = new byte[entry.Length];
            int read = 0;
            while (read < raw.Length)
            {
                int n = fs.Read(raw, read, raw.Length - read);
                if (n <= 0) break;
                read += n;
            }
            if (read != raw.Length) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? ".");

            // zlib-стрим начинается с 0x78
            if (raw.Length > 2 && raw[0] == 0x78)
            {
                try
                {
                    using var input = new MemoryStream(raw);
                    using var z = new ZLibStream(input, CompressionMode.Decompress);
                    using var output = File.Create(destPath);
                    z.CopyTo(output);
                    return new FileInfo(destPath).Length > 0;
                }
                catch
                {
                    // не сжато — запишем как есть
                }
            }

            File.WriteAllBytes(destPath, raw);
            return true;
        }
        catch { return false; }
    }
}
