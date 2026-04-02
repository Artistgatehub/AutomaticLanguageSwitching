import {
  HOST_NAME,
  ErrorMessage,
  ExtensionToHostMessage,
  HostToExtensionMessage,
  HelloAckMessage,
  LayoutRestoreResultMessage,
  WarningMessage,
  createHelloMessage,
  createChromeFocusReturnedMessage,
  createTabClosedMessage,
  createTabSwitchedMessage
} from "./protocol.js";

let port: chrome.runtime.Port | null = null;
let reconnectTimer: number | null = null;
let reconnectDelayMs = 1000;

const MAX_RECONNECT_DELAY_MS = 10000;
const lastActiveTabByWindow = new Map<number, number>();
const ACTIVE_ICONS = createIconSet("active");
const INACTIVE_ICONS = createIconSet("inactive");

function formatTab(windowId: number | null, tabId: number | null): string {
  return windowId === null || tabId === null ? "null" : `${windowId}:${tabId}`;
}

function summarizeOutgoingMessage(message: ExtensionToHostMessage): string {
  switch (message.type) {
    case "hello":
      return "hello";
    case "tab_switched":
      return `tab_switched prev=${formatTab(message.payload.previousWindowId, message.payload.previousTabId)} current=${formatTab(message.payload.currentWindowId, message.payload.currentTabId)}`;
    case "chrome_focus_returned":
      return `chrome_focus_returned current=${formatTab(message.payload.currentWindowId, message.payload.currentTabId)}`;
    case "tab_closed":
      return `tab_closed tab=${formatTab(message.payload.windowId, message.payload.tabId)}`;
  }
}

function log(message: string, data?: unknown): void {
  if (data === undefined) {
    console.log(`[als-extension] ${message}`);
    return;
  }

  try {
    console.log(`[als-extension] ${message} ${JSON.stringify(data)}`);
  } catch {
    console.log(`[als-extension] ${message}`, data);
  }
}

function clearReconnectTimer(): void {
  if (reconnectTimer !== null) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
}

function createIconSet(
  state: "active" | "inactive"
): Record<16 | 32 | 48 | 128, string> {
  return {
    16: chrome.runtime.getURL(`icons/icon-${state}-16.png`),
    32: chrome.runtime.getURL(`icons/icon-${state}-32.png`),
    48: chrome.runtime.getURL(`icons/icon-${state}-48.png`),
    128: chrome.runtime.getURL(`icons/icon-${state}-128.png`)
  };
}

function setActionIcons(path: chrome.action.TabIconDetails["path"]): void {
  chrome.action.setIcon({ path }, () => {
    const error = chrome.runtime.lastError?.message;
    if (error) {
      log(`Failed to update extension icon. error=${error}`);
    }
  });
}

function scheduleReconnect(): void {
  if (reconnectTimer !== null) {
    return;
  }

  const delay = reconnectDelayMs;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    tryConnect();
  }, delay);

  reconnectDelayMs = Math.min(reconnectDelayMs * 2, MAX_RECONNECT_DELAY_MS);
  log(`Scheduled native host reconnect in ${delay}ms.`);
}

function handleHostMessage(message: HostToExtensionMessage): void {
  switch (message.type) {
    case "hello_ack": {
      const m = message as HelloAckMessage;
      log(
        `hello_ack hostVersion=${m.payload.hostVersion ?? "unknown"} platform=${m.payload.platform ?? "unknown"} settingEnabled=${m.payload.perAppInputMethodEnabled ?? "unknown"} autoEnableAttempted=${m.payload.attemptedAutoEnable ?? false}`
      );

      if (m.payload.perAppInputMethodEnabled === false) {
        log(
          `Windows per-app input setting still disabled after startup check. autoEnableAttempted=${m.payload.attemptedAutoEnable ?? false}`
        );
      }

      void syncCurrentActiveTab();
      return;
    }

    case "warning": {
      const m = message as WarningMessage;
      log(
        `warning message="${m.payload.message}" settingEnabled=${m.payload.perAppInputMethodEnabled ?? "unknown"} autoEnableAttempted=${m.payload.attemptedAutoEnable ?? false}`
      );
      return;
    }

    case "layout_restore_result": {
      const m = message as LayoutRestoreResultMessage;
      log(
        `restore_result tab=${formatTab(m.payload.windowId, m.payload.tabId)} layout=${m.payload.layoutId} result=${m.payload.result}`
      );
      return;
    }

    case "error": {
      const m = message as ErrorMessage;
      log("Native host returned an error.", m);
      return;
    }

    default:
      log("Received message from native host.", message);
  }
}

