export const PROTOCOL_VERSION = 1;
export const HOST_NAME = "com.automaticlanguageswitching.host";

export type ExtensionToHostMessage =
  | HelloMessage
  | TabSwitchedMessage
  | TabClosedMessage;

export type HostToExtensionMessage =
  | HelloAckMessage
  | LayoutRestoreResultMessage
  | ErrorMessage;

export interface BaseMessage<TType extends string, TPayload> {
  version: typeof PROTOCOL_VERSION;
  type: TType;
  payload: TPayload;
}

export type HelloMessage = BaseMessage<
  "hello",
  {
    extensionVersion: string;
  }
>;

export type HelloAckMessage = BaseMessage<
  "hello_ack",
  {
    hostVersion: string;
    platform: string;
  }
>;

export type TabSwitchedMessage = BaseMessage<
  "tab_switched",
  {
    previousWindowId: number | null;
    previousTabId: number | null;
    currentWindowId: number;
    currentTabId: number;
  }
>;

export type TabClosedMessage = BaseMessage<
  "tab_closed",
  {
    windowId: number;
    tabId: number;
  }
>;

export type LayoutRestoreResultMessage = BaseMessage<
  "layout_restore_result",
  {
    windowId: number;
    tabId: number;
    layoutId: string;
    result: "applied" | "unavailable" | "failed";
  }
>;

export type ErrorMessage = BaseMessage<
  "error",
  {
    message: string;
  }
>;

export function createHelloMessage(): HelloMessage {
  return {
    version: PROTOCOL_VERSION,
    type: "hello",
    payload: {
      extensionVersion: chrome.runtime.getManifest().version
    }
  };
}

export function createTabSwitchedMessage(
  previousWindowId: number | null,
  previousTabId: number | null,
  currentWindowId: number,
  currentTabId: number
): TabSwitchedMessage {
  return {
    version: PROTOCOL_VERSION,
    type: "tab_switched",
    payload: {
      previousWindowId,
      previousTabId,
      currentWindowId,
      currentTabId
    }
  };
}

export function createTabClosedMessage(
  windowId: number,
  tabId: number
): TabClosedMessage {
  return {
    version: PROTOCOL_VERSION,
    type: "tab_closed",
    payload: { windowId, tabId }
  };
}
