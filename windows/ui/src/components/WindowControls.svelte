<script lang="ts">
  import { Minus, Square, Copy, X } from '@lucide/svelte';
  import { t } from '../i18n';
  import { app, send } from '../lib/store.svelte';
  import type { WindowAction } from '../lib/types';

  let { variant }: { variant: 'mac' | 'windows' } = $props();
  const act = (action: WindowAction) => send({ type: 'window', action });
  const maxLabel = $derived(app.host.maximized ? t.window.restore : t.window.maximize);
</script>

{#if variant === 'mac'}
  <div class="traffic" role="group" aria-label={t.window.controls}>
    <button class="light close" aria-label={t.window.close} title={t.window.close} onclick={() => act('close')}>
      <svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.8 3.8l4.4 4.4M8.2 3.8l-4.4 4.4" /></svg>
    </button>
    <button class="light min" aria-label={t.window.minimize} title={t.window.minimize} onclick={() => act('minimize')}>
      <svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.2 6h5.6" /></svg>
    </button>
    <button class="light max" aria-label={maxLabel} title={maxLabel} onclick={() => act('maximize')}>
      <svg viewBox="0 0 12 12" aria-hidden="true"><path class="fill" d="M3.6 3.6h3.6L3.6 7.2zM8.4 8.4H4.8l3.6-3.6z" /></svg>
    </button>
  </div>
{:else}
  <div class="caption" role="group" aria-label={t.window.controls}>
    <button aria-label={t.window.minimize} title={t.window.minimize} onclick={() => act('minimize')}><Minus size={16} strokeWidth={1.25} /></button>
    <button aria-label={maxLabel} title={maxLabel} onclick={() => act('maximize')}>
      {#if app.host.maximized}<Copy size={13} strokeWidth={1.25} />{:else}<Square size={13} strokeWidth={1.25} />{/if}
    </button>
    <button class="close" aria-label={t.window.close} title={t.window.close} onclick={() => act('close')}><X size={17} strokeWidth={1.25} /></button>
  </div>
{/if}

<style>
  .traffic { display: flex; gap: 8px; padding: 0 2px; }
  .light {
    width: 12px; height: 12px; padding: 0; border: 0; border-radius: 50%;
    display: grid; place-items: center;
    box-shadow: inset 0 0 0 .5px rgb(0 0 0 / .18);
  }
  .light svg { width: 12px; height: 12px; opacity: 0; transition: opacity var(--dur-fast) var(--ease); }
  .light path { stroke: rgb(0 0 0 / .6); stroke-width: 1.3; stroke-linecap: round; fill: none; }
  .light path.fill { fill: rgb(0 0 0 / .55); stroke: none; }
  .traffic:hover svg, .traffic:focus-within svg { opacity: 1; }
  .close { background: var(--traffic-close); }
  .min { background: var(--traffic-min); }
  .max { background: var(--traffic-max); }
  .light:active { filter: brightness(.85); }
  .light:focus-visible { border-radius: 50%; }

  .caption { display: flex; align-self: stretch; margin-right: calc(-1 * var(--s-4)); }
  .caption button {
    width: 46px; height: 100%; min-height: 32px; border: 0; padding: 0; background: transparent; color: var(--text);
    display: grid; place-items: center; transition: background var(--dur-fast) linear;
  }
  .caption button:hover { background: var(--fill-1); }
  .caption button.close:hover { background: #C42B1C; color: #fff; }
  .caption button:focus-visible { border-radius: 0; }
</style>
