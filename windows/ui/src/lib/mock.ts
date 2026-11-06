// In-browser fake host: answers UiMessages the way the WebView2 host will, with a rich dataset.
import type { Bridge } from './bridge';
import { createDataset, parseScenario } from './mock-data';
import type { DataSection, HostMessage, Message, SectionId, Transfer, UiMessage } from './types';

const REPLIES = ['D’accord !', 'Ça marche 👍', 'Super, merci', 'Ok, à tout à l’heure', 'Haha 😄'];

/** params (preview URL query): scenario=populated|empty|disconnected|unpaired|error|call, theme=light|dark, window=mac|windows, section=<SectionId> */
export function createMockHost(params: URLSearchParams, emit: (m: HostMessage) => void) {
  const scenario = parseScenario(params.get('scenario'));
  const { snapshot: s, threads } = createDataset(scenario);
  const theme = params.get('theme');
  if (theme === 'light' || theme === 'dark') s.settings.theme = theme;
  if (params.get('window') === 'windows') s.settings.windowStyle = 'windows';
  const section = params.get('section') as SectionId | null;
  const later = (ms: number, fn: () => void) => setTimeout(fn, ms);
  const now = () => new Date().toISOString();
  const online = () => s.connection.paired && s.connection.bluetooth === 'connected';
  const toast = (text: string) => emit({ type: 'toast', text });
  let seq = 0;

  const failing = new Set<DataSection>(scenario === 'error' ? ['messages', 'notifications', 'files', 'clipboard', 'calls'] : []);

  const pushConversations = () => {
    s.conversations.sort((a, b) => b.lastAt.localeCompare(a.lastAt));
    emit({ type: 'conversations', conversations: s.conversations.map((c) => ({ ...c })) });
  };

  const upsertMessage = (m: Message) => {
    const list = (threads[m.conversationId] ??= []);
    const i = list.findIndex((x) => x.id === m.id);
    if (i >= 0) list[i] = m; else list.push(m);
    const conv = s.conversations.find((c) => c.id === m.conversationId);
    if (conv) { conv.lastMessage = m.body; conv.lastAt = m.at; }
    emit({ type: 'message', message: { ...m } });
    pushConversations();
  };

  const deliver = (m: Message) => {
    later(700, () => upsertMessage(online() ? { ...m, status: 'sent', via: 'bluetooth' } : { ...m, status: 'queued', via: 'relay' }));
    if (online()) later(2600, () => upsertMessage({
      id: `in-${++seq}`, conversationId: m.conversationId, fromMe: false, at: now(), service: m.service,
      body: REPLIES[seq % REPLIES.length],
    }));
  };

  const timers = new Map<string, ReturnType<typeof setInterval>>();
  const runTransfer = (t: Transfer) => {
    clearInterval(timers.get(t.id));
    t.status = 'active';
    emit({ type: 'transfer', transfer: { ...t } });
    timers.set(t.id, setInterval(() => {
      t.transferred = Math.min(t.size, t.transferred + Math.max(t.size / 40, 256 * 1024));
      if (t.transferred >= t.size) { t.status = 'done'; clearInterval(timers.get(t.id)); }
      emit({ type: 'transfer', transfer: { ...t } });
    }, 450));
  };

  const emitSection = (section: DataSection) => {
    if (section === 'messages') pushConversations();
    if (section === 'notifications') emit({ type: 'notifications', notifications: s.notifications });
    if (section === 'files') s.transfers.forEach((t) => emit({ type: 'transfer', transfer: t }));
    if (section === 'clipboard') emit({ type: 'clipboard', items: s.clipboard });
    if (section === 'calls') s.calls.forEach((c) => emit({ type: 'call', call: c }));
  };

  return function handle(msg: UiMessage, files?: File[]) {
    switch (msg.type) {
      case 'ready':
        later(scenario === 'populated' ? 150 : 50, () => {
          emit({ type: 'state', snapshot: structuredClone(s) });
          if (section) emit({ type: 'navigate', section });
          failing.forEach((section) => emit({ type: 'sectionError', section, message: 'La base de données locale n’a pas pu être lue.' }));
          s.transfers.filter((t) => t.status === 'active').forEach(runTransfer);
        });
        break;
      case 'openConversation': {
        const conv = s.conversations.find((c) => c.id === msg.conversationId);
        if (conv?.unread) { conv.unread = 0; pushConversations(); }
        later(60, () => emit({ type: 'messages', conversationId: msg.conversationId, messages: (threads[msg.conversationId] ?? []).map((m) => ({ ...m })) }));
        break;
      }
      case 'sendMessage': {
        let convId = msg.conversationId;
        if (!convId && msg.to) {
          const to = msg.to;
          const existing = s.conversations.find((c) => c.handle === to || c.name.toLowerCase() === to.toLowerCase());
          convId = existing?.id ?? `c-new-${++seq}`;
          if (!existing) s.conversations.unshift({ id: convId, name: to, handle: to, service: 'sms', lastMessage: '', lastAt: now(), unread: 0 });
          later(0, () => emit({ type: 'navigate', section: 'messages', conversationId: convId }));
        }
        if (!convId) return;
        const service = s.conversations.find((c) => c.id === convId)?.service ?? 'sms';
        const m: Message = { id: msg.clientId, conversationId: convId, fromMe: true, body: msg.body, at: now(), service, status: 'sending' };
        upsertMessage(m);
        deliver(m);
        break;
      }
      case 'retryMessage': {
        const m = Object.values(threads).flat().find((x) => x.id === msg.messageId);
        if (m) { const again = { ...m, status: 'sending' as const, error: undefined, at: now() }; upsertMessage(again); deliver(again); }
        break;
      }
      case 'dismissNotification':
      case 'notificationAction':
        s.notifications = s.notifications.filter((n) => n.id !== msg.id);
        emit({ type: 'notificationRemoved', id: msg.id });
        break;
      case 'clearNotifications':
        s.notifications = [];
        emit({ type: 'notifications', notifications: [] });
        break;
      case 'sendFiles':
        msg.names.forEach((name, i) => {
          const t: Transfer = { id: `t-new-${++seq}`, name, size: files?.[i]?.size || 4 * 1024 * 1024, transferred: 0, direction: 'toPhone', status: 'queued', at: now() };
          s.transfers.unshift(t);
          emit({ type: 'transfer', transfer: { ...t } });
          if (online()) later(300, () => runTransfer(t));
        });
        break;
      case 'cancelTransfer':
      case 'retryTransfer': {
        const t = s.transfers.find((x) => x.id === msg.id);
        if (!t) return;
        if (msg.type === 'retryTransfer') { t.error = undefined; runTransfer(t); return; }
        clearInterval(timers.get(t.id));
        t.status = 'cancelled';
        emit({ type: 'transfer', transfer: { ...t } });
        break;
      }
      case 'revealTransfer':
      case 'openDownloads':
        toast('Aperçu : l’hôte ouvrirait le dossier dans l’Explorateur.');
        break;
      case 'copyClipboardItem': {
        const item = s.clipboard.find((k) => k.id === msg.id);
        if (item?.text) navigator.clipboard?.writeText(item.text).catch(() => {});
        toast('Copié dans le presse-papiers du PC');
        break;
      }
      case 'deleteClipboardItem':
        s.clipboard = s.clipboard.filter((k) => k.id !== msg.id);
        emit({ type: 'clipboard', items: s.clipboard });
        break;
      case 'clearClipboard':
        s.clipboard = [];
        emit({ type: 'clipboard', items: [] });
        break;
      case 'pushClipboard':
        s.clipboard.unshift({ id: `k-new-${++seq}`, kind: 'text', text: 'Texte copié sur le PC', origin: 'pc', at: now() });
        emit({ type: 'clipboard', items: s.clipboard });
        toast('Presse-papiers du PC envoyé à l’iPhone');
        break;
      case 'openLink':
        if (/^https?:\/\//i.test(msg.url)) window.open(msg.url, '_blank', 'noopener');
        break;
      case 'callAction': {
        const c = s.calls.find((x) => x.id === msg.id);
        if (!c) return;
        c.ringing = false;
        if (msg.action === 'decline') c.kind = 'missed';
        emit({ type: 'call', call: { ...c } });
        if (msg.action === 'answer') toast('Décrochez sur l’iPhone : l’audio reste sur le téléphone.');
        break;
      }
      case 'startPairing':
        toast('Aperçu : l’hôte afficherait le QR code d’appairage.');
        break;
      case 'revokeDevice':
        s.devices = s.devices.filter((d) => d.id !== msg.id);
        emit({ type: 'devices', devices: s.devices });
        break;
      case 'setSettings':
        s.settings = { ...s.settings, ...msg.patch };
        emit({ type: 'settings', settings: { ...s.settings } });
        if (msg.patch.bluetooth !== undefined) {
          s.connection = { ...s.connection, bluetooth: msg.patch.bluetooth ? 'connected' : 'off', bluetoothIssue: undefined };
          emit({ type: 'connection', connection: { ...s.connection } });
        }
        break;
      case 'pickDownloadDir':
        toast('Aperçu : l’hôte ouvrirait le sélecteur de dossier.');
        break;
      case 'retrySection':
        failing.delete(msg.section);
        later(400, () => { emit({ type: 'sectionError', section: msg.section, message: null }); emitSection(msg.section); });
        break;
      case 'window':
        if (msg.action === 'maximize') { s.host.maximized = !s.host.maximized; emit({ type: 'window', maximized: s.host.maximized }); }
        if (msg.action === 'close' || msg.action === 'minimize') toast(msg.action === 'close' ? 'Aperçu : la fenêtre se fermerait (l’app reste dans la zone de notification).' : 'Aperçu : la fenêtre serait réduite.');
        break;
      case 'openMainWindow':
        break;
    }
  };
}

export function createMockBridge(params: URLSearchParams): Bridge {
  const listeners = new Set<(m: HostMessage) => void>();
  // structuredClone mimics the JSON boundary of the real host: the UI never shares objects with the mock.
  const handle = createMockHost(params, (m) => listeners.forEach((l) => l(structuredClone(m))));
  return {
    mode: 'mock',
    send: (message, files) => handle(message, files),
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}
