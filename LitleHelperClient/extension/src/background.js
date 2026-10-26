// PixelHelper Web Guard background service worker
const HOST_NAME = "com.pixelhelper.webguard";
const sessionAllowedHosts = new Set();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message && message.type === "allowHost" && message.host) {
    sessionAllowedHosts.add(message.host.toLowerCase());
    sendResponse({ ok: true });
  }
});

chrome.webNavigation.onBeforeNavigate.addListener((details) => {
  if (details.frameId !== 0) return; // Only process main window navigation
  const url = details.url;
  if (!url || !url.startsWith("http")) return;

  try {
    const parsed = new URL(url);
    const host = parsed.hostname.toLowerCase();

    // Skip if user explicitly allowed this domain for the current session
    if (sessionAllowedHosts.has(host)) return;

    // Fast local skip for intranet or search queries if needed
    if (host === "localhost" || host === "127.0.0.1" || host.endsWith(".loc")) return;

    chrome.runtime.sendNativeMessage(HOST_NAME, { url: url }, (response) => {
      if (chrome.runtime.lastError) {
        // Native host not running or not registered, don't break browsing
        return;
      }

      if (response && (response.verdict === "Blocked" || response.verdict === "Warning")) {
        const blockedUrl = chrome.runtime.getURL("blocked.html") + 
          "?url=" + encodeURIComponent(url) + 
          "&host=" + encodeURIComponent(response.host || host) + 
          "&reason=" + encodeURIComponent(response.reason || "Подозрительный ресурс");

        chrome.tabs.update(details.tabId, { url: blockedUrl });
      }
    });
  } catch (e) {
    // URL parsing failed, let browser handle
  }
});
