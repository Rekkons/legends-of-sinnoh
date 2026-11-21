// 웹 손님판 브라우저 몫(M7 웹 손님 — isekai Plugins/WebGL/IsekaiTextInput.jslib · IsekaiAttention.jslib를 옮겨 한 파일로, 이름 앞 Isekai → Sinnoh).
//   SinnohTextInput_*: 한글 IME 입력(C#: Assets/Sinnoh/Client/Ui/WebTextInput.cs, 02 §8).
//   SinnohAttentionStart: 창이 비활성일 때 내 차례 — 탭 제목 깜빡임(C#: Assets/Sinnoh/Client/AttentionFlash.cs).
//   SinnohLoading: Gen 번들 받기 진행(C#: Assets/Sinnoh/Client/WebContent.cs) → 페이지(WebGLTemplates/Sinnoh/index.html)의 window.sinnohLoading(done, total, ready).
mergeInto(LibraryManager.library, {
  SinnohTextInput_Open__deps: ['$UTF8ToString'],
  SinnohTextInput_Open: function (valuePtr, maxLength, placeholderPtr, x, y, w, h) {
    var st = window.__sinnohTextInput;
    if (!st) {
      st = window.__sinnohTextInput = { el: null, state: -1, text: '', x: 0, y: 0, w: 0, h: 0, openedAt: 0, canvas: null, onResize: null, onDown: null };
      st.place = function () {
        if (!st.el || !st.canvas) return;
        var r = st.canvas.getBoundingClientRect();
        var hh = Math.max(20, st.h * r.height);
        var s = st.el.style;
        s.left = (r.left + st.x * r.width) + 'px';
        s.top = (r.top + st.y * r.height) + 'px';
        s.width = Math.max(40, st.w * r.width) + 'px';
        s.height = hh + 'px';
        s.fontSize = Math.max(12, Math.round(hh * 0.55)) + 'px';
      };
      st.remove = function () {
        if (st.onResize) window.removeEventListener('resize', st.onResize);
        if (st.onDown && st.canvas) st.canvas.removeEventListener('pointerdown', st.onDown, true);
        st.onResize = null;
        st.onDown = null;
        var el = st.el;
        st.el = null;   // 지우면서 오는 blur는 무시된다
        if (el && el.parentNode) el.parentNode.removeChild(el);
        if (el && st.canvas) st.canvas.focus();   // 키 입력을 다시 게임으로
      };
      st.finish = function (state) {
        if (!st.el || st.state !== 0) return;
        st.text = st.el.value;
        st.state = state;
        st.remove();
      };
    }
    st.remove();
    st.canvas = Module['canvas'] || document.querySelector('canvas');
    st.x = x; st.y = y; st.w = w; st.h = h;
    st.text = UTF8ToString(valuePtr);
    st.state = 0;

    var el = document.createElement('input');
    el.type = 'text';
    el.value = st.text;
    el.placeholder = UTF8ToString(placeholderPtr);
    if (maxLength > 0) el.maxLength = maxLength;
    el.autocomplete = 'off';
    el.spellcheck = false;
    el.setAttribute('autocapitalize', 'off');
    el.style.cssText = 'position:fixed;z-index:2147483647;box-sizing:border-box;margin:0;padding:0 0.4em;' +
      'border:2px solid #c9a24a;border-radius:4px;background:#fffdf6;color:#1f1a14;outline:none;font-family:sans-serif;';
    el.addEventListener('input', function () { if (st.el === el) st.text = el.value; });
    el.addEventListener('keydown', function (e) {
      e.stopPropagation();
      if (e.isComposing || e.keyCode === 229) return;   // 조합 확정용 Enter
      if (e.key === 'Enter') { e.preventDefault(); st.finish(1); }
      else if (e.key === 'Escape' || e.key === 'Esc') { e.preventDefault(); st.finish(3); }
    });
    el.addEventListener('keyup', function (e) { e.stopPropagation(); });
    el.addEventListener('keypress', function (e) { e.stopPropagation(); });
    el.addEventListener('blur', function () {
      if (st.el !== el) return;
      // 연 직후 게임이 캔버스로 포커스를 되가져가면(같은 클릭 처리) 닫지 않고 다시 잡는다
      if (performance.now() - st.openedAt < 300) { setTimeout(function () { if (st.el === el) el.focus(); }, 0); return; }
      st.finish(2);
    });
    st.onResize = function () { st.place(); };
    window.addEventListener('resize', st.onResize);
    st.onDown = function () { st.finish(2); };   // 게임 화면을 누르면 확정(캔버스가 기본 동작을 막아 blur가 안 올 수 있다)
    if (st.canvas) st.canvas.addEventListener('pointerdown', st.onDown, true);

    st.el = el;
    var fs = document.fullscreenElement;   // 전체 화면이면 그 안에 넣어야 보인다(캔버스 자체면 자식을 못 넣는다)
    (fs && fs.tagName !== 'CANVAS' ? fs : document.body).appendChild(el);
    st.place();
    st.openedAt = performance.now();
    el.focus();
    try { el.setSelectionRange(el.value.length, el.value.length); } catch (err) { }
  },

  SinnohTextInput_Place: function (x, y, w, h) {
    var st = window.__sinnohTextInput;
    if (!st) return;
    st.x = x; st.y = y; st.w = w; st.h = h;
    st.place();
  },

  SinnohTextInput_State: function () {
    var st = window.__sinnohTextInput;
    return st ? st.state : -1;
  },

  SinnohTextInput_Text__deps: ['$stringToUTF8', '$lengthBytesUTF8'],
  SinnohTextInput_Text: function (buffer, size) {
    var st = window.__sinnohTextInput;
    var text = !st ? '' : st.el ? st.el.value : st.text;
    var n = lengthBytesUTF8(text);
    if (buffer && size > n) stringToUTF8(text, buffer, size);
    return n;
  },

  SinnohTextInput_Close: function () {
    var st = window.__sinnohTextInput;
    if (!st) return;
    st.remove();
    st.state = -1;
  }
,

  SinnohAttentionStart__deps: ['$UTF8ToString'],
  SinnohAttentionStart: function (prefixPtr) {
    var prefix = UTF8ToString(prefixPtr);
    var visible = function () { return document.visibilityState === 'visible' && document.hasFocus(); };
    if (visible()) return;
    var st = window.__sinnohAttention;
    if (st) return;   // 이미 깜빡이는 중
    st = window.__sinnohAttention = { base: document.title, on: false, timer: 0 };
    var stop = function () {
      if (window.__sinnohAttention !== st) return;
      clearInterval(st.timer);
      document.title = st.base;
      window.removeEventListener('focus', stop);
      document.removeEventListener('visibilitychange', onVis);
      window.__sinnohAttention = null;
    };
    var onVis = function () { if (visible()) stop(); };
    st.timer = setInterval(function () {
      if (visible()) { stop(); return; }
      st.on = !st.on;
      document.title = st.on ? prefix + st.base : st.base;
    }, 1000);
    st.on = true;
    document.title = prefix + st.base;
    window.addEventListener('focus', stop);
    document.addEventListener('visibilitychange', onVis);
  }
,

  SinnohLoading: function (done, total, ready) {
    if (typeof window.sinnohLoading === 'function') window.sinnohLoading(done, total, ready);
  }
});
