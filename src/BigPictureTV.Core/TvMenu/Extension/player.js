// Runs in every frame of the TV menu's pages, so it reaches video players
// inside frames of other sites (Crunchyroll's). Seeks and pauses the
// biggest video in its frame when the page (spatial-nav.js) asks, and
// handles the controller's keys itself while the player has the focus.
(() => {
  if (window.__tvPlayerLoaded) return;
  window.__tvPlayerLoaded = true;

  function video() {
    let best = null, bestArea = 0;
    for (const v of document.querySelectorAll('video')) {
      const r = v.getBoundingClientRect(), a = r.width * r.height;
      if (a > bestArea) { best = v; bestArea = a; }
    }
    return best;
  }

  function run(command) {
    const v = video();
    if (!v) return false;
    if (command.seek) v.currentTime = Math.max(0, Math.min(v.duration || Infinity, v.currentTime + command.seek));
    if (command.toggle) { if (v.paused) v.play(); else v.pause(); }
    return true;
  }

  chrome.runtime.onMessage.addListener(message => {
    if (message && message.player) run(message.player);
  });

  // Keys typed while the focus is in this frame (the player was chosen with A).
  if (window.top === window) return;
  addEventListener('keydown', e => {
    if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return;
    let handled = false;
    if (e.key === 'PageUp') handled = run({ seek: -10 });
    else if (e.key === 'PageDown') handled = run({ seek: 10 });
    else if (e.key === 'F8') {
      try { chrome.runtime.sendMessage({ layout: 'toggle' }); handled = true; } catch (err) { /* extension reloaded */ }
    }
    if (handled) {
      e.preventDefault();
      e.stopImmediatePropagation();
    }
  }, true);
})();
