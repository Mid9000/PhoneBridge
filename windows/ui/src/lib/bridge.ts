import type { HostMessage, UiMessage } from './types';
import { createMockBridge } from './mock';

/** Minimal shape of the WebView2 object injected into the page by the host. */
export interface WebView2Like {
  postMessage(message: unknown): void;
  postMessageWithAdditionalObjects?(message: unknown, objects: ArrayLike<unknown>): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  removeEventListener?(type: 'message', listener: (event: { data: unknown }) => void): void;
}

export interface Bridge {
  readonly mode: 'webview' | 'mock';
  /** files: dropped File objects, forwarded to the host so it can read their real paths. */
  send(message: UiMessage, files?: File[]): void;
  subscribe(listener: (message: HostMessage) => void): () => void;
}

const HOST_TYPES = new Set<HostMessage['type']>([
  'state', 'connection', 'battery', 'conversations', 'messages', 'message', 'notification',
  'notificationRemoved', 'notifications', 'transfer', 'clipboard', 'call', 'devices', 'settings',
  'window', 'navigate', 'sectionError', 'toast',
]);

/** Trust boundary: drop anything that is not an object with a known `type`. */
export function parseHostMessage(data: unknown): HostMessage | null {
  if (typeof data === 'string') {
    try { data = JSON.parse(data); } catch { return null; }
  }
  if (!data || typeof data !== 'object') return null;
  const type = (data as { type?: unknown }).type;
  return typeof type === 'string' && HOST_TYPES.has(type as HostMessage['type']) ? (data as HostMessage) : null;
}

export function getWebView(win: unknown = globalThis): WebView2Like | null {
  return (win as { chrome?: { webview?: WebView2Like } }).chrome?.webview ?? null;
}

export function createWebViewBridge(webview: WebView2Like): Bridge {
  const listeners = new Set<(m: HostMessage) => void>();
  webview.addEventListener('message', (event) => {
    const msg = parseHostMessage(event.data);
    if (msg) listeners.forEach((l) => l(msg));
  });
  return {
    mode: 'webview',
    send(message, files) {
      if (files?.length && webview.postMessageWithAdditionalObjects) {
        webview.postMessageWithAdditionalObjects(message, files);
      } else {
        webview.postMessage(message);
      }
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}

/** WebView2 when hosted, otherwise the in-browser mock host (so `npm run dev` shows a populated UI). */
export function createBridge(): Bridge {
  const webview = getWebView();
  if (webview) return createWebViewBridge(webview);
  return createMockBridge(new URLSearchParams(location.search));
}
