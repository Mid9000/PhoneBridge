<script lang="ts">
  import { tick, type Component } from 'svelte';
  import {
    ArrowRight, Bell, Clipboard, CornerDownLeft, File, MessageCircle, Monitor, Moon, Search, SquarePen,
  } from '@lucide/svelte';
  import { t } from '../i18n';
  import { stamp } from '../lib/format';
  import { app, go, send, ui } from '../lib/store.svelte';
  import type { SectionId, Settings } from '../lib/types';

  interface Item { id: string; group: string; label: string; sub?: string; icon: Component<any>; run: () => void }

  let dialog: HTMLDialogElement;
  let input = $state<HTMLInputElement>();
  let query = $state('');
  let active = $state(0);

  const sections: SectionId[] = ['messages', 'notifications', 'files', 'clipboard', 'calls', 'phone', 'settings'];
  const darkNow = () => app.settings.theme === 'dark' || (app.settings.theme === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
  const setSettings = (patch: Partial<Settings>) => { Object.assign(app.settings, patch); send({ type: 'setSettings', patch }); };

  const items = $derived.by((): Item[] => {
    const q = query.trim().toLocaleLowerCase();
    const match = (...parts: (string | undefined)[]) => !q || parts.some((p) => p?.toLocaleLowerCase().includes(q));
    const g = t.palette.groups;
    const actions: Item[] = [
      { id: 'new', group: t.palette.actions, label: t.palette.newMessage, icon: SquarePen, run: () => { go('messages'); ui.composeNew = true; app.conversationId = null; } },
      ...sections.map((s) => ({ id: `go-${s}`, group: t.palette.actions, label: t.palette.goTo(t.nav[s]), icon: ArrowRight, run: () => go(s) })),
      { id: 'theme', group: t.palette.actions, label: t.palette.toggleTheme, icon: Moon, run: () => setSettings({ theme: darkNow() ? 'light' : 'dark' }) },
      { id: 'window', group: t.palette.actions, label: t.palette.toggleWindow, icon: Monitor, run: () => setSettings({ windowStyle: app.settings.windowStyle === 'mac' ? 'windows' : 'mac' }) },
    ];
    const take = (list: Item[], n: number) => list.slice(0, n);
    return [
      ...take(actions.filter((a) => match(a.label)), q ? 5 : 3),
      ...take(app.conversations.filter((c) => match(c.name, c.handle, c.lastMessage)).map((c) => ({
        id: `c-${c.id}`, group: g.conversations, label: c.name, sub: c.lastMessage, icon: MessageCircle, run: () => go('messages', c.id),
      })), q ? 6 : 4),
      ...(q ? [
        ...take(app.notifications.filter((n) => match(n.appName, n.title, n.body)).map((n) => ({
          id: `n-${n.id}`, group: g.notifications, label: `${n.appName} — ${n.title}`, sub: n.body, icon: Bell, run: () => go('notifications'),
        })), 5),
        ...take(app.transfers.filter((x) => match(x.name)).map((x) => ({
          id: `f-${x.id}`, group: g.files, label: x.name, sub: stamp(x.at), icon: File, run: () => go('files'),
        })), 5),
        ...take(app.clipboard.filter((k) => k.text && match(k.text)).map((k) => ({
          id: `k-${k.id}`, group: g.clipboard, label: k.text!, icon: Clipboard, run: () => go('clipboard'),
        })), 5),
      ] : []),
    ];
  });

  $effect(() => { void query; active = 0; });

  $effect(() => {
    if (ui.palette && !dialog.open) {
      query = '';
      dialog.showModal();
      tick().then(() => input?.focus());
    } else if (!ui.palette && dialog.open) dialog.close();
  });

  function run(item: Item | undefined) {
    if (!item) return;
    ui.palette = false;
    item.run();
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (!items.length) return;
      active = (active + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
      tick().then(() => document.getElementById(`pal-${items[active]?.id}`)?.scrollIntoView({ block: 'nearest' }));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      run(items[active]);
    }
  }
</script>

<dialog
  bind:this={dialog}
  class="palette"
  aria-label={t.palette.label}
  onclose={() => (ui.palette = false)}
  onclick={(e) => { if (e.target === dialog) ui.palette = false; }}
>
  <div class="panel">
    <div class="field">
      <Search size={18} strokeWidth={1.75} />
      <input
        bind:this={input}
        bind:value={query}
        {onkeydown}
        placeholder={t.palette.placeholder}
        role="combobox"
        aria-expanded="true"
        aria-controls="palette-list"
        aria-activedescendant={items[active] ? `pal-${items[active].id}` : undefined}
        aria-autocomplete="list"
        autocomplete="off"
        spellcheck="false"
      />
    </div>
    <div class="results" id="palette-list" role="listbox" aria-label={t.palette.label}>
      {#each items as item, i (item.id)}
        {#if i === 0 || items[i - 1]!.group !== item.group}<div class="group-label" role="presentation">{item.group}</div>{/if}
        <div
          id="pal-{item.id}"
          class="item"
          class:active={i === active}
          role="option"
          aria-selected={i === active}
          tabindex="-1"
          onclick={() => run(item)}
          onkeydown={() => {}}
          onmousemove={() => (active = i)}
        >
          <item.icon size={16} strokeWidth={1.75} />
          <span class="label">{item.label}</span>
          {#if item.sub}<span class="sub">{item.sub}</span>{/if}
          {#if i === active}<span class="enter"><CornerDownLeft size={13} strokeWidth={1.75} /></span>{/if}
        </div>
      {:else}
        <p class="empty">{t.palette.noResults}</p>
      {/each}
    </div>
    <footer>
      <span><kbd>↑</kbd><kbd>↓</kbd> {t.palette.hintNav}</span>
      <span><kbd>Entrée</kbd> {t.palette.hintOpen}</span>
      <span><kbd>Échap</kbd> {t.palette.hintClose}</span>
    </footer>
  </div>
</dialog>

<style>
  .palette {
    width: min(600px, calc(100vw - 48px)); max-height: min(520px, calc(100vh - 140px)); margin: 12vh auto auto; padding: 0;
    border: 0; border-radius: var(--r-14); background: transparent; color: var(--text); overflow: visible;
  }
  .palette[open] { animation: pop-in var(--dur) var(--ease); }
  .palette::backdrop { background: rgb(0 0 0 / .18); animation: fade-in var(--dur) var(--ease); }
  .panel {
    display: flex; flex-direction: column; max-height: inherit; border-radius: var(--r-14); overflow: hidden;
    background: var(--bg-popover); backdrop-filter: blur(40px) saturate(1.8); box-shadow: var(--shadow-pop);
  }
  .field { display: flex; align-items: center; gap: var(--s-3); padding: 0 var(--s-4); height: 52px; color: var(--text-2); border-bottom: 1px solid var(--separator); }
  input { flex: 1; border: 0; background: transparent; font-size: var(--fs-17); color: var(--text); }
  input::placeholder { color: var(--text-3); }
  input:focus-visible { box-shadow: none; }
  .results { overflow: auto; padding: 6px; }
  .group-label { padding: 8px 10px 4px; font-size: 11px; font-weight: var(--fw-semibold); color: var(--text-2); }
  .item { display: flex; align-items: center; gap: 10px; height: 34px; padding: 0 10px; border-radius: var(--r-6); color: var(--text); }
  .item :global(svg) { flex: none; color: var(--text-2); }
  .item.active { background: var(--accent-fill); color: #fff; }
  .item.active :global(svg), .item.active .sub { color: rgb(255 255 255 / .85); }
  .label { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex: none; max-width: 55%; }
  .sub { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: var(--text-2); font-size: var(--fs-12); }
  .enter { margin-left: auto; display: grid; }
  .empty { margin: 0; padding: var(--s-6); text-align: center; color: var(--text-2); }
  footer { display: flex; gap: var(--s-4); padding: 8px var(--s-4); border-top: 1px solid var(--separator); font-size: 11px; color: var(--text-2); }
  kbd { font: inherit; font-size: 10px; padding: 1px 5px; margin-right: 2px; border-radius: 4px; background: var(--fill-2); }
</style>
