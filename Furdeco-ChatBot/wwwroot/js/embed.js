/**
 * TrackIT Embed Script v1.0
 * ─────────────────────────────────────────────────────────────────
 * MVC app origin: https://dev.snapsend.co:550
 * Chatbot route:  https://dev.snapsend.co:550/chat
 *
 * USAGE — paste before </body> on any static site:
 *
 *   <!-- Minimal — zero config needed -->
 *   <script src="https://dev.snapsend.co:550/scripts/embed.js"></script>
 *
 *   <!-- Optional custom config (set BEFORE the script tag) -->
 *   <script>
 *     window.TrackITConfig = {
 *       position:    'bottom-right',        // 'bottom-right' | 'bottom-left'
 *       offsetX:     28,                    // px from edge
 *       offsetY:     28,                    // px from bottom
 *       buttonColor: '#3AB54A',             // any CSS hex colour
 *       buttonLabel: 'TrackIT',             // text under icon
 *       badgeCount:  1,                     // red dot number (0 = hide)
 *       greeting:    'Track your delivery', // hover tooltip text
 *       zIndex:      999999,
 *     };
 *   </script>
 *   <script src="https://dev.snapsend.co:550/scripts/embed.js"></script>
 *
 * JS API — trigger from anywhere on the page:
 *   window.TrackIT.open()
 *   window.TrackIT.close()
 *   window.TrackIT.toggle()
 *
 * Example: open on button click
 *   <button onclick="window.TrackIT.open()">Track My Order</button>
 * ─────────────────────────────────────────────────────────────────
 */

