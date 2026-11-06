import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createWebViewBridge, parseHostMessage, type WebView2Like } from './bridge';
import { createMockHost } from './mock';
import { initialState, reduce } from './state';
import type { HostMessage } from './types';

describe('parseHostMessage', () => {
  it('accepts known message types, from objects or JSON strings', () => {
    expect(parseHostMessage({ type: 'battery', battery: { level: 50, charging: false, updatedAt: null } })?.type).toBe('battery');
    expect(parseHostMessage('{"type":"window","maximized":true}')).toEqual({ type: 'window', maximized: true });
  });
  it('drops anything else', () => {
    for (const bad of [null, 42, 'not json', '{"type":"evil"}', { kind: 'state' }, { type: 7 }]) expect(parseHostMessage(bad)).toBeNull();
  });
});

describe('createWebViewBridge', () => {
  function fakeWebView() {
    let listener: ((e: { data: unknown }) => void) | undefined;
    const wv: WebView2Like & { emit(data: unknown): void } = {
      postMessage: vi.fn(),
      postMessageWithAdditionalObjects: vi.fn(),
      addEventListener: (_t, l) => { listener = l; },
      emit: (data) => listener?.({ data }),
    };
    return wv;
  }

  it('posts plain messages, and files through postMessageWithAdditionalObjects', () => {
    const wv = fakeWebView();
    const bridge = createWebViewBridge(wv);
    bridge.send({ type: 'ready' });
    expect(wv.postMessage).toHaveBeenCalledWith({ type: 'ready' });
    const file = new File(['x'], 'a.txt');
    bridge.send({ type: 'sendFiles', names: ['a.txt'] }, [file]);
    expect(wv.postMessageWithAdditionalObjects).toHaveBeenCalledWith({ type: 'sendFiles', names: ['a.txt'] }, [file]);
  });

  it('delivers only valid host messages to subscribers', () => {
    const wv = fakeWebView();
    const got: HostMessage[] = [];
    const unsubscribe = createWebViewBridge(wv).subscribe((m) => got.push(m));
    wv.emit({ type: 'toast', text: 'ok' });
    wv.emit({ type: 'nope' });
    unsubscribe();
    wv.emit({ type: 'toast', text: 'late' });
    expect(got).toEqual([{ type: 'toast', text: 'ok' }]);
  });
});

describe('reduce', () => {
  it('upserts messages and transfers, tracks section errors', () => {
    const s = initialState();
    const msg = { id: 'm1', conversationId: 'c1', fromMe: true, body: 'hi', at: '2026-01-01T00:00:00Z', service: 'sms' as const, status: 'sending' as const };
    reduce(s, { type: 'message', message: msg });
    reduce(s, { type: 'message', message: { ...msg, status: 'sent' } });
    expect(s.threads.c1).toEqual([{ ...msg, status: 'sent' }]);

    const tr = { id: 't1', name: 'a', size: 10, transferred: 0, direction: 'toPhone' as const, status: 'queued' as const, at: '' };
    reduce(s, { type: 'transfer', transfer: { ...tr, id: 't0' } });
    reduce(s, { type: 'transfer', transfer: tr });
    reduce(s, { type: 'transfer', transfer: { ...tr, transferred: 10, status: 'done' } });
    expect(s.transfers.map((x) => `${x.id}:${x.status}`)).toEqual(['t1:done', 't0:queued']);

    reduce(s, { type: 'sectionError', section: 'files', message: 'boom' });
    expect(s.sectionErrors.files).toBe('boom');
    reduce(s, { type: 'sectionError', section: 'files', message: null });
    expect(s.sectionErrors.files).toBeUndefined();
  });
});

describe('mock host', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const start = (query: string) => {
    const out: HostMessage[] = [];
    const handle = createMockHost(new URLSearchParams(query), (m) => out.push(structuredClone(m)));
    handle({ type: 'ready' });
    vi.advanceTimersByTime(200);
    return { out, handle };
  };

  it('answers ready with a populated snapshot', () => {
    const { out } = start('');
    const state = out.find((m) => m.type === 'state');
    expect(state?.type === 'state' && state.snapshot.conversations.length).toBeGreaterThan(5);
  });

  it('sends a message: sending, then sent over bluetooth when connected', () => {
    const { out, handle } = start('');
    handle({ type: 'sendMessage', clientId: 'x1', conversationId: 'c-lea', body: 'Salut' });
    vi.advanceTimersByTime(1000);
    const states = out.flatMap((m) => (m.type === 'message' && m.message.id === 'x1' ? [m.message.status] : []));
    expect(states).toEqual(['sending', 'sent']);
  });

  it('queues replies for the Shortcuts relay when the phone is away', () => {
    const { out, handle } = start('scenario=disconnected');
    handle({ type: 'sendMessage', clientId: 'x2', conversationId: 'c-lea', body: 'Salut' });
    vi.advanceTimersByTime(1000);
    const last = out.filter((m) => m.type === 'message').at(-1);
    expect(last?.type === 'message' && last.message.status).toBe('queued');
  });

  it('turning Bluetooth off answers settings and an "off" connection, then replies go to the relay', () => {
    const { out, handle } = start('');
    handle({ type: 'setSettings', patch: { bluetooth: false } });
    const conn = out.filter((m) => m.type === 'connection').at(-1);
    expect(conn?.type === 'connection' && conn.connection.bluetooth).toBe('off');
    handle({ type: 'sendMessage', clientId: 'x3', conversationId: 'c-lea', body: 'Salut' });
    vi.advanceTimersByTime(1000);
    const last = out.filter((m) => m.type === 'message').at(-1);
    expect(last?.type === 'message' && last.message.status).toBe('queued');
  });

  it('honours preview params (unpaired, theme)', () => {
    const { out } = start('scenario=unpaired&theme=dark');
    const state = out.find((m) => m.type === 'state');
    expect(state?.type === 'state' && [state.snapshot.connection.paired, state.snapshot.settings.theme]).toEqual([false, 'dark']);
  });
});
