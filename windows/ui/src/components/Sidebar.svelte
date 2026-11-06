<script lang="ts">
  import { MessageCircle, Bell, Folder, Clipboard, Phone, Smartphone, Settings } from '@lucide/svelte';
  import type { Component } from 'svelte';
  import { t } from '../i18n';
  import { app, go, phoneStatus } from '../lib/store.svelte';
  import type { SectionId } from '../lib/types';
  import BatteryGauge from './BatteryGauge.svelte';
  import WindowControls from './WindowControls.svelte';

  const DAY = 86_400_000;
  const unread = $derived(app.conversations.reduce((n, c) => n + c.unread, 0));
  const missed = $derived(app.calls.filter((c) => c.kind === 'missed' && Date.now() - Date.parse(c.at) < DAY).length);
  const status = $derived(phoneStatus(app.connection));

  const items: { id: SectionId; icon: Component<any>; badge?: () => number }[] = [
    { id: 'messages', icon: MessageCircle, badge: () => unread },
    { id: 'notifications', icon: Bell, badge: () => app.notifications.length },
    { id: 'files', icon: Folder },
    { id: 'clipboard', icon: Clipboard },
    { id: 'calls', icon: Phone, badge: () => missed },
    { id: 'phone', icon: Smartphone },
  ];

  let nav: HTMLElement;
  function onkeydown(e: KeyboardEvent) {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    e.preventDefault();
    const ids: SectionId[] = [...items.map((i) => i.id), 'settings'];
    const next = ids[(ids.indexOf(app.section) + (e.key === 'ArrowDown' ? 1 : -1) + ids.length) % ids.length]!;
    go(next);
    queueMicrotask(() => nav.querySelector<HTMLElement>(`[data-id="${next}"]`)?.focus());
  }
</script>

<aside class="sidebar">
  <div class="top drag">
    {#if app.settings.windowStyle === 'mac'}<WindowControls variant="mac" />{/if}
  </div>

  <nav aria-label={t.nav.label} bind:this={nav}>
    <ul>
      {#each items as item, i (item.id)}
        {@const count = item.badge?.() ?? 0}
        <li>
          <button
            class="item"
            {onkeydown}
            data-id={item.id}
            aria-current={app.section === item.id ? 'page' : undefined}
            tabindex={app.section === item.id ? 0 : -1}
            title="{t.nav[item.id]} (Ctrl+{i + 1})"
            onclick={() => go(item.id)}
          >
            <item.icon size={17} strokeWidth={1.6} />
            <span class="label">{t.nav[item.id]}</span>
            {#if count}<span class="count" aria-label={item.id === 'messages' ? t.messages.unread(count) : String(count)}>{count}</span>{/if}
          </button>
        </li>
      {/each}
    </ul>
    <div class="bottom">
      <button
        class="item"
            {onkeydown}
        data-id="settings"
        aria-current={app.section === 'settings' ? 'page' : undefined}
        tabindex={app.section === 'settings' ? 0 : -1}
        title="{t.nav.settings} (Ctrl+,)"
        onclick={() => go('settings')}
      >
        <Settings size={17} strokeWidth={1.6} />
        <span class="label">{t.nav.settings}</span>
      </button>
    </div>
  </nav>

  <button class="device" popovertarget="quick-menu" aria-label={t.quick.label} title={t.quick.label}>
    <span class="dot {status}" aria-hidden="true"></span>
    <span class="device-text">
      <span class="name">{app.connection.paired ? app.connection.phoneName : t.status.unpaired}</span>
      {#if status !== 'unpaired'}<span class="state">{t.status[status]}</span>{/if}
    </span>
    {#if app.connection.paired && app.battery.level != null}
      <span class="batt"><span class="pct">{app.battery.level} %</span><BatteryGauge level={app.battery.level} charging={app.battery.charging} /></span>
    {/if}
  </button>
</aside>

<style>
  .sidebar {
    width: var(--sidebar-w); flex: none; display: flex; flex-direction: column;
    background: var(--bg-sidebar); border-right: 1px solid var(--separator);
  }
  :global(html.mica) .sidebar { background: light-dark(rgb(255 255 255 / .28), rgb(32 32 32 / .3)); }
  .top { height: var(--toolbar-h); flex: none; display: flex; align-items: center; padding: 0 var(--s-4) 0 18px; }
  nav { flex: 1; display: flex; flex-direction: column; padding: 0 10px; min-height: 0; overflow: auto; }
  ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 1px; }
  .bottom { margin-top: auto; padding: var(--s-2) 0; }
  .item {
    width: 100%; height: 30px; display: flex; align-items: center; gap: 10px; padding: 0 10px;
    border: 0; border-radius: var(--r-6); background: transparent; color: var(--text); font-size: var(--fs-13);
    transition: background var(--dur-fast) var(--ease);
  }
  .item :global(svg) { color: var(--accent); flex: none; }
  .item:hover { background: var(--fill-1); }
  .item[aria-current='page'] { background: var(--fill-2); font-weight: var(--fw-medium); }
  .label { flex: 1; text-align: left; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .count { font-size: var(--fs-12); color: var(--text-2); font-variant-numeric: tabular-nums; }

  .device {
    margin: 0 10px 10px; display: flex; align-items: center; gap: 10px; padding: 8px 10px; text-align: left;
    border: 0; border-radius: var(--r-10); background: var(--fill-1);
    transition: background var(--dur-fast) var(--ease);
  }
  .device:hover { background: var(--fill-2); }
  .device-text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .name { font-size: var(--fs-12); font-weight: var(--fw-medium); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .state { font-size: 11px; color: var(--text-2); }
  .batt { display: flex; align-items: center; gap: 5px; font-size: 11px; color: var(--text-2); font-variant-numeric: tabular-nums; }
  .dot { width: 8px; height: 8px; border-radius: 50%; flex: none; background: var(--gray-4); }
  .dot.connected { background: var(--green); }
  .dot.partial { background: var(--orange); }
  .dot.away { background: var(--gray-5); }
</style>
