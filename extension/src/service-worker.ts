import {
  HOST_NAME,
  ErrorMessage,
  ExtensionToHostMessage,
  HostToExtensionMessage,
  HelloAckMessage,
  LayoutRestoreResultMessage,
  createHelloMessage,
  createTabClosedMessage,
  createTabSwitchedMessage
} from "./protocol.js";

let port: chrome.runtime.Port | null = null;
let reconnectTimer: number | null = null;
let reconnectDelayMs = 1000;

const MAX_RECONNECT_DELAY_MS = 10000;
const lastActiveTabByWindow = new Map<number, number>();

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
      log("Native host connected.", m);
      void syncCurrentActiveTab();
      return;
    }

    case "layout_restore_result": {
      const m = message as LayoutRestoreResultMessage;
      console.log(
        `[als-extension] restore windowId=${m.payload.windowId} tabId=${m.payload.tabId} layoutId=${m.payload.layoutId} result=${m.payload.result}`
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
      log("Native host disconnected.", runtimeError);

      if (port === nextPort) {
        port = null;
      }

      scheduleReconnect();
    });

    clearReconnectTimer();
    nextPort.postMessage(createHelloMessage());
    reconnectDelayMs = 1000;
    port = nextPort;
    log("Connected to native host.");
    return nextPort;
  } catch (error) {
    log("Failed to connect to native host.", error);
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
    log("Skipping message because native host is unavailable.", message);
    return;
  }

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

chrome.tabs.onActivated.addListener((activeInfo) => {
  const previousTabId = lastActiveTabByWindow.get(activeInfo.windowId) ?? null;
  const previousWindowId = previousTabId === null ? null : activeInfo.windowId;

  lastActiveTabByWindow.set(activeInfo.windowId, activeInfo.tabId);

  log("Active tab changed.", activeInfo);
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

  log("Tab closed.", { tabId, windowId: removeInfo.windowId });
  send(createTabClosedMessage(removeInfo.windowId, tabId));
});

tryConnect();
