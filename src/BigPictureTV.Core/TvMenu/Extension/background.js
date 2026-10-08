// Tells the pages whether they are in a TV menu window (opened by the app as
// an app window, without tabs) or in a normal browser window, where the
// extension stays out of the way. Passes messages between the frames of a
// tab: the page's script (top frame) and the video player's (often in a
// frame of another site).
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (message && message.tvWindow) {
    if (!sender.tab || sender.tab.windowId === undefined) { reply(false); return; }
    chrome.windows.get(sender.tab.windowId)
      .then(w => reply(w.type !== 'normal'))
      .catch(() => reply(false));
    return true; // replies later
  }
  if (sender.tab && sender.tab.id !== undefined) chrome.tabs.sendMessage(sender.tab.id, message).catch(() => {});
});