function connect(): chrome.runtime.Port | null {
  if (port) {
    return port;
  }

  try {
    const nextPort = chrome.runtime.connectNative(HOST_NAME);

    nextPort.onMessage.addListener(handleHostMessage);

    nextPort.onDisconnect.addListener(() => {
      const runtimeError = chrome.runtime.lastError?.message;
      log(`Native host disconnected.${runtimeError ? ` reason=${runtimeError}` : ""}`);
      setActionIcons(INACTIVE_ICONS);

      if (port === nextPort) {
        port = null;
      }

      scheduleReconnect();
    });

    clearReconnectTimer();
    nextPort.postMessage(createHelloMessage());
    log("Outgoing message hello");
    reconnectDelayMs = 1000;
    port = nextPort;
    setActionIcons(ACTIVE_ICONS);
    log("Connected to native host.");
    return nextPort;
  } catch (error) {
    log("Failed to connect to native host.", error);
    setActionIcons(INACTIVE_ICONS);
    scheduleReconnect();
    return null;
  }
}

function tryConnect(): void {
  connect();
}

function send(message: ExtensionToHostMessage): void {
  const nativePort = connect();

  if (!nativePort) {
    log(`Skipping outgoing message because native host is unavailable: ${summarizeOutgoingMessage(message)}`);
    return;
  }

  log(`Outgoing message ${summarizeOutgoingMessage(message)}`);
  nativePort.postMessage(message);
}

async function syncCurrentActiveTab(): Promise<void> {
  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  const activeTab = tabs[0];

  if (!activeTab || activeTab.id === undefined || activeTab.windowId === undefined) {
    return;
  }

  lastActiveTabByWindow.set(activeTab.windowId, activeTab.id);
  send(createTabSwitchedMessage(null, null, activeTab.windowId, activeTab.id));
}

chrome.runtime.onInstalled.addListener(() => {
  tryConnect();
});

chrome.runtime.onStartup.addListener(() => {
  tryConnect();
});

chrome.runtime.onMessage.addListener((message, sender) => {
  if (message?.type !== "page_focus_returned") {
    return;
  }

  const tabId = sender.tab?.id;
  const windowId = sender.tab?.windowId;

  if (tabId === undefined || windowId === undefined) {
    return;
  }

  chrome.tabs.get(tabId, (tab) => {
    if (chrome.runtime.lastError) {
      log(`Focus return ignored: tab lookup failed for ${windowId}:${tabId} error=${chrome.runtime.lastError.message}`);
      return;
    }

    if (!tab.active) {
      log(`Focus return ignored: ${windowId}:${tabId} is no longer active.`);
      return;
    }

    lastActiveTabByWindow.set(windowId, tabId);
    log(`Focus returned to active tab ${windowId}:${tabId}.`);
    send(createChromeFocusReturnedMessage(windowId, tabId));
  });
});

chrome.tabs.onActivated.addListener((activeInfo) => {
  const previousTabId = lastActiveTabByWindow.get(activeInfo.windowId) ?? null;
  const previousWindowId = previousTabId === null ? null : activeInfo.windowId;

  lastActiveTabByWindow.set(activeInfo.windowId, activeInfo.tabId);

  log(
    `Active tab changed prev=${formatTab(previousWindowId, previousTabId)} current=${formatTab(activeInfo.windowId, activeInfo.tabId)}`
  );
  send(
    createTabSwitchedMessage(
      previousWindowId,
      previousTabId,
      activeInfo.windowId,
      activeInfo.tabId
    )
  );
});

chrome.tabs.onRemoved.addListener((tabId, removeInfo) => {
  if (removeInfo.windowId === chrome.windows.WINDOW_ID_NONE) {
    return;
  }

  if (lastActiveTabByWindow.get(removeInfo.windowId) === tabId) {
    lastActiveTabByWindow.delete(removeInfo.windowId);
  }

  log(`Tab closed ${removeInfo.windowId}:${tabId}.`);
  send(createTabClosedMessage(removeInfo.windowId, tabId));
});

setActionIcons(INACTIVE_ICONS);
tryConnect();
