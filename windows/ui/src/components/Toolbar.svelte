<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Search } from '@lucide/svelte';
  import { t } from '../i18n';
  import { app, ui } from '../lib/store.svelte';
  import WindowControls from './WindowControls.svelte';

  let { title, subtitle, children }: { title: string; subtitle?: string; children?: Snippet } = $props();
</script>

<header class="toolbar drag" class:win={app.settings.windowStyle === 'windows'}>
  <div class="titles">
    <h1>{title}</h1>
    {#if subtitle}<span class="subtitle">{subtitle}</span>{/if}
  </div>
  <div class="actions">
    {@render children?.()}
    <button class="search-btn" onclick={() => (ui.palette = true)} title={t.common.searchShortcut} aria-label={t.common.searchShortcut}>
      <Search size={14} strokeWidth={1.75} />
      <span>{t.common.search}</span>
      <kbd>Ctrl K</kbd>
    </button>
  </div>
  {#if app.settings.windowStyle === 'windows'}<WindowControls variant="windows" />{/if}
</header>

<style>
  .toolbar {
    flex: none; height: var(--toolbar-h); display: flex; align-items: center; gap: var(--s-3);
    padding: 0 var(--s-4) 0 var(--s-6);
  }
  .toolbar.win { align-items: stretch; }
  .toolbar.win > :not(:global(.caption)) { align-self: center; }
  .titles { display: flex; align-items: baseline; gap: var(--s-2); min-width: 0; flex: 1; }
  h1 { margin: 0; font-size: var(--fs-15); font-weight: var(--fw-semibold); white-space: nowrap; letter-spacing: -.01em; }
  .subtitle { color: var(--text-2); font-size: var(--fs-12); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .actions { display: flex; align-items: center; gap: var(--s-2); }
  .search-btn {
    display: flex; align-items: center; gap: 6px; height: 28px; width: 190px; padding: 0 6px 0 10px;
    border: 0; border-radius: var(--r-pill); background: var(--fill-1); color: var(--text-3); font-size: var(--fs-13);
    transition: background var(--dur-fast) var(--ease);
  }
  .search-btn:hover { background: var(--fill-2); }
  .search-btn span { flex: 1; text-align: left; }
  kbd {
    font: inherit; font-size: 11px; color: var(--text-3); padding: 1px 6px; border-radius: var(--r-pill);
    background: var(--fill-1);
  }
  @media (max-width: 900px) { .search-btn { width: auto; } .search-btn span, kbd { display: none; } }
</style>
