// Moves a visible focus box between the links and buttons of any web page
// with the arrow keys the controller's cross sends, like on a TV app. Part of
// a small extension the app loads only into the browser profiles of the TV
// menu (never the user's own browser). As an extension it runs apart from the
// page's scripts, so anti-bot checks like Cloudflare's don't see it. Keys it
// doesn't need (a playing video, text being edited, an embedded player) go
// on to the page untouched.
(() => {
  if (window.__tvNavLoaded || window.top !== window) return;
  window.__tvNavLoaded = true;

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
    if (el.disabled || el.closest('[inert]')) return false; // aria-hidden too often marks carousel cards out of view
    const r = el.getBoundingClientRect();
    if (r.width < 4 || r.height < 4) return false;
    if (el.checkVisibility) return el.checkVisibility({ opacityProperty: true, visibilityProperty: true });
    for (let e = el; e && e !== document.documentElement; e = e.parentElement) {
      const s = getComputedStyle(e);
      if (s.display === 'none' || s.visibility === 'hidden' || s.opacity === '0') return false;
    }
    return true;
  }

  // Everything that can be chosen; a card that is a link wins over what's
  // inside it, and of several links to the same place (a card's picture and
  // its title) only the biggest stays, so each card is one stop.
  function targets() {
    const list = [...document.querySelectorAll(SELECTOR)].filter(el => {
      const outer = el.parentElement && el.parentElement.closest(SELECTOR);
      return !(outer && shown(outer)) && shown(el);
    });
    const byHref = new Map();
    for (const el of list) {
      const href = el.tagName === 'A' && el.getAttribute('href');
      if (!href || href === '#') continue;
      const kept = byHref.get(href);
      if (!kept || area(el) > area(kept)) byHref.set(href, el);
    }
    return list.filter(el => {
      const href = el.tagName === 'A' && el.getAttribute('href');
      return !href || href === '#' || byHref.get(href) === el;
    });
  }

  function area(el) {
    const r = el.getBoundingClientRect();
    return r.width * r.height;
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
    reveal(el);
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

  // The nearest target in that direction. Ones lined up with the current
  // one (same row for left/right, same column for up/down) always win;
  // among them the closest ahead, then the best centred.
  function next(from, dir, list) {
    const a = from.getBoundingClientRect();
    const vertical = dir === 'down' || dir === 'up';
    let best = null, bestScore = Infinity;
    for (const el of list) {
      if (el === from || from.contains(el) || el.contains(from)) continue;
      const b = el.getBoundingClientRect();
      let ahead, gap, offset;
      if (vertical) {
        ahead = dir === 'down' ? b.top - a.bottom : a.top - b.bottom;
        if ((dir === 'down' ? b.top + b.bottom - a.top - a.bottom : a.top + a.bottom - b.top - b.bottom) <= 0) continue;
        gap = Math.max(a.left, b.left) - Math.min(a.right, b.right);
        offset = Math.abs((a.left + a.right) - (b.left + b.right)) / 2;
      } else {
        ahead = dir === 'right' ? b.left - a.right : a.left - b.right;
        if ((dir === 'right' ? b.left + b.right - a.left - a.right : a.left + a.right - b.left - b.right) <= 0) continue;
        gap = Math.max(a.top, b.top) - Math.min(a.bottom, b.bottom);
        offset = Math.abs((a.top + a.bottom) - (b.top + b.bottom)) / 2;
      }
      const lined = gap < 0;
      // Up and down go to the next card, not to a button on the same one.
      const sameCard = vertical && inSameCard(from, el);
      const score = (lined ? 0 : 1e6 + gap * 3) + (sameCard ? 5e5 : 0) + Math.max(0, ahead) + offset / 4;
      if (score < bestScore) { bestScore = score; best = el; }
    }
    // Going up or down onto a card lands on its main (biggest) link, not on
    // a small button at its edge.
    if (best && vertical) {
      for (const el of list)
        if (el !== best && area(el) > area(best) && inSameCard(best, el)) best = el;
    }
    return best;
  }

  // Whether two targets belong to one card: their nearest common box is
  // not much bigger than the larger of them.
  function inSameCard(a, b) {
    let common = a.parentElement;
    while (common && !common.contains(b)) common = common.parentElement;
    if (!common || common === document.body) return false;
    const box = common.getBoundingClientRect();
    return box.width < innerWidth / 2 && box.width * box.height < 3 * Math.max(area(a), area(b));
  }

  // Scroll only when the choice is out of view or near an edge, and then put
  // it in the middle, so the page doesn't jump on every press.
  function reveal(el) {
    const r = el.getBoundingClientRect(), margin = Math.min(120, innerHeight / 6);
    const outside = r.top < margin || r.bottom > innerHeight - margin || r.left < 0 || r.right > innerWidth;
    if (outside) el.scrollIntoView({ block: r.height > innerHeight - 2 * margin ? 'start' : 'center', inline: 'nearest', behavior: 'smooth' });
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
    if (pageWants('accept') || isText(document.activeElement)) return false; // Enter in a text box submits it
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

  // A security check (Cloudflare and the like) is left completely alone.
  function checking() {
    return /just a moment|un momento|attention required/i.test(document.title) ||
      !!document.querySelector('#challenge-form, #challenge-running, .cf-turnstile, [name="cf-turnstile-response"]');
  }

  const commands = {
    ArrowUp: () => move('up'), ArrowDown: () => move('down'), ArrowLeft: () => move('left'), ArrowRight: () => move('right'),
    Enter: accept, F2: search, // F2: the controller's Y
  };
  addEventListener('keydown', e => {
    const command = commands[e.key];
    if (!command || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || e.isComposing || checking()) return;
    let handled = false;
    try { handled = command(); } catch (err) { handled = false; }
    if (handled) {
      e.preventDefault();
      e.stopImmediatePropagation();
    }
  }, true);
})();
