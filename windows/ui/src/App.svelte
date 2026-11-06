<script lang="ts">
  import { onMount } from 'svelte';
  import { PlugZap, Upload } from '@lucide/svelte';
  import { t } from './i18n';
  import { app, bridge, go, send, sendFiles, ui } from './lib/store.svelte';
  import type { SectionId } from './lib/types';
  import EmptyState from './components/EmptyState.svelte';
  import Palette from './components/Palette.svelte';
  import PreviewBar from './components/PreviewBar.svelte';
  import QuickMenu from './components/QuickMenu.svelte';
  import Sidebar from './components/Sidebar.svelte';
  import Toasts from './components/Toasts.svelte';
  import Toolbar from './components/Toolbar.svelte';
  import Calls from './views/Calls.svelte';
  import Clipboard from './views/Clipboard.svelte';
  import Files from './views/Files.svelte';
  import Messages from './views/Messages.svelte';
  import Notifications from './views/Notifications.svelte';
  import Phone from './views/Phone.svelte';
  import Settings from './views/Settings.svelte';

  const views = { messages: Messages, notifications: Notifications, files: Files, clipboard: Clipboard, calls: Calls, phone: Phone, settings: Settings };
  const order: SectionId[] = ['messages', 'notifications', 'files', 'clipboard', 'calls', 'phone'];

  const standalone = location.hash === '#quick'; // host tray popup window
  const framed = bridge.mode === 'mock' && !standalone; // browser preview: draw a fake window on a backdrop
  let hostSilent = $state(false);
  let dragDepth = $state(0);
  const View = $derived(views[app.section]);

  $effect(() => {
    const root = document.documentElement;
    if (app.settings.theme === 'system') delete root.dataset.theme;
    else root.dataset.theme = app.settings.theme;
    root.classList.toggle('mica', app.host.mica && !framed);
  });

  function hello() {
    hostSilent = false;
    send({ type: 'ready' });
    setTimeout(() => (hostSilent = !app.ready), 4000);
  }
  onMount(() => {
    hello();
    // Preview-only deep links for screenshots: ?open=palette|quick
    const open = bridge.mode === 'mock' ? new URLSearchParams(location.search).get('open') : null;
    if (open) setTimeout(() => (open === 'palette' ? (ui.palette = true) : document.getElementById('quick-menu')?.showPopover()), 300);
  });

  function onkeydown(e: KeyboardEvent) {
    if (!e.ctrlKey || e.altKey) return;
    const k = e.key.toLowerCase();
    if (k === 'k') { e.preventDefault(); ui.palette = !ui.palette; }
    else if (k === 'n' && app.connection.paired) { e.preventDefault(); go('messages'); ui.composeNew = true; app.conversationId = null; }
    else if (k === ',') { e.preventDefault(); go('settings'); }
    else if (/^[1-6]$/.test(k)) { e.preventDefault(); go(order[Number(k) - 1]!); }
  }

  // Fallback when the host has no CSS drag regions: ask it to start a native move (or maximize on double-click).
  function onmousedown(e: MouseEvent) {
    if (bridge.mode !== 'webview' || e.button !== 0) return;
    const target = e.target as HTMLElement;
    if (!target.closest('.drag') || target.closest('button, input, textarea, a, select, [role="button"]')) return;
    send({ type: 'window', action: e.detail === 2 ? 'maximize' : 'drag' });
  }

  const hasFiles = (e: DragEvent) => !!e.dataTransfer?.types.includes('Files');
  function ondragenter(e: DragEvent) { if (hasFiles(e)) { e.preventDefault(); dragDepth++; } }
  function ondragleave(e: DragEvent) { if (hasFiles(e)) dragDepth = Math.max(0, dragDepth - 1); }
  function ondragover(e: DragEvent) { if (hasFiles(e)) e.preventDefault(); }
  function ondrop(e: DragEvent) {
    if (!hasFiles(e)) return;
    e.preventDefault();
    dragDepth = 0;
    sendFiles([...(e.dataTransfer?.files ?? [])]);
  }
</script>

<svelte:window {onkeydown} {onmousedown} {ondragenter} {ondragleave} {ondragover} {ondrop} />

{#if standalone}
  <QuickMenu standalone />
{:else}
  <div class="desktop" class:framed>
    {#if framed}<PreviewBar />{/if}
    <div class="window" class:maximized={app.host.maximized}>
      <Sidebar />
      <main class="content">
        {#if !app.ready}
          <Toolbar title={t.appName} />
          {#if hostSilent}
            <EmptyState icon={PlugZap} tone="error" title={t.app.hostSilentTitle} body={t.app.hostSilentBody}>
              <button class="btn" onclick={hello}>{t.common.retry}</button>
            </EmptyState>
          {:else}
            <div class="loading" aria-busy="true"><span class="spinner"></span>{t.common.loading}</div>
          {/if}
        {:else}
          {#key app.section}
            <div class="view"><View /></div>
          {/key}
        {/if}
        {#if dragDepth > 0}
          <div class="drop-overlay" aria-hidden="true">
            <div><Upload size={28} strokeWidth={1.6} /><span>{t.files.overlay}</span></div>
          </div>
        {/if}
      </main>
    </div>
  </div>
  <QuickMenu />
  <Palette />
{/if}
<Toasts />

<style>
  .desktop { height: 100%; }
  .window { height: 100%; display: flex; overflow: hidden; background: var(--bg); }
  :global(html.mica) .window { background: transparent; }
  .content { flex: 1; min-width: 0; display: flex; flex-direction: column; position: relative; background: var(--bg); }
  .view { flex: 1; min-height: 0; display: flex; flex-direction: column; animation: fade-in var(--dur) var(--ease); }

  /* Browser preview only: a window floating on a neutral backdrop. */
  .framed {
    display: grid; place-items: center; padding: 56px 28px 28px;
    background:
      radial-gradient(1200px 700px at 15% 10%, light-dark(#d9e6ff, #1b2a4a), transparent 60%),
      radial-gradient(900px 600px at 90% 90%, light-dark(#ffe1ec, #3a1f35), transparent 60%),
      light-dark(#eef0f6, #101114);
  }
  .framed .window {
    width: min(1240px, 100%); height: min(800px, 100%);
    border-radius: var(--r-window); box-shadow: var(--shadow-window);
    outline: 1px solid light-dark(transparent, rgb(255 255 255 / .1)); outline-offset: -1px;
  }
  .framed .window.maximized { width: 100%; height: 100%; }
  @media (max-width: 900px), (max-height: 600px) {
    .framed { padding: 0; }
    .framed .window { width: 100%; height: 100%; border-radius: 0; }
    .framed :global(.bar) { display: none; }
  }

  .loading { flex: 1; display: flex; align-items: center; justify-content: center; gap: var(--s-2); color: var(--text-2); }
  .spinner { width: 14px; height: 14px; border-radius: 50%; border: 2px solid var(--fill-3); border-top-color: var(--accent); animation: spin 0.8s linear infinite; }
  @keyframes spin { to { transform: rotate(360deg); } }

  .drop-overlay {
    position: absolute; inset: 8px; z-index: 5; display: grid; place-items: center; pointer-events: none;
    border: 2px dashed var(--accent); border-radius: var(--r-14);
    background: light-dark(rgb(240 246 255 / .86), rgb(20 30 48 / .86)); backdrop-filter: blur(6px);
    animation: fade-in var(--dur-fast) var(--ease);
  }
  .drop-overlay div { display: flex; flex-direction: column; align-items: center; gap: var(--s-2); color: var(--accent-text); font-size: var(--fs-15); font-weight: var(--fw-semibold); }
</style>
