using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace VividDesk.Core;

/// <summary>
/// Библиотека обоев: сканирует папку (формат: подпапка с project.json), создаёт project.json
/// для скопированных вручную файлов/папок, генерирует миниатюры, следит за изменениями через FileSystemWatcher.
/// </summary>
public class LibraryService : IDisposable
{
    public static readonly string[] VideoExts = { ".mp4", ".webm", ".gif", ".mkv", ".avi", ".mov", ".m4v", ".ts", ".wmv", ".flv", ".mpg", ".mpeg", ".m2ts", ".ogv" };
    public static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounce;
    private readonly ConfigService _config;

    public string Root => _config.Config.WallpaperDir;
    public List<WallpaperProject> Wallpapers { get; private set; } = new();

    /// <summary>Список обоев изменился (добавление, удаление, готова миниатюра).</summary>
    public event Action WallpapersChanged;

    public LibraryService(ConfigService config)
    {
        _config = config;
        Directory.CreateDirectory(Root);

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500) // пока копируется большой файл — не дёргаемся
        };
        _debounce.Tick += (s, e) => { _debounce.Stop(); Rescan(); };

        _watcher = new FileSystemWatcher(Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        _watcher.Created += OnFsChange;
        _watcher.Renamed += OnFsChange;
        _watcher.Deleted += OnFsChange;
        _watcher.Error += (s, e) => Rescan();
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFsChange(object sender, FileSystemEventArgs e)
    {
        // игнорируем события внутри чужих папок проектов (кроме создания самой папки) — debounce всё равно пересканирует
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Полное сканирование корня библиотеки.</summary>
    public void Rescan()
    {
        Directory.CreateDirectory(Root);
        var found = new List<WallpaperProject>();

        // 1. одиночные видеофайлы, скопированные прямо в корень — превращаем в папки-проекты
        foreach (var file in Directory.GetFiles(Root))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (VideoExts.Contains(ext))
            {
                try { ImportFile(file, moveIntoLibrary: true); } catch { /* файл ещё копируется */ }
            }
        }

        // 2. подпапки (закрытые системные папки вроде System Volume Information пропускаем)
        foreach (var dir in Directory.GetDirectories(Root))
        {
            WallpaperProject proj;
            try { proj = LoadProject(dir); }
            catch (Exception) { continue; } // нет доступа / занято — не наша папка
            if (proj != null) found.Add(proj);
        }

        found.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        Wallpapers = found;
        WallpapersChanged?.Invoke();
    }

    /// <summary>Загружает один проект; при отсутствии project.json — автодетект и создание.</summary>
    public WallpaperProject LoadProject(string dir)
    {
        string pj = Path.Combine(dir, "project.json");
        WallpaperProject proj;
        if (File.Exists(pj))
        {
            try
            {
                proj = JsonSerializer.Deserialize<WallpaperProject>(File.ReadAllText(pj), JsonOpts);
                if (proj == null) return null;
            }
            catch { return null; }
            proj.FolderPath = dir;

            // чужие форматы (например, сцены Wallpaper Engine: "type": "scene") не берём
            string kind = (proj.Type ?? "").Trim().ToLowerInvariant();
            if (kind == "scene")
            {
                // сцена Wallpaper Engine: вытаскиваем видео из .pkg или берём превью
                if (TrySceneFallback(dir, proj)) return proj;
                proj.Problem = "unsupported";
                return proj;
            }
            if (kind != "video" && kind != "web" && kind != "url" && kind != "image") { proj.Problem = "unsupported"; return proj; }
        }
        else
        {
            // папку скопировали вручную: ищем видео или html (перебор может упасть на ACL)
            string video = null, html = null;
            try
            {
                video = Directory.EnumerateFiles(dir).FirstOrDefault(f => VideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()));
                html = Directory.EnumerateFiles(dir, "*.html").FirstOrDefault();
            }
            catch (Exception) { return null; } // нет доступа к папке
            if (video != null)
            {
                proj = new WallpaperProject
                {
                    Title = Path.GetFileName(dir),
                    Type = "video",
                    File = Path.GetFileName(video),
                    FolderPath = dir
                };
            }
            else if (Directory.EnumerateFiles(dir).FirstOrDefault(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())) is string image)
            {
                proj = new WallpaperProject { Title = Path.GetFileName(dir), Type = "image", File = Path.GetFileName(image), FolderPath = dir };
            }
            else if (html != null)
            {
                proj = new WallpaperProject
                {
                    Title = Path.GetFileName(dir),
                    Type = "web",
                    File = Path.GetFileName(html),
                    FolderPath = dir
                };
            }
            else return null;

            SaveProjectFile(proj);
        }

        // проверяем существование целевого файла
        if (proj.Kind == WallpaperType.Url)
        {
            if (string.IsNullOrWhiteSpace(proj.Url)) return null;
        }
        else if (!File.Exists(Path.Combine(dir, proj.File ?? ""))) proj.Problem = "missing";

        // миниатюра: есть файл — хорошо, нет — генерируем в фоне
        if (!string.IsNullOrEmpty(proj.Preview) && !File.Exists(proj.PreviewPath))
            System.Threading.Tasks.Task.Run(() => GenerateThumbnail(proj, notify: true));

        return proj;
    }

    /// <summary>Создаёт project.json в папке проекта.</summary>
    public static void SaveProjectFile(WallpaperProject proj)
    {
        File.WriteAllText(Path.Combine(proj.FolderPath, "project.json"),
            JsonSerializer.Serialize(proj, JsonOpts));
    }

    /// <summary>Миниатюра: кадр видео либо рисованная заглушка.</summary>
    public void GenerateThumbnail(WallpaperProject proj, bool notify)
    {
        try
        {
            if (proj.Kind == WallpaperType.Video)
            {
                string video = Path.Combine(proj.FolderPath, proj.File);
                if (!ThumbnailGenerator.FromVideo(video, proj.PreviewPath))
                    ThumbnailGenerator.DrawPlaceholder(proj.PreviewPath, proj.Title, "видео",
                        SDColorFromTitle(proj.Title), SDColor2());
            }
            else
            {
                ThumbnailGenerator.DrawPlaceholder(proj.PreviewPath, proj.Title,
                    proj.Kind == WallpaperType.Url ? "веб-страница" : "веб-обои",
                    SDColorFromTitle(proj.Title), SDColor2());
            }
            if (notify)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => WallpapersChanged?.Invoke());
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("thumb: " + ex.Message); }
    }

    private static System.Drawing.Color SDColorFromTitle(string t)
    {
        int h = Math.Abs((t ?? "x").GetHashCode()) % 360;
        return System.Drawing.Color.FromArgb(0, Math.Min(255, 60 + h % 120), Math.Min(255, 120 + (h * 2) % 135));
    }
    private static System.Drawing.Color SDColor2() => System.Drawing.Color.FromArgb(18, 12, 40);

    /// <summary>Добавление своего файла (MP4/WebM/GIF или HTML) — создаёт папку-проект, копирует файл, project.json, миниатюру.</summary>
    /// <summary>Результат импорта архива.</summary>
    public class ZipImportResult
    {
        public bool Ok;
        public WallpaperProject Project;
        public string Problem = "";   // "noproject" | "unsupported" | "missing" | текст ошибки
    }

    /// <summary>
    /// Импорт скачанного ZIP: распаковывает во временную папку, находит корень обоев
    /// (project.json или видео/html на любой вложенности — сайты кладут архив в подпапку)
    /// и переносит его в библиотеку.
    /// </summary>
    public ZipImportResult ImportZip(string zipPath)
    {
        var result = new ZipImportResult();
        string temp = Path.Combine(Path.GetTempPath(), "lume_zip_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, temp);

            string root = FindProjectRoot(temp);
            if (root == null) { result.Problem = "noproject"; return result; }

            string dest = UniqueDir(SafeDirName(Path.GetFileNameWithoutExtension(zipPath)));
            Directory.CreateDirectory(dest);
            foreach (var entry in Directory.GetFileSystemEntries(root)) MoveEntry(entry, dest);

            var proj = LoadProject(dest);
            if (proj == null) { result.Problem = "noproject"; return result; }
            if (!string.IsNullOrEmpty(proj.Problem)) { result.Problem = proj.Problem; result.Project = proj; return result; }

            System.Threading.Tasks.Task.Run(() => GenerateThumbnail(proj, notify: true));
            Rescan();
            result.Ok = true;
            result.Project = proj;
            return result;
        }
        catch (Exception ex)
        {
            result.Problem = ex.Message;
            return result;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }
    }

    /// <summary>
    /// Сцена Wallpaper Engine: рендерить её мы не умеем, но можно взять видео из .pkg
    /// или preview-файл рядом с пакетом и показать как обычные обои.
    /// </summary>
    private bool TrySceneFallback(string dir, WallpaperProject proj)
    {
        try
        {
            string pkg = Directory.GetFiles(dir, "*.pkg").FirstOrDefault();
            if (pkg != null)
            {
                var media = PkgArchive.FindMedia(pkg);
                if (media != null)
                {
                    string ext = Path.GetExtension(media.Name).ToLowerInvariant();
                    string outName = "pkg-media" + ext;
                    string dest = Path.Combine(dir, outName);
                    if (!File.Exists(dest) && !PkgArchive.Extract(pkg, media, dest)) dest = null;

                    if (dest != null && File.Exists(dest))
                    {
                        proj.Type = VideoExts.Contains(ext) ? "video" : "image";
                        proj.File = outName;
                        proj.Title = proj.Title + " (медиа из сцены)";
                        proj.Problem = "";
                        return true;
                    }
                }
            }

            foreach (var candidate in new[] { "preview.gif", "preview.jpg", "preview.jpeg", "preview.png" })
            {
                if (!File.Exists(Path.Combine(dir, candidate))) continue;
                proj.Type = candidate.EndsWith(".gif") ? "video" : "image";
                proj.File = candidate;
                proj.Title = proj.Title + " (превью сцены)";
                proj.Problem = "";
                return true;
            }
        }
        catch { }
        return false;
    }
    /// <summary>Ищет папку с обоями внутри распакованного архива (не глубже трёх уровней).</summary>
    private static string FindProjectRoot(string dir, int depth = 0)
    {
        if (depth > 3) return null;
        if (File.Exists(Path.Combine(dir, "project.json"))) return dir;

        try
        {
            if (Directory.GetFiles(dir).Any(f => VideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()))) return dir;
            if (Directory.GetFiles(dir, "*.html").Any()) return dir;
        }
        catch { }

        foreach (var sub in Directory.GetDirectories(dir))
        {
            string found = FindProjectRoot(sub, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static string SafeDirName(string raw)
    {
        string name = string.Join("_", (raw ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
        return string.IsNullOrWhiteSpace(name) ? "wallpaper" : name;
    }

    private static void MoveEntry(string entry, string destDir)
    {
        string target = Path.Combine(destDir, Path.GetFileName(entry));
        if (Directory.Exists(entry)) Directory.Move(entry, target);
        else File.Move(entry, target);
    }

    public WallpaperProject ImportFile(string sourcePath, bool moveIntoLibrary)
    {
        // Проверка дубликата: ищем уже импортированный файл с тем же именем и размером
        try
        {
            long sourceLength = new FileInfo(sourcePath).Length;
            foreach (string existingDir in Directory.EnumerateDirectories(Root))
            {
                try
                {
                    WallpaperProject existing = LoadProject(existingDir);
                    if (existing == null)
                        continue;
    
                    if (!string.Equals(Path.GetFileName(existing.File), Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase))
                        continue;
    
                    string existingPath = Path.Combine(existing.FolderPath, existing.File);
                    if (!File.Exists(existingPath))
                        continue;
    
                    if (new FileInfo(existingPath).Length != sourceLength)
                        continue;
    
                    // Тот же файл уже есть в библиотеке — возвращаем существующий проект без копирования
                    return existing;
                }
                catch
                {
                    // Одна недоступная папка не должна ломать импорт
                }
            }
        }
        catch
        {
            // Если не удалось обойти библиотеку, продолжаем обычный импорт
        }
    
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        bool isVideo = VideoExts.Contains(ext);
        bool isImage = ImageExts.Contains(ext);
        if (!isVideo && !isImage && ext != ".html")
            throw new NotSupportedException("Поддерживаются файлы: " + string.Join(", ", VideoExts) + ", .html");
    
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string dir = UniqueDir(baseName);
        Directory.CreateDirectory(dir);
    
        string dest = Path.Combine(dir, Path.GetFileName(sourcePath));
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            // файл уже лежит внутри своей будущей папки — оставляем как есть
        }
        else if (moveIntoLibrary && IsInsideRoot(Path.GetDirectoryName(sourcePath)))
        {
            File.Move(sourcePath, dest);
        }
        else
        {
            File.Copy(sourcePath, dest, true);
        }
    
        var proj = new WallpaperProject
        {
            Title = baseName,
            Type = isVideo ? "video" : isImage ? "image" : "web",
            File = Path.GetFileName(dest),
            FolderPath = dir
        };
        SaveProjectFile(proj);
        System.Threading.Tasks.Task.Run(() => GenerateThumbnail(proj, notify: true));
        return proj;
    }

    /// <summary>Добавление веб-обоев по URL.</summary>
    public WallpaperProject ImportUrl(string url, string title)
    {
        string dir = UniqueDir(string.IsNullOrWhiteSpace(title) ? "Веб-обои" : title);
        Directory.CreateDirectory(dir);
        var proj = new WallpaperProject
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Type = "url",
            Url = url,
            FolderPath = dir
        };
        SaveProjectFile(proj);
        System.Threading.Tasks.Task.Run(() => GenerateThumbnail(proj, notify: true));
        return proj;
    }

    /// <summary>Смена папки библиотеки: перевешиваем watcher и пересканируем.</summary>
    public void RescanAfterDirChange()
    {
        _watcher.EnableRaisingEvents = false;
        try { _watcher.Path = Root; } catch { }
        _watcher.EnableRaisingEvents = true;
        Rescan();
    }

    private string UniqueDir(string name)    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string dir = Path.Combine(Root, name);
        int i = 2;
        while (Directory.Exists(dir)) dir = Path.Combine(Root, $"{name} ({i++})");
        return dir;
    }

    private bool IsInsideRoot(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string root = Path.GetFullPath(Root).TrimEnd('\\') + "\\";
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Удаление проекта из библиотеки (вместе с файлами).</summary>
    public void Delete(WallpaperProject proj)
    {
        try
        {
            if (Directory.Exists(proj.FolderPath))
            {
                foreach (var f in Directory.GetFiles(proj.FolderPath))
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                Directory.Delete(proj.FolderPath, true);
            }
        }
        finally { Rescan(); }
    }

    public WallpaperProject FindByPath(string folderPath) =>
        Wallpapers.FirstOrDefault(w => string.Equals(w.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Dispose() => _watcher.Dispose();
}
