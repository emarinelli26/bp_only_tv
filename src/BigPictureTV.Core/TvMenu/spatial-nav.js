// Moves a visible focus box between the links and buttons of any web page
// with the controller's cross, like on a TV app. Injected by the app into
// the pages of the TV menu (never into the user's own browser). Each call
// returns true if it handled the press, or false to let the page get the
// real key instead (a playing video, text being edited, an embedded player).
(() => {
  if (window.__tvNav || window.top !== window) return;

  const SELECTOR = [
    'a[href]', 'button', 'input:not([type=hidden])', 'select', 'textarea', 'iframe', 'summary',
    '[role=button]', '[role=link]', '[role=tab]', '[role=menuitem]', '[role=option]', '[role=checkbox]',
    '[tabindex]:not([tabindex="-1"])', '[contenteditable=""]', '[contenteditable=true]',
  ].join(',');
  const MARK = '__tvnav-focus';
  let current = null;

  function addStyle() {
    if (document.getElementById('__tvnav-style') || !document.head) return;
    const style = document.createElement('style');
    style.id = '__tvnav-style';
    style.textContent = `.${MARK} { outline: 4px solid #fff !important; outline-offset: 2px !important;
      box-shadow: 0 0 0 8px #f47521 !important; border-radius: 6px; transition: box-shadow .1s; }`;
    document.head.appendChild(style);
  }

  function shown(el) {
    if (el.disabled || el.closest('[aria-hidden=true],[inert]')) return false;
    const r = el.getBoundingClientRect();
    if (r.width < 4 || r.height < 4) return false;
    if (el.checkVisibility) return el.checkVisibility({ opacityProperty: true, visibilityProperty: true });
    for (let e = el; e && e !== document.documentElement; e = e.parentElement) {
      const s = getComputedStyle(e);
      if (s.display === 'none' || s.visibility === 'hidden' || s.opacity === '0') return false;
    }
    return true;
  }

  // Everything that can be chosen; a card that is a link wins over what's inside it.
  function targets() {
    return [...document.querySelectorAll(SELECTOR)].filter(el => {
      const outer = el.parentElement && el.parentElement.closest(SELECTOR);
      return !(outer && shown(outer)) && shown(el);
    });
  }

  function isText(el) {
    return el && (el.tagName === 'TEXTAREA' || el.isContentEditable ||
      (el.tagName === 'INPUT' && !/^(button|submit|reset|checkbox|radio|range|color|file|image)$/i.test(el.type)));
  }

  function bigVideoPlaying() {
    return [...document.querySelectorAll('video')].some(v => {
      if (v.paused || v.ended) return false;
      const r = v.getBoundingClientRect();
      return r.width * r.height > innerWidth * innerHeight * 0.5;
    });
  }

  // When the page itself should get the key.
  function pageWants(dir) {
    const active = document.activeElement;
    const sideways = dir === 'left' || dir === 'right' || dir === 'accept';
    if (document.fullscreenElement) return true;
    if (active && active.tagName === 'IFRAME' && sideways) return true; // an embedded player
    if (isText(active) && sideways && dir !== 'accept') return true; // moving the caret
    if (bigVideoPlaying() && sideways) return true; // seeking, pausing
    return false;
  }

  function choose(el) {
    addStyle();
    if (current) current.classList.remove(MARK);
    current = el;
    el.classList.add(MARK);
    if (el.tagName !== 'IFRAME' && !isText(el)) {
      if (!el.hasAttribute('tabindex') && el.tabIndex < 0) el.setAttribute('tabindex', '-1');
      el.focus({ preventScroll: true });
    } else if (document.activeElement && document.activeElement !== document.body) {
      document.activeElement.blur(); // out of the player or text box, without typing in it
    }
    el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
  }

  function valid(el) {
    return el && document.contains(el) && shown(el);
  }

  // The first thing in view, top to bottom and left to right.
  function first(list) {
    const inView = list.filter(el => {
      const r = el.getBoundingClientRect();
      return r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth;
    });
    const pool = inView.length ? inView : list;
    return pool.sort((a, b) => {
      const ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
      return Math.abs(ra.top - rb.top) > 8 ? ra.top - rb.top : ra.left - rb.left;
    })[0];
  }

  // The nearest target in that direction: how far ahead, plus twice how far
  // off to the side (zero when they line up), so neighbours in a row or a
  // column win over closer ones diagonally.
  function next(from, dir, list) {
    const a = from.getBoundingClientRect();
    let best = null, bestScore = Infinity;
    for (const el of list) {
      if (el === from || from.contains(el) || el.contains(from)) continue;
      const b = el.getBoundingClientRect();
      let ahead, side;
      if (dir === 'down' || dir === 'up') {
        ahead = dir === 'down' ? b.top - a.bottom : a.top - b.bottom;
        if (ahead < -Math.min(a.height, b.height) / 2) continue;
        side = Math.max(0, Math.max(a.left, b.left) - Math.min(a.right, b.right));
        if (side === 0) side = Math.abs((a.left + a.right) / 2 - (b.left + b.right) / 2) / 10;
      } else {
        ahead = dir === 'right' ? b.left - a.right : a.left - b.right;
        if (ahead < -Math.min(a.width, b.width) / 2) continue;
        side = Math.max(0, Math.max(a.top, b.top) - Math.min(a.bottom, b.bottom));
        if (side === 0) side = Math.abs((a.top + a.bottom) / 2 - (b.top + b.bottom) / 2) / 10;
      }
      const score = Math.max(0, ahead) + 2 * side;
      if (score < bestScore) { bestScore = score; best = el; }
    }
    return best;
  }

  function move(dir) {
    if (pageWants(dir)) return false;
    const list = targets();
    if (!list.length) return false;
    if (!valid(current)) {
      choose(first(list));
      return true;
    }
    const found = next(current, dir, list);
    if (found) choose(found);
    else if (dir === 'up' || dir === 'down') scrollBy({ top: (dir === 'down' ? 1 : -1) * innerHeight * 0.6, behavior: 'smooth' });
    return true;
  }

  function accept() {
    if (pageWants('accept')) return false;
    if (!valid(current)) return false;
    if (current.tagName === 'IFRAME') {
      current.focus(); // keys go to the embedded player from now on
      return true;
    }
    if (isText(current) || current.tagName === 'SELECT') {
      current.focus();
      return true;
    }
    current.click();
    return true;
  }

  // Y: the page's search box, or its search button or link.
  function search() {
    const box = document.querySelector('input[type=search], input[name*=search i], input[placeholder*=search i], ' +
      'input[placeholder*=buscar i], input[aria-label*=search i], input[aria-label*=buscar i]');
    if (box && shown(box)) {
      choose(box);
      box.focus();
      return true;
    }
    const button = [...document.querySelectorAll('a[href*="/search"], [aria-label*=search i], [aria-label*=buscar i], ' +
      '[title*=search i], [title*=buscar i]')].find(shown);
    if (button) {
      button.click();
      return true;
    }
    return false;
  }

  window.__tvNav = { move, accept, search };
})();
