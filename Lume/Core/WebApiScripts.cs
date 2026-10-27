namespace VividDesk.Core;

/// <summary>
/// JS-инъекция, выполняемая в каждом веб-обоях до загрузки страницы.
/// Устанавливает публичный API window.wallpaper (getProperties / onPropertiesChanged / onMouseMove /
/// onAudio / onTime) и внутренние механизмы: CSS-фильтры, скорость анимаций, лимит FPS, пауза, звук.
/// </summary>
public static class WebApiScripts
{
    public const string ApiScript = @"
(function () {
  if (window.__vdInstalled) return; window.__vdInstalled = true;

  var props = {};
  var listeners = { properties: [], mouse: [], audio: [], time: [] };

  function emit(kind, data) {
    (listeners[kind] || []).forEach(function (cb) { try { cb(data); } catch (e) { console.warn('[wallpaper] listener error', e); } });
  }

  var api = {
    getProperties: function (cb) {
      if (!cb) return props;
      try { cb(JSON.parse(JSON.stringify(props))); } catch (e) {}
      if (!Object.keys(props).length && window.chrome && window.chrome.webview)
        window.chrome.webview.postMessage({ type: 'requestProperties' });
    },
    onPropertiesChanged: {
      addListener: function (cb) { if (cb) listeners.properties.push(cb); },
      removeListener: function (cb) { var i = listeners.properties.indexOf(cb); if (i >= 0) listeners.properties.splice(i, 1); }
    },
    onMouseMove: { addListener: function (cb) { if (cb) listeners.mouse.push(cb); } },
    onAudio:     { addListener: function (cb) { if (cb) listeners.audio.push(cb); } },
    onTime:      { addListener: function (cb) { if (cb) listeners.time.push(cb); } }
  };
  try { Object.defineProperty(window, 'wallpaper', { value: api }); } catch (e) { window.wallpaper = api; }

  /* ---------- встроенные регулировки ---------- */
  var rate = 1, paused = false, muted = true;

  function applyVisual(v) {
    var parts = [];
    if (v.brightness != null && v.brightness !== 1) parts.push('brightness(' + v.brightness + ')');
    if (v.contrast   != null && v.contrast   !== 1) parts.push('contrast('   + v.contrast   + ')');
    if (v.saturation != null && v.saturation !== 1) parts.push('saturate('  + v.saturation + ')');
    var f = parts.join(' ');
    document.documentElement.style.filter = f;
  }

  function updateRate() {
    try { document.getAnimations().forEach(function (a) { a.playbackRate = rate; }); } catch (e) {}
    var m = document.querySelectorAll('video, audio');
    for (var i = 0; i < m.length; i++) { try { m[i].playbackRate = rate; } catch (e) {} }
  }

  function setPaused(p) {
    paused = p;
    try { document.getAnimations().forEach(function (a) { if (p) a.pause(); else a.play(); }); } catch (e) {}
    var m = document.querySelectorAll('video, audio');
    for (var i = 0; i < m.length; i++) { try { if (p) m[i].pause(); else m[i].play(); } catch (e) {} }
  }

  function setMuted(m) {
    muted = m;
    var els = document.querySelectorAll('video, audio');
    for (var i = 0; i < els.length; i++) els[i].muted = m;
  }

  // новые медиа-элементы и анимации подхватываются автоматически
  var mo = new MutationObserver(function () { setMuted(muted); if (!paused) updateRate(); });
  mo.observe(document.documentElement, { childList: true, subtree: true });

  /* ---------- лимит FPS: пропуск кадров в requestAnimationFrame ---------- */
  var fpsLimit = 0, lastT = 0;
  var origRaf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = function (cb) {
    return origRaf(function tick(t) {
      if (paused || (fpsLimit > 0 && (t - lastT) < 1000 / fpsLimit - 1)) { origRaf(tick); return; }
      lastT = t; cb(t);
    });
  };

  /* ---------- сообщения от приложения ---------- */
  function onHostMessage(e) {
    var m = e.data; if (!m || !m.type) return;
    switch (m.type) {
      case 'properties': props = m.properties || {}; emit('properties', JSON.parse(JSON.stringify(props))); break;
      case 'visual':     applyVisual(m); break;
      case 'rate':       rate = m.value; updateRate(); break;
      case 'fps':        fpsLimit = Math.max(0, m.value | 0); break;
      case 'pause':      setPaused(m.value); break;
      case 'mute':       setMuted(m.value); break;
      case 'mousemove':  emit('mouse', m.point); break;
      case 'audio':      emit('audio', m.bands); break;
      case 'time':       emit('time', m.time); break;
    }
  }
  if (window.chrome && window.chrome.webview) window.chrome.webview.addEventListener('message', onHostMessage);
})();
";
}
