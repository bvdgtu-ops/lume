using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VividDesk.Core;

/// <summary>
/// Встроенные веб-обои, создаваемые при первом запуске в папке библиотеки.
/// Каждый — полноценная интерактивная страница, использующая window.wallpaper API
/// (пользовательские свойства, мышь, аудио-визуализатор, время суток).
/// </summary>
public static class BuiltinWallpapers
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void EnsureCreated(string libraryRoot)
    {
        Create(libraryRoot, "Космос", new WallpaperProperty[]
        {
            P("speed", "Скорость звёзд", "slider", 0, 2, 0.1, JNum(1)),
            P("tint", "Оттенок неба", "color", def: JStr("#3366ff")),
            P("showMoon", "Показывать луну", "checkbox", def: JBool(true)),
            P("stars", "Плотность звёзд", "slider", 100, 1500, 50, JNum(600)),
            P("nebula", "Туманность", "select", def: JStr("purple"),
                options: new[] { "purple", "teal", "crimson", "none" }),
        }, SpaceHtml());

        Create(libraryRoot, "Аудио-визуализатор", new WallpaperProperty[]
        {
            P("color", "Цвет полос", "color", def: JStr("#7c5cff")),
            P("barCount", "Количество полос", "slider", 16, 96, 8, JNum(48)),
            P("mirror", "Зеркальные полосы", "checkbox", def: JBool(true)),
            P("glow", "Свечение", "slider", 0, 30, 1, JNum(12)),
        }, AudioVizHtml());

        Create(libraryRoot, "Часы и время суток", new WallpaperProperty[]
        {
            P("showClock", "Показывать часы", "checkbox", def: JBool(true)),
            P("format", "Формат времени", "select", def: JStr("24h"), options: new[] { "24h", "12h" }),
            P("textColor", "Цвет текста", "color", def: JStr("#ffffff")),
            P("showDate", "Показывать дату", "checkbox", def: JBool(true)),
        }, ClockHtml());
    }

    private static WallpaperProperty P(string key, string label, string type,
        double? min = null, double? max = null, double? step = null,
        JsonElement? def = null, string[] options = null) => new()
    {
        Key = key,
        Label = label,
        Type = type,
        Min = min,
        Max = max,
        Step = step,
        Default = def,
        Options = options?.Select(o => new PropertyOption(o)).ToList() ?? new List<PropertyOption>()
    };

    private static JsonElement? JNum(double v) => JsonDocument.Parse(v.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();
    private static JsonElement? JBool(bool v) => JsonDocument.Parse(v ? "true" : "false").RootElement.Clone();
    private static JsonElement? JStr(string v) => JsonDocument.Parse(JsonSerializer.Serialize(v)).RootElement.Clone();

    private static void Create(string root, string title, WallpaperProperty[] properties, string html)
    {
        string dir = Path.Combine(root, title);
        if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "project.json"))) return;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), html);

        var proj = new WallpaperProject
        {
            Title = title,
            Type = "web",
            File = "index.html",
            Preview = "preview.png",
            Description = "Встроенные обои Lume",
            Properties = properties.ToList(),
            FolderPath = dir
        };
        File.WriteAllText(Path.Combine(dir, "project.json"), JsonSerializer.Serialize(proj, JsonOpts));
    }

    // ============================= ОБОИ: КОСМОС =============================
    private static string SpaceHtml() => @"<!DOCTYPE html>
