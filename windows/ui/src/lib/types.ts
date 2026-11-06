// Host <-> UI contract. Mirror of docs/ui-bridge.md: change both together.
// All timestamps are ISO 8601 strings (UTC). All ids are opaque strings.

export type Service = 'imessage' | 'sms';
export type LinkState = 'connected' | 'connecting' | 'disconnected' | 'off' | 'unavailable';
/** Why Bluetooth is not (fully) up; the host sends it even while partly connected. */
export type BluetoothIssue = 'noAdapter' | 'notPaired' | 'notificationsDenied' | 'messagesDenied' | 'unreachable';
export type SectionId = 'messages' | 'notifications' | 'files' | 'clipboard' | 'calls' | 'phone' | 'settings';

export interface Connection {
  paired: boolean;
  phoneName: string;
  pcName: string;
  wifi: LinkState;
  bluetooth: LinkState; // 'off' = disabled in settings
  bluetoothIssue?: BluetoothIssue;
  lastSeenAt: string | null;
}

export interface Battery {
  level: number | null; // 0-100, null = never received
  charging: boolean;
  updatedAt: string | null;
  source?: 'shortcut' | 'app' | 'bluetooth';
}

export interface Conversation {
  id: string;
  name: string; // contact name, or the handle when unknown
  handle: string; // phone number or iMessage address
  service: Service;
  lastMessage: string;
  lastAt: string;
  unread: number;
}

export type SendStatus = 'sending' | 'sent' | 'queued' | 'failed';

export interface Message {
  id: string;
  conversationId: string;
  fromMe: boolean;
  body: string;
  at: string;
  service: Service;
  status?: SendStatus; // only for fromMe messages sent from the PC
  via?: 'bluetooth' | 'relay';
  error?: string;
}

export type NotificationCategory = 'message' | 'social' | 'email' | 'call' | 'news' | 'other';

export interface PhoneNotification {
  id: string;
  appId: string;
  appName: string;
  category: NotificationCategory;
  title: string;
  subtitle?: string;
  body: string;
  at: string;
  positiveAction?: string; // label from ANCS, if any
  negativeAction?: string;
}

export type TransferStatus = 'queued' | 'active' | 'done' | 'failed' | 'cancelled';

export interface Transfer {
  id: string;
  name: string;
  size: number;
  transferred: number;
  direction: 'toPc' | 'toPhone';
  status: TransferStatus;
  at: string;
  error?: string;
}

export interface ClipboardItem {
  id: string;
  kind: 'text' | 'url' | 'image';
  text?: string;
  imageDataUrl?: string; // small thumbnail only (data:image/...)
  origin: 'phone' | 'pc';
  at: string;
}

export interface Call {
  id: string;
  name: string;
  handle: string;
  kind: 'incoming' | 'outgoing' | 'missed';
  at: string;
  durationSec?: number;
  ringing?: boolean; // true while the phone is ringing (answer/decline possible)
}

export interface PairedDevice {
  id: string;
  name: string;
  pairedAt: string;
  lastSeenAt: string | null;
  current: boolean;
}

export interface Settings {
  windowStyle: 'mac' | 'windows';
  theme: 'system' | 'light' | 'dark';
  toasts: boolean;
  clipboardAuto: boolean;
  otpAutoCopy: boolean;
  keepHistory: boolean;
  helloLock: boolean;
  shortcutsHttp: boolean;
  bluetooth: boolean; // ANCS notifications/calls, MAP messages, PBAP names
  downloadDir: string;
}

export interface HostInfo {
  version: string;
  mica: boolean; // window has a Mica backdrop: UI keeps the sidebar background translucent
  maximized: boolean;
}

export interface Snapshot {
  host: HostInfo;
  connection: Connection;
  battery: Battery;
  conversations: Conversation[];
  notifications: PhoneNotification[];
  transfers: Transfer[];
  clipboard: ClipboardItem[];
  calls: Call[];
  devices: PairedDevice[];
  settings: Settings;
}

export type DataSection = 'messages' | 'notifications' | 'files' | 'clipboard' | 'calls';

/** Host -> UI */
export type HostMessage =
  | { type: 'state'; snapshot: Snapshot }
  | { type: 'connection'; connection: Connection }
  | { type: 'battery'; battery: Battery }
  | { type: 'conversations'; conversations: Conversation[] }
  | { type: 'messages'; conversationId: string; messages: Message[] }
  | { type: 'message'; message: Message }
  | { type: 'notification'; notification: PhoneNotification }
  | { type: 'notificationRemoved'; id: string }
  | { type: 'notifications'; notifications: PhoneNotification[] }
  | { type: 'transfer'; transfer: Transfer }
  | { type: 'clipboard'; items: ClipboardItem[] }
  | { type: 'call'; call: Call }
  | { type: 'devices'; devices: PairedDevice[] }
  | { type: 'settings'; settings: Settings }
  | { type: 'window'; maximized: boolean }
  | { type: 'navigate'; section: SectionId; conversationId?: string }
  | { type: 'sectionError'; section: DataSection; message: string | null }
  | { type: 'toast'; text: string; kind?: 'info' | 'error' };

export type WindowAction = 'minimize' | 'maximize' | 'close' | 'drag';

/** UI -> Host */
export type UiMessage =
  | { type: 'ready' }
  | { type: 'window'; action: WindowAction }
  | { type: 'openConversation'; conversationId: string }
  | { type: 'sendMessage'; clientId: string; conversationId?: string; to?: string; body: string }
  | { type: 'retryMessage'; messageId: string }
  | { type: 'dismissNotification'; id: string }
  | { type: 'clearNotifications' }
  | { type: 'notificationAction'; id: string; action: 'positive' | 'negative' }
  | { type: 'sendFiles'; names: string[] } // File objects ride along via postMessageWithAdditionalObjects
  | { type: 'pickFiles' }
  | { type: 'cancelTransfer'; id: string }
  | { type: 'retryTransfer'; id: string }
  | { type: 'revealTransfer'; id: string }
  | { type: 'openDownloads' }
  | { type: 'copyClipboardItem'; id: string }
  | { type: 'deleteClipboardItem'; id: string }
  | { type: 'clearClipboard' }
  | { type: 'pushClipboard' }
  | { type: 'openLink'; url: string }
  | { type: 'callAction'; id: string; action: 'answer' | 'decline' }
  | { type: 'startPairing' }
  | { type: 'revokeDevice'; id: string }
  | { type: 'setSettings'; patch: Partial<Settings> }
  | { type: 'pickDownloadDir' }
  | { type: 'retrySection'; section: DataSection }
  | { type: 'openMainWindow' };
