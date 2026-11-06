import { t } from '../i18n';
import { createBridge } from './bridge';
import { initialState, reduce } from './state';
import type { Connection, HostMessage, SectionId, UiMessage } from './types';

export const bridge = createBridge();
export const app = $state(initialState());

/** Purely local UI state (never sent to the host). */
export const ui = $state({ palette: false, composeNew: false });

function apply(msg: HostMessage) {
  reduce(app, msg);
  if (msg.type === 'navigate' && msg.conversationId) ui.composeNew = false;
  if (msg.type === 'toast') {
    const id = app.toasts.at(-1)!.id;
    setTimeout(() => (app.toasts = app.toasts.filter((x) => x.id !== id)), 3800);
  }
}
bridge.subscribe(apply);

export const send = (message: UiMessage, files?: File[]) => bridge.send(message, files);

/** Local feedback that does not come from the host. */
export const notify = (text: string, kind: 'info' | 'error' = 'info') => apply({ type: 'toast', text, kind });

export function go(section: SectionId, conversationId?: string) {
  app.section = section;
  if (conversationId) { app.conversationId = conversationId; ui.composeNew = false; }
}

export type PhoneStatus = 'unpaired' | 'connected' | 'partial' | 'away';
export function phoneStatus(c: Connection): PhoneStatus {
  if (!c.paired) return 'unpaired';
  const up = [c.wifi, c.bluetooth].filter((s) => s === 'connected').length;
  return up === 2 ? 'connected' : up === 1 ? 'partial' : 'away';
}

export function sendFiles(files: File[]) {
  if (!files.length) return;
  if (!app.connection.paired) return notify(t.files.disabled, 'error');
  send({ type: 'sendFiles', names: files.map((f) => f.name) }, files);
  go('files');
}
