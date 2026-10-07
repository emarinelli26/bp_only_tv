// Passes messages between the frames of a tab: the page's script (top frame)
// and the video player's (often in a frame of another site).
chrome.runtime.onMessage.addListener((message, sender) => {
  if (sender.tab && sender.tab.id !== undefined) chrome.tabs.sendMessage(sender.tab.id, message).catch(() => {});
});