<html lang=""ru"">
<head>
<meta charset=""utf-8"">
<style>
  html, body { margin:0; width:100%; height:100%; overflow:hidden; background:#04060f; }
  canvas { display:block; width:100%; height:100%; }
</style>
</head>
<body>
<canvas id=""c""></canvas>
<script>
(function () {
  var canvas = document.getElementById('c'), ctx = canvas.getContext('2d');
  var props = { speed: 1, tint: '#3366ff', showMoon: true, stars: 600, nebula: 'purple' };
  var mouse = { nx: 0.5, ny: 0.5 };
  var hour = new Date().getHours();

  if (window.wallpaper) {
    window.wallpaper.getProperties(function (p) { if (p) Object.assign(props, p); });
    window.wallpaper.onPropertiesChanged.addListener(function (p) { Object.assign(props, p); });
    window.wallpaper.onMouseMove.addListener(function (pt) { mouse.nx = pt.nx; mouse.ny = pt.ny; });
    window.wallpaper.onTime.addListener(function (t) { hour = t.hour; });
  }

  var stars = [];
  function resize() {
    canvas.width = innerWidth; canvas.height = innerHeight;
    buildStars();
  }
  function buildStars() {
    stars = [];
    var n = Math.round(+props.stars || 600);
    for (var i = 0; i < n; i++) {
      stars.push({
        x: Math.random() * canvas.width, y: Math.random() * canvas.height,
        z: 0.2 + Math.random() * 0.8,
        tw: Math.random() * Math.PI * 2
      });
    }
  }
  addEventListener('resize', resize); resize();

  var nebulaColors = { purple: ['rgba(84,20,180,0.16)', 'rgba(20,60,160,0.13)'],
                       teal:   ['rgba(0,120,120,0.16)', 'rgba(0,60,110,0.13)'],
                       crimson:['rgba(150,20,60,0.16)', 'rgba(60,0,80,0.14)'],
                       none:   null };

  function hexToRgb(h) {
    h = (h || '#3366ff').replace('#', '');
    return [parseInt(h.substr(0,2),16), parseInt(h.substr(2,2),16), parseInt(h.substr(4,2),16)];
  }

  var t0 = performance.now();
  function frame(t) {
    var dt = Math.min(0.05, (t - t0) / 1000); t0 = t;
    var speed = (+props.speed || 0) * dt;

    ctx.fillStyle = '#04060f';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    var neb = nebulaColors[props.nebula] || null;
    if (neb) {
      var g = ctx.createRadialGradient(canvas.width * 0.72, canvas.height * 0.3, 0, canvas.width * 0.72, canvas.height * 0.3, canvas.width * 0.6);
      g.addColorStop(0, neb[0]); g.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = g; ctx.fillRect(0, 0, canvas.width, canvas.height);
      var g2 = ctx.createRadialGradient(canvas.width * 0.2, canvas.height * 0.8, 0, canvas.width * 0.2, canvas.height * 0.8, canvas.width * 0.5);
      g2.addColorStop(0, neb[1]); g2.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = g2; ctx.fillRect(0, 0, canvas.width, canvas.height);
    }

    var tint = hexToRgb(props.tint);
    // ночью небо чуть темнее и холоднее, днём — светлее и теплее
    var dayK = Math.max(0, Math.cos((hour - 13) / 24 * 2 * Math.PI)); // 1 днём, 0 ночью
    var cx = canvas.width / 2, cy = canvas.height / 2;

    for (var i = 0; i < stars.length; i++) {
      var s = stars[i];
      s.tw += dt * 2;
      // движение от центра (эффект полёта) + параллакс от мыши
      var px = (s.x - cx) / s.z, py = (s.y - cy) / s.z;
      var ox = (mouse.nx - 0.5) * 60 / s.z, oy = (mouse.ny - 0.5) * 60 / s.z;
      s.x += (s.x - cx) * speed * 0.12;
      s.y += (s.y - cy) * speed * 0.12;
      if (s.x < 0 || s.x > canvas.width || s.y < 0 || s.y > canvas.height) {
        var ang = Math.random() * Math.PI * 2, r = Math.random() * 0.02 + 0.01;
        s.x = cx + Math.cos(ang) * canvas.width * r;
        s.y = cy + Math.sin(ang) * canvas.height * r;
      }
      var size = s.z * (1.6 + Math.sin(s.tw) * 0.5);
      var alpha = 0.35 + 0.55 * s.z * (0.6 + 0.4 * Math.sin(s.tw));
      ctx.fillStyle = 'rgba(' + Math.round(tint[0] * 0.3 + 179 * dayK * 0.3 + 80) + ',' +
                              Math.round(tint[1] * 0.3 + 200 * dayK * 0.3 + 90) + ',' +
                              Math.round(200 + tint[2] * 0.25 + 40 * dayK) + ',' + alpha.toFixed(2) + ')';
      ctx.fillRect(s.x + ox, s.y + oy, size, size);
    }

    if (props.showMoon !== false) {
      var mx = canvas.width * (0.78 + (mouse.nx - 0.5) * 0.04);
      var my = canvas.height * (0.22 + (mouse.ny - 0.5) * 0.04);
      var mr = Math.min(canvas.width, canvas.height) * 0.07;
      var halo = ctx.createRadialGradient(mx, my, mr * 0.4, mx, my, mr * 3);
      halo.addColorStop(0, 'rgba(235,240,255,0.25)'); halo.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = halo; ctx.beginPath(); ctx.arc(mx, my, mr * 3, 0, 7); ctx.fill();
      ctx.fillStyle = '#e8ecf5'; ctx.beginPath(); ctx.arc(mx, my, mr, 0, 7); ctx.fill();
      ctx.fillStyle = 'rgba(190,197,210,0.55)';
      ctx.beginPath(); ctx.arc(mx - mr * 0.3, my - mr * 0.2, mr * 0.22, 0, 7); ctx.fill();
      ctx.beginPath(); ctx.arc(mx + mr * 0.35, my + mr * 0.3, mr * 0.15, 0, 7); ctx.fill();
    }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
})();
</script>
</body>
</html>";

    // ========================= ОБОИ: АУДИО-ВИЗУАЛИЗАТОР =========================
    private static string AudioVizHtml() => @"<!DOCTYPE html>
<html lang=""ru"">
<head>
<meta charset=""utf-8"">
<style>
  html, body { margin:0; width:100%; height:100%; overflow:hidden; background:#0a0714; }
  canvas { display:block; width:100%; height:100%; }
</style>
</head>
<body>
<canvas id=""c""></canvas>
<script>
(function () {
  var canvas = document.getElementById('c'), ctx = canvas.getContext('2d');
  var props = { color: '#7c5cff', barCount: 48, mirror: true, glow: 12 };
  var bands = new Array(96).fill(0);
  var smooth = new Array(96).fill(0);

  if (window.wallpaper) {
    window.wallpaper.getProperties(function (p) { if (p) Object.assign(props, p); });
    window.wallpaper.onPropertiesChanged.addListener(function (p) { Object.assign(props, p); });
    window.wallpaper.onAudio.addListener(function (b) {
      for (var i = 0; i < Math.min(bands.length, b.length); i++) bands[i] = b[i];
    });
  }

  function hexToRgb(h) {
    h = (h || '#7c5cff').replace('#', '');
    return [parseInt(h.substr(0,2),16), parseInt(h.substr(2,2),16), parseInt(h.substr(4,2),16)];
  }

  function resize() { canvas.width = innerWidth; canvas.height = innerHeight; }
  addEventListener('resize', resize); resize();

  function frame() {
    var w = canvas.width, h = canvas.height;
    ctx.fillStyle = 'rgba(10,7,20,0.35)';
    ctx.fillRect(0, 0, w, h);

    var n = Math.max(8, Math.round(+props.barCount || 48));
    var rgb = hexToRgb(props.color);
    var glow = +props.glow || 0;
    var baseY = props.mirror === false ? h * 0.9 : h / 2;

    if (glow > 0) { ctx.shadowBlur = glow; ctx.shadowColor = 'rgb(' + rgb.join(',') + ')'; }
    else ctx.shadowBlur = 0;

    var bw = w / n;
    for (var i = 0; i < n; i++) {
      var idx = Math.floor(i / n * bands.length);
      var v = bands[idx] || 0;
      smooth[i] += (v - smooth[i]) * 0.35;
      var bh = Math.pow(smooth[i], 0.8) * h * (props.mirror === false ? 0.8 : 0.48) + 4;
      var k = 0.5 + 0.5 * smooth[i];
      ctx.fillStyle = 'rgb(' + Math.min(255, rgb[0] + (255 - rgb[0]) * k * 0.6) + ',' +
                              Math.min(255, rgb[1] + (255 - rgb[1]) * k * 0.5) + ',' +
                              Math.min(255, rgb[2] + (255 - rgb[2]) * k * 0.4) + ')';
      var x = i * bw + bw * 0.15, bwr = bw * 0.7;
      if (props.mirror === false) {
        ctx.fillRect(x, baseY - bh, bwr, bh);
      } else {
        ctx.fillRect(x, baseY - bh, bwr, bh);
        ctx.fillRect(x, baseY, bwr, bh);
      }
    }
    ctx.shadowBlur = 0;
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
})();
</script>
</body>
</html>";

    // ========================= ОБОИ: ЧАСЫ И ВРЕМЯ СУТОК =========================
    private static string ClockHtml() => @"<!DOCTYPE html>
<html lang=""ru"">
<head>
<meta charset=""utf-8"">
<style>
  html, body { margin:0; width:100%; height:100%; overflow:hidden; font-family:'Segoe UI',sans-serif; }
  #bg { position:absolute; inset:0; transition:background 120s linear; }
  #clock { position:absolute; left:50%; top:50%; transform:translate(-50%,-50%); text-align:center; }
  #time { font-size:11vw; font-weight:200; letter-spacing:0.04em; }
  #date { font-size:2.2vw; opacity:.75; margin-top:1vw; }
</style>
</head>
<body>
<div id=""bg""></div>
<div id=""clock"">
  <div id=""time""></div>
  <div id=""date""></div>
</div>
<script>
(function () {
  var props = { showClock: true, format: '24h', textColor: '#ffffff', showWeather: true };
  var now = new Date();
  if (window.wallpaper) {
    window.wallpaper.getProperties(function (p) { if (p) Object.assign(props, p); });
    window.wallpaper.onPropertiesChanged.addListener(function (p) { Object.assign(props, p); });
    window.wallpaper.onTime.addListener(function (t) {
      now = new Date(t.iso);
      render();
    });
  }
  function lerp(a, b, k) { return a + (b - a) * k; }
  // палитра по часам: ночь → рассвет → день → закат → ночь
  var phases = [
    { h: 0,  c: [8, 10, 30] },  { h: 4,  c: [10, 12, 38] },
    { h: 6,  c: [120, 70, 90] },{ h: 8,  c: [200, 140, 90] },
    { h: 10, c: [40, 110, 170] },{ h: 14, c: [30, 130, 190] },
    { h: 18, c: [220, 120, 70] },{ h: 20, c: [70, 35, 80] },
    { h: 22, c: [12, 14, 40] }, { h: 24, c: [8, 10, 30] }
  ];
  function bgFor(hour) {
    for (var i = 0; i < phases.length - 1; i++) {
      var a = phases[i], b = phases[i + 1];
      if (hour >= a.h && hour <= b.h) {
        var k = (hour - a.h) / (b.h - a.h || 1);
        return [Math.round(lerp(a.c[0], b.c[0], k)), Math.round(lerp(a.c[1], b.c[1], k)), Math.round(lerp(a.c[2], b.c[2], k))];
      }
    }
    return phases[0].c;
  }
  function two(n) { return (n < 10 ? '0' : '') + n; }
  function render() {
    var c = bgFor(now.getHours() + now.getMinutes() / 60);
    document.getElementById('bg').style.background =
      'radial-gradient(120% 100% at 50% 0%, rgb(' + c.map(function(x){return Math.min(255,x+25);}).join(',') + ') 0%, rgb(' + c.join(',') + ') 70%)';
    document.getElementById('clock').style.display = props.showClock === false ? 'none' : 'block';
    var tEl = document.getElementById('time');
    var dEl = document.getElementById('date');
    tEl.style.color = props.textColor || '#fff';
    dEl.style.color = props.textColor || '#fff';
    dEl.style.display = props.showWeather === false ? 'none' : 'block';
    var h12 = now.getHours() % 12; if (h12 === 0) h12 = 12;
    tEl.textContent = (props.format === '12h' ? h12 : two(now.getHours())) + ':' + two(now.getMinutes());
    if (props.format === '12h') tEl.textContent += now.getHours() < 12 ? ' AM' : ' PM';
    var days = ['воскресенье','понедельник','вторник','среда','четверг','пятница','суббота'];
    var months = ['января','февраля','марта','апреля','мая','июня','июля','августа','сентября','октября','ноября','декабря'];
    dEl.textContent = days[now.getDay()] + ', ' + now.getDate() + ' ' + months[now.getMonth()];
  }
  render();
  setInterval(render, 1000);
})();
</script>
</body>
</html>";
}
