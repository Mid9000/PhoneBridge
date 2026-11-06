<script lang="ts">
  import { tick } from 'svelte';
  import { ArrowUp, CircleAlert, MessageCircle, MessagesSquare, Search, SquarePen, RotateCcw } from '@lucide/svelte';
  import { t } from '../i18n';
  import { daySeparator, stamp } from '../lib/format';
  import { app, phoneStatus, send, ui } from '../lib/store.svelte';
  import type { Message } from '../lib/types';
  import Avatar from '../components/Avatar.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import StatusBanner from '../components/StatusBanner.svelte';
  import Toolbar from '../components/Toolbar.svelte';

  let query = $state('');
  let draft = $state('');
  let to = $state('');
  let scroller = $state<HTMLElement>();
  let composer = $state<HTMLTextAreaElement>();
  let listEl = $state<HTMLElement>();

  const error = $derived(app.sectionErrors.messages);
  const filtered = $derived.by(() => {
    const q = query.trim().toLocaleLowerCase();
    return q ? app.conversations.filter((c) => `${c.name} ${c.handle} ${c.lastMessage}`.toLocaleLowerCase().includes(q)) : app.conversations;
  });
  const current = $derived(ui.composeNew ? undefined : app.conversations.find((c) => c.id === app.conversationId));
  const thread = $derived(current ? app.threads[current.id] : undefined);
  const online = $derived(phoneStatus(app.connection) === 'connected' || app.connection.bluetooth === 'connected');
  const canSend = $derived(app.connection.paired && draft.trim().length > 0 && (!ui.composeNew || to.trim().length > 0));
  const lastMineId = $derived(thread?.findLast((m) => m.fromMe)?.id);

  // Default selection: most recent conversation.
  $effect(() => {
    if (!app.conversationId && !ui.composeNew && app.conversations[0]) app.conversationId = app.conversations[0].id;
  });

  // Load the thread and mark it read whenever the selection changes (primitive derived: no re-run on list refresh).
  const currentId = $derived(current?.id);
  $effect(() => {
    if (currentId) send({ type: 'openConversation', conversationId: currentId });
  });

  // Keep the newest message in view.
  $effect(() => {
    void thread?.length;
    void currentId;
    tick().then(() => scroller?.scrollTo({ top: scroller.scrollHeight }));
  });

  $effect(() => {
    if (ui.composeNew) tick().then(() => document.getElementById('compose-to')?.focus());
  });

  function select(id: string) {
    ui.composeNew = false;
    app.conversationId = id;
  }

  function onListKey(e: KeyboardEvent) {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    e.preventDefault();
    const i = filtered.findIndex((c) => c.id === app.conversationId);
    const next = filtered[Math.max(0, Math.min(filtered.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))];
    if (!next) return;
    select(next.id);
    tick().then(() => listEl?.querySelector<HTMLElement>(`[data-id="${next.id}"]`)?.focus());
  }

  function submit() {
    if (!canSend) return;
    const body = draft.trim();
    const clientId = `ui-${crypto.randomUUID()}`;
    if (ui.composeNew) send({ type: 'sendMessage', clientId, to: to.trim(), body });
    else if (current) send({ type: 'sendMessage', clientId, conversationId: current.id, body });
    draft = '';
    to = '';
    composer?.focus();
  }

  function onComposerKey(e: KeyboardEvent) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); submit(); }
  }

  function newMessage() {
    ui.composeNew = true;
    app.conversationId = null;
  }

  /** Separator before a message when it starts a new day or follows a long gap. */
  const needsSeparator = (list: Message[], i: number) =>
    i === 0 || Date.parse(list[i]!.at) - Date.parse(list[i - 1]!.at) > 60 * 60_000;
  const sameSender = (a?: Message, b?: Message) => !!a && !!b && a.fromMe === b.fromMe;
</script>