(function (window, document) {
    'use strict';

    /* Prevent double-init */
    if (window.__trackitLoaded) return;
    window.__trackitLoaded = true;

    /* ── Origin — hardcoded to your MVC app ──────────────────────── */
    var ORIGIN = 'https://dev.snapsend.co:550';
    //var IFRAME_URL = ORIGIN + '/chat';
    var IFRAME_URL = ORIGIN;

    /* ── Read optional config ─────────────────────────────────────── */
    var cfg = window.TrackITConfig || {};
    var POSITION = cfg.position || 'bottom-right';
    var OFFSET_X = cfg.offsetX != null ? cfg.offsetX : 28;
    var OFFSET_Y = cfg.offsetY != null ? cfg.offsetY : 28;
    var BTN_COLOR = cfg.buttonColor || '#3AB54A';
    var BTN_LABEL = cfg.buttonLabel || 'TrackIT';
    var BADGE = cfg.badgeCount != null ? cfg.badgeCount : 1;
    var GREETING = cfg.greeting || 'Track your delivery \uD83D\uDCE6';
    var Z = cfg.zIndex || 999999;
    var SIDE = POSITION === 'bottom-left' ? 'left' : 'right';

    /* ── CSS ──────────────────────────────────────────────────────── */
    var style = document.createElement('style');
    style.textContent =
        '@import url("https://fonts.googleapis.com/css2?family=Syne:wght@700;800&family=DM+Sans:wght@400;500&display=swap");' +

        /* Wrapper */
        '#trackit-wrap{position:fixed;' + SIDE + ':' + OFFSET_X + 'px;bottom:' + OFFSET_Y + 'px;z-index:' + Z + ';font-family:"DM Sans",sans-serif;}' +

        /* Tooltip */
        '#trackit-tooltip{position:absolute;bottom:80px;' + SIDE + ':0;background:#1c1c1e;color:#fff;font-size:12.5px;font-weight:500;padding:8px 13px;border-radius:10px;white-space:nowrap;pointer-events:none;opacity:0;transform:translateY(6px);transition:opacity .2s,transform .22s cubic-bezier(.34,1.3,.64,1);box-shadow:0 4px 16px rgba(0,0,0,.22);}' +
        '#trackit-tooltip::after{content:"";position:absolute;bottom:-5px;' + SIDE + ':22px;width:10px;height:10px;background:#1c1c1e;transform:rotate(45deg);border-radius:2px;}' +
        '#trackit-wrap:hover #trackit-tooltip{opacity:1;transform:translateY(0);}' +

        /* Button */
        '#trackit-btn{width:68px;height:68px;background:' + BTN_COLOR + ';border-radius:50%;border:none;cursor:pointer;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;box-shadow:0 8px 28px rgba(0,0,0,.18),0 3px 8px rgba(0,0,0,.1);transition:transform .22s cubic-bezier(.34,1.56,.64,1),box-shadow .22s;position:relative;animation:trackit-pop .6s cubic-bezier(.34,1.56,.64,1) both 1.2s;}' +
        '@keyframes trackit-pop{from{transform:scale(0);opacity:0;}60%{transform:scale(1.12);}to{transform:scale(1);opacity:1;}}' +
        '#trackit-btn:hover{transform:scale(1.08);box-shadow:0 14px 36px rgba(0,0,0,.22);}' +
        '#trackit-btn:active{transform:scale(.96);}' +

        /* Icon swap */
        '#trackit-btn .ti-chat{transition:opacity .25s,transform .25s;}' +
        '#trackit-btn .ti-close{transition:opacity .25s,transform .25s;position:absolute;opacity:0;transform:rotate(-90deg) scale(.6);}' +
        '#trackit-wrap.open #trackit-btn .ti-chat{opacity:0;transform:rotate(90deg) scale(.6);}' +
        '#trackit-wrap.open #trackit-btn .ti-close{opacity:1;transform:rotate(0) scale(1);}' +
        '#trackit-lbl{font-family:"Syne",sans-serif;font-size:9px;font-weight:700;color:#fff;letter-spacing:.5px;position:relative;z-index:1;transition:opacity .25s;}' +
        '#trackit-wrap.open #trackit-lbl{opacity:0;}' +

        /* Badge */
        '#trackit-badge{position:absolute;top:5px;right:5px;min-width:18px;height:18px;background:#ff3b30;border-radius:9px;border:2px solid #fff;font-size:9px;color:#fff;display:flex;align-items:center;justify-content:center;font-weight:700;padding:0 3px;animation:trackit-pulse 2.4s ease infinite;transition:transform .3s,opacity .3s;}' +
        '@keyframes trackit-pulse{0%,100%{box-shadow:0 0 0 0 rgba(255,59,48,.45);}50%{box-shadow:0 0 0 5px rgba(255,59,48,0);}}' +

        /* Panel */
        '#trackit-panel{position:absolute;bottom:84px;' + SIDE + ':0;width:400px;height:640px;border-radius:22px;overflow:hidden;box-shadow:0 24px 80px rgba(0,0,0,.18),0 6px 20px rgba(0,0,0,.1);transform-origin:bottom ' + SIDE + ';transform:scale(0) translateY(20px);opacity:0;pointer-events:none;transition:transform .38s cubic-bezier(.34,1.56,.64,1),opacity .28s ease;background:#fff;}' +
        '#trackit-wrap.open #trackit-panel{transform:scale(1) translateY(0);opacity:1;pointer-events:all;}' +
        '#trackit-iframe{width:100%;height:100%;border:none;display:block;}' +

        /* Skeleton */
        '#trackit-skeleton{position:absolute;inset:0;background:#fff;display:flex;flex-direction:column;overflow:hidden;transition:opacity .4s;}' +
        '#trackit-skeleton.hidden{opacity:0;pointer-events:none;}' +
        '.ti-sk-hdr{height:78px;background:linear-gradient(135deg,#2a8f38,#3AB54A);flex-shrink:0;}' +
        '.ti-sk-body{flex:1;padding:16px;display:flex;flex-direction:column;gap:10px;}' +
        '.ti-sk-line,.ti-sk-bubble{background:linear-gradient(90deg,#f0f0f0 25%,#e4e4e4 50%,#f0f0f0 75%);background-size:200% 100%;animation:trackit-shimmer 1.4s infinite;border-radius:8px;}' +
        '.ti-sk-line{height:13px;}.ti-sk-bubble{height:42px;}' +
        '@keyframes trackit-shimmer{from{background-position:200% 0;}to{background-position:-200% 0;}}' +

        /* Responsive */
        '@media(max-width:480px){#trackit-panel{width:calc(100vw - 20px);height:calc(100vh - 110px);' + SIDE + ':-4px;}#trackit-wrap{' + SIDE + ':12px;bottom:14px;}}';

    document.head.appendChild(style);

    /* ── DOM ──────────────────────────────────────────────────────── */

    /* Skeleton */
    var skeleton = document.createElement('div');
    skeleton.id = 'trackit-skeleton';
    skeleton.innerHTML =
        '<div class="ti-sk-hdr"></div>' +
        '<div class="ti-sk-body">' +
        '  <div class="ti-sk-bubble" style="width:72%"></div>' +
        '  <div class="ti-sk-line" style="width:100%"></div>' +
        '  <div class="ti-sk-line" style="width:75%"></div>' +
        '  <div class="ti-sk-line" style="width:55%"></div>' +
        '  <div style="height:10px"></div>' +
        '  <div class="ti-sk-bubble" style="width:60%"></div>' +
        '  <div style="height:8px"></div>' +
        '  <div class="ti-sk-bubble" style="width:80%"></div>' +
        '  <div class="ti-sk-line" style="width:90%"></div>' +
        '</div>';

    /* iframe */
    var iframe = document.createElement('iframe');
    iframe.id = 'trackit-iframe';
    iframe.title = 'TrackIT Delivery Assistant';
    iframe.setAttribute('allow', 'clipboard-write');
    var iframeReady = false;
    iframe.onload = function () {
        iframeReady = true;
        setTimeout(function () { skeleton.classList.add('hidden'); }, 350);
    };

    /* Panel */
    var panel = document.createElement('div');
    panel.id = 'trackit-panel';
    panel.appendChild(skeleton);
    panel.appendChild(iframe);

    /* Tooltip */
    var tooltip = document.createElement('div');
    tooltip.id = 'trackit-tooltip';
    tooltip.textContent = GREETING;

    /* Button */
    var btn = document.createElement('button');
    btn.id = 'trackit-btn';
    btn.setAttribute('aria-label', 'Open TrackIT delivery assistant');
    btn.setAttribute('aria-expanded', 'false');
    btn.innerHTML =
        '<svg class="ti-chat" width="24" height="24" fill="none" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" viewBox="0 0 24 24">' +
        '  <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>' +
        '</svg>' +
        '<svg class="ti-close" width="22" height="22" fill="none" stroke="white" stroke-width="2.5" stroke-linecap="round" viewBox="0 0 24 24">' +
        '  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>' +
        '</svg>' +
        (BADGE > 0 ? '<span id="trackit-badge" aria-label="' + BADGE + ' notification">' + BADGE + '</span>' : '') +
        '<span id="trackit-lbl">' + BTN_LABEL + '</span>';

    /* Wrap it all up */
    var wrap = document.createElement('div');
    wrap.id = 'trackit-wrap';
    wrap.appendChild(tooltip);
    wrap.appendChild(panel);
    wrap.appendChild(btn);
    document.body.appendChild(wrap);

    /* ── Logic ────────────────────────────────────────────────────── */
    var isOpen = false;

    function openChat() {
        isOpen = true;
        wrap.classList.add('open');
        btn.setAttribute('aria-expanded', 'true');

        /* Remove badge */
        var badge = document.getElementById('trackit-badge');
        if (badge) {
            badge.style.transform = 'scale(0)';
            badge.style.opacity = '0';
            setTimeout(function () { if (badge.parentNode) badge.parentNode.removeChild(badge); }, 300);
        }

        /* Lazy-load iframe on first open */
        if (!iframeReady && !iframe.src) {
            skeleton.classList.remove('hidden');
            iframe.src = IFRAME_URL;
        }

        /* Tell iframe it's visible */
        if (iframeReady) {
            try { iframe.contentWindow.postMessage({ type: 'trackit:opened' }, ORIGIN); } catch (e) { }
        }
    }

    function closeChat() {
        isOpen = false;
        wrap.classList.remove('open');
        btn.setAttribute('aria-expanded', 'false');
        try { iframe.contentWindow.postMessage({ type: 'trackit:closed' }, ORIGIN); } catch (e) { }
    }

    /* Toggle on button click */
    btn.addEventListener('click', function () {
        isOpen ? closeChat() : openChat();
    });

    /* Escape to close */
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && isOpen) closeChat();
    });

    /* Click outside to close */
    document.addEventListener('click', function (e) {
        if (isOpen && !wrap.contains(e.target)) closeChat();
    });

    /* ── postMessage bridge (iframe → parent) ─────────────────────── */
    window.addEventListener('message', function (e) {
        if (e.origin !== ORIGIN) return;
        var d = e.data || {};
        if (d.type === 'trackit:close') closeChat();
        if (d.type === 'trackit:open') openChat();
        if (d.type === 'trackit:resize' && d.height) {
            panel.style.height = Math.min(Math.max(d.height, 300), 720) + 'px';
        }
    });

    /* ── Public JS API ────────────────────────────────────────────── */
    window.TrackIT = {
        open: openChat,
        close: closeChat,
        toggle: function () { isOpen ? closeChat() : openChat(); },
    };

})(window, document);