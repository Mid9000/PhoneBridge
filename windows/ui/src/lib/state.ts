import type { DataSection, HostMessage, Message, SectionId, Snapshot } from './types';

export interface Toast { id: number; text: string; kind: 'info' | 'error' }

export interface AppState extends Snapshot {
  ready: boolean; // first snapshot received
  threads: Record<string, Message[]>;
  sectionErrors: Partial<Record<DataSection, string>>;
  toasts: Toast[];
  section: SectionId;
  conversationId: string | null;
}

export function initialState(): AppState {
  return {
    ready: false,
    host: { version: '', mica: false, maximized: false },
    connection: { paired: false, phoneName: '', pcName: '', wifi: 'disconnected', bluetooth: 'disconnected', lastSeenAt: null },
    battery: { level: null, charging: false, updatedAt: null },
    conversations: [], notifications: [], transfers: [], clipboard: [], calls: [], devices: [],
    settings: {
      windowStyle: 'mac', theme: 'system', toasts: true, clipboardAuto: false, otpAutoCopy: true,
      keepHistory: true, helloLock: false, shortcutsHttp: true, downloadDir: '',
    },
    threads: {}, sectionErrors: {}, toasts: [], section: 'messages', conversationId: null,
  };
}

function upsert<T extends { id: string }>(list: T[], item: T, atStart: boolean): T[] {
  const i = list.findIndex((x) => x.id === item.id);
  if (i >= 0) { list[i] = item; return list; }
  return atStart ? [item, ...list] : [...list, item];
}

let toastSeq = 0;

/** Applies one host message to the state (mutates it, so it works on a Svelte $state proxy). */
export function reduce(state: AppState, msg: HostMessage): void {
  switch (msg.type) {
    case 'state':
      Object.assign(state, msg.snapshot, { ready: true });
      break;
    case 'connection': state.connection = msg.connection; break;
    case 'battery': state.battery = msg.battery; break;
    case 'conversations': state.conversations = msg.conversations; break;
    case 'messages': state.threads[msg.conversationId] = msg.messages; break;
    case 'message':
      state.threads[msg.message.conversationId] = upsert(state.threads[msg.message.conversationId] ?? [], msg.message, false);
      break;
    case 'notification': state.notifications = upsert(state.notifications, msg.notification, true); break;
    case 'notificationRemoved': state.notifications = state.notifications.filter((n) => n.id !== msg.id); break;
    case 'notifications': state.notifications = msg.notifications; break;
    case 'transfer': state.transfers = upsert(state.transfers, msg.transfer, true); break;
    case 'clipboard': state.clipboard = msg.items; break;
    case 'call': state.calls = upsert(state.calls, msg.call, true); break;
    case 'devices': state.devices = msg.devices; break;
    case 'settings': state.settings = msg.settings; break;
    case 'window': state.host.maximized = msg.maximized; break;
    case 'navigate':
      state.section = msg.section;
      if (msg.conversationId) state.conversationId = msg.conversationId;
      break;
    case 'sectionError':
      if (msg.message) state.sectionErrors[msg.section] = msg.message;
      else delete state.sectionErrors[msg.section];
      break;
    case 'toast':
      state.toasts = [...state.toasts.slice(-2), { id: ++toastSeq, text: msg.text, kind: msg.kind ?? 'info' }];
      break;
  }
}