<Toolbar title={t.nav.messages}>
  <button class="icon-btn" onclick={newMessage} aria-label={t.messages.newMessage} title="{t.messages.newMessage} (Ctrl+N)" disabled={!app.connection.paired}>
    <SquarePen size={17} strokeWidth={1.6} />
  </button>
</Toolbar>
<StatusBanner />

{#if error}
  <EmptyState icon={CircleAlert} tone="error" title={t.common.errorTitle} body={error}>
    <button class="btn" onclick={() => send({ type: 'retrySection', section: 'messages' })}>{t.common.retry}</button>
  </EmptyState>
{:else if !app.conversations.length && !ui.composeNew}
  <EmptyState icon={MessagesSquare} title={t.messages.emptyTitle} body={t.messages.emptyBody} />
{:else}
  <div class="split">
    <section class="list-pane" aria-label={t.messages.list}>
      <label class="search-field">
        <Search size={14} strokeWidth={1.75} />
        <span class="sr-only">{t.messages.search}</span>
        <input type="search" placeholder={t.messages.search} bind:value={query} />
      </label>
      <div class="list" bind:this={listEl} onkeydown={onListKey} role="listbox" tabindex="-1" aria-label={t.messages.list}>
        {#if ui.composeNew}
          <div class="row selected new" role="option" aria-selected="true">
            <Avatar name="" size={40} />
            <div class="row-main"><div class="row-top"><span class="name">{to || t.messages.newMessage}</span></div></div>
          </div>
        {/if}
        {#each filtered as c (c.id)}
          {@const selected = c.id === app.conversationId}
          <button
            class="row"
            class:selected
            data-id={c.id}
            role="option"
            aria-selected={selected}
            tabindex={selected || (!app.conversationId && c === filtered[0]) ? 0 : -1}
            onclick={() => select(c.id)}
          >
            <span class="unread-dot" class:on={c.unread > 0} aria-hidden="true"></span>
            <Avatar name={c.name} size={40} />
            <span class="row-main">
              <span class="row-top">
                <span class="name">{c.name}</span>
                <time datetime={c.lastAt}>{stamp(c.lastAt)}</time>
              </span>
              <span class="preview">{c.lastMessage}</span>
              {#if c.unread}<span class="sr-only">{t.messages.unread(c.unread)}</span>{/if}
            </span>
          </button>
        {:else}
          <p class="no-results">{t.messages.noResults(query)}</p>
        {/each}
      </div>
    </section>

    <section class="thread-pane" aria-label={current ? t.messages.thread(current.name) : t.messages.newMessage}>
      {#if ui.composeNew}
        <div class="thread-head to-row">
          <label for="compose-to">{t.messages.to}</label>
          <input id="compose-to" class="to-input" placeholder={t.messages.toPlaceholder} bind:value={to} autocomplete="off" />
        </div>
        <div class="messages"></div>
      {:else if current}
        <div class="thread-head">
          <Avatar name={current.name} size={28} />
          <div class="head-text">
            <span class="head-name">{current.name}</span>
            <span class="head-sub">{t.service[current.service]} · {current.handle}</span>
          </div>
        </div>
        <div class="messages selectable" bind:this={scroller} aria-live="polite" aria-relevant="additions">
          <p class="note">{t.messages.iphoneNote}</p>
          {#each thread ?? [] as m, i (m.id)}
            {@const sep = needsSeparator(thread!, i)}
            {@const prev = sep ? undefined : thread![i - 1]}
            {@const next = thread![i + 1] && !needsSeparator(thread!, i + 1) ? thread![i + 1] : undefined}
            {#if sep}<p class="sep"><time datetime={m.at}>{daySeparator(m.at)}</time></p>{/if}
            <div class="msg" class:me={m.fromMe} class:joined-top={sameSender(prev, m)} class:joined-bottom={sameSender(m, next)}>
              <span class="sr-only">{m.fromMe ? t.messages.you : current.name} :</span>
              <div class="bubble {m.fromMe ? m.service : 'in'}" class:pending={m.status === 'sending'} class:failed={m.status === 'failed'}>{m.body}</div>
              {#if m.fromMe && m.status && (m.id === lastMineId || m.status === 'failed' || m.status === 'queued')}
                <div class="status {m.status}">
                  {#if m.status === 'failed'}
                    <CircleAlert size={12} strokeWidth={2} />
                    <span>{t.messages.sendStatus.failed}{m.error ? ` · ${m.error}` : ''}</span>
                    <button class="link" onclick={() => send({ type: 'retryMessage', messageId: m.id })}><RotateCcw size={11} strokeWidth={2} />{t.common.retry}</button>
                  {:else}
                    {t.messages.sendStatus[m.status]}
                  {/if}
                </div>
              {/if}
            </div>
          {/each}
        </div>
      {:else}
        <EmptyState icon={MessageCircle} title={t.messages.selectTitle} body={t.messages.selectBody} />
      {/if}

      {#if current || ui.composeNew}
        {#if app.connection.paired && !online}<p class="offline">{t.messages.offlineNote}</p>{/if}
        <form class="composer" onsubmit={(e) => { e.preventDefault(); submit(); }}>
          <div class="pill">
            <textarea
              bind:this={composer}
              bind:value={draft}
              rows="1"
              placeholder={t.messages.placeholder[current?.service ?? 'sms']}
              aria-label={t.messages.composer}
              onkeydown={onComposerKey}
              disabled={!app.connection.paired}
            ></textarea>
            <button type="submit" class="send {current?.service ?? 'sms'}" disabled={!canSend} aria-label={t.messages.send} title={t.messages.send}>
              <ArrowUp size={16} strokeWidth={2.5} />
            </button>
          </div>
        </form>
      {/if}
    </section>
  </div>
{/if}

<style>
  .split { flex: 1; min-height: 0; display: flex; border-top: 1px solid var(--separator); }
  .list-pane {
    width: 300px; flex: none; display: flex; flex-direction: column; gap: var(--s-2);
    padding: var(--s-3) var(--s-2) 0; border-right: 1px solid var(--separator);
  }
  .list-pane .search-field { margin: 0 var(--s-1); }
  .list { flex: 1; overflow: auto; padding: 2px 0 var(--s-2); display: flex; flex-direction: column; gap: 1px; }
  .row {
    position: relative; display: flex; align-items: center; gap: 10px; width: 100%;
    padding: 9px 10px 9px 16px; border: 0; border-radius: var(--r-10); background: transparent; text-align: left;
    transition: background var(--dur-fast) var(--ease);
  }
  .row:hover { background: var(--fill-1); }
  .row.selected { background: var(--accent-fill); color: #fff; }
  .row.selected .preview, .row.selected time { color: rgb(255 255 255 / .82); }
  .row:focus-visible { border-radius: var(--r-10); }
  .unread-dot { position: absolute; left: 4px; width: 8px; height: 8px; border-radius: 50%; }
  .unread-dot.on { background: var(--accent); }
  .row.selected .unread-dot.on { background: #fff; }
  .row-main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
  .row-top { display: flex; align-items: baseline; gap: var(--s-2); }
  .name { flex: 1; font-weight: var(--fw-semibold); font-size: var(--fs-13); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  time { font-size: var(--fs-12); color: var(--text-2); white-space: nowrap; }
  .preview {
    font-size: var(--fs-13); color: var(--text-2); line-height: 1.35;
    display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
  }
  .no-results { color: var(--text-2); text-align: center; padding: var(--s-6) var(--s-2); margin: 0; }

  .thread-pane { flex: 1; min-width: 0; display: flex; flex-direction: column; background: var(--bg); }
  .thread-head {
    flex: none; display: flex; align-items: center; gap: 10px; height: 52px; padding: 0 var(--s-5);
    border-bottom: 1px solid var(--separator);
  }
  .head-text { display: flex; flex-direction: column; min-width: 0; }
  .head-name { font-weight: var(--fw-semibold); }
  .head-sub { font-size: var(--fs-12); color: var(--text-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .to-row label { color: var(--text-2); }
  .to-input { flex: 1; border: 0; background: transparent; font-size: var(--fs-13); padding: 6px 0; }
  .to-input:focus-visible { box-shadow: none; }

  .messages { flex: 1; overflow: auto; padding: var(--s-4) var(--s-5) var(--s-2); display: flex; flex-direction: column; }
  .note { margin: 0 auto var(--s-2); font-size: 11px; color: var(--text-2); text-align: center; max-width: 420px; }
  .sep { margin: var(--s-4) 0 var(--s-2); text-align: center; font-size: 11px; color: var(--text-2); font-weight: var(--fw-medium); }
  .msg { display: flex; flex-direction: column; align-items: flex-start; margin-top: 8px; animation: rise-in var(--dur) var(--ease); }
  .msg.joined-top { margin-top: 2px; }
  .msg.me { align-items: flex-end; }
  .bubble {
    max-width: min(72%, 520px); padding: 7px 12px; border-radius: 18px; font-size: var(--fs-13); line-height: 1.38;
    white-space: pre-wrap; overflow-wrap: anywhere; background: var(--bubble-in); color: var(--text);
    transition: opacity var(--dur) var(--ease);
  }
  .bubble.imessage { background: var(--accent-fill); color: #fff; }
  .bubble.sms { background: var(--green-fill); color: #fff; }
  .msg:not(.me).joined-bottom .bubble { border-bottom-left-radius: 6px; }
  .msg:not(.me).joined-top .bubble { border-top-left-radius: 6px; }
  .msg.me.joined-bottom .bubble { border-bottom-right-radius: 6px; }
  .msg.me.joined-top .bubble { border-top-right-radius: 6px; }
  .bubble.pending { opacity: .6; }
  .bubble.failed { opacity: .75; }
  .status { display: flex; align-items: center; gap: 5px; margin: 3px 4px 0; font-size: 11px; color: var(--text-2); }
  .status.failed { color: var(--red-text); }
  .status.queued { color: var(--orange-text); }
  .link {
    display: inline-flex; align-items: center; gap: 3px; border: 0; padding: 0; background: none;
    color: var(--accent-text); font-size: 11px; font-weight: var(--fw-medium);
  }
  .link:hover { text-decoration: underline; }

  .offline { margin: 0 var(--s-5); font-size: var(--fs-12); color: var(--orange-text); }
  .composer { flex: none; padding: var(--s-2) var(--s-4) var(--s-4); }
  .pill {
    display: flex; align-items: flex-end; gap: var(--s-2); padding: 4px 4px 4px 14px;
    border: 1px solid var(--border); border-radius: 18px; background: var(--bg);
    transition: border-color var(--dur-fast) var(--ease), box-shadow var(--dur-fast) var(--ease);
  }
  .pill:focus-within { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-soft); }
  textarea {
    flex: 1; resize: none; border: 0; background: transparent; padding: 5px 0; min-height: 26px; max-height: 140px;
    field-sizing: content; font-size: var(--fs-13); line-height: 1.4;
  }
  textarea:focus-visible { box-shadow: none; }
  textarea::placeholder { color: var(--text-3); }
  .send {
    flex: none; width: 28px; height: 28px; border-radius: 50%; border: 0; display: grid; place-items: center;
    background: var(--accent-fill); color: #fff; transition: transform var(--dur-fast) var(--ease), opacity var(--dur-fast) var(--ease);
  }
  .send.sms { background: var(--green-fill); }
  .send:disabled { background: var(--fill-2); color: var(--text-3); }
  .send:not(:disabled):active { transform: scale(.92); }
  .send:focus-visible { border-radius: 50%; }
</style>
