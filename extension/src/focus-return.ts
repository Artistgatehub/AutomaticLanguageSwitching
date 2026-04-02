function notifyFocusReturned(): void {
  try {
    chrome.runtime.sendMessage({ type: "page_focus_returned" });
  } catch {
    // Ignore transient extension/runtime unavailability.
  }
}

window.addEventListener("focus", () => {
  notifyFocusReturned();
});

document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible" && document.hasFocus()) {
    notifyFocusReturned();
  }
});
