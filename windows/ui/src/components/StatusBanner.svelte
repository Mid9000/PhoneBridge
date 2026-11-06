<script lang="ts">
  import { WifiOff, BluetoothOff, Link2Off } from '@lucide/svelte';
  import { t } from '../i18n';
  import { ago } from '../lib/format';
  import { app, phoneStatus, send } from '../lib/store.svelte';

  const status = $derived(phoneStatus(app.connection));
</script>

{#if status !== 'connected'}
  <div class="banner {status}" role="status">
    {#if status === 'unpaired'}
      <Link2Off size={15} strokeWidth={1.75} />
      <span>{t.banner.unpaired}</span>
      <button class="btn primary small" onclick={() => send({ type: 'startPairing' })}>{t.banner.pair}</button>
    {:else if status === 'away'}
      <WifiOff size={15} strokeWidth={1.75} />
      <span>{t.banner.away(app.connection.phoneName, ago(app.connection.lastSeenAt))}</span>
    {:else if app.connection.bluetooth !== 'connected'}
      <BluetoothOff size={15} strokeWidth={1.75} />
      <span>{t.banner.partialBluetooth}</span>
    {:else}
      <WifiOff size={15} strokeWidth={1.75} />
      <span>{t.banner.partialWifi}</span>
    {/if}
  </div>
{/if}

<style>
  .banner {
    flex: none; display: flex; align-items: center; gap: var(--s-2);
    margin: 0 var(--s-4) var(--s-2); padding: 7px var(--s-3); border-radius: var(--r-10);
    font-size: var(--fs-12); color: var(--text); background: var(--fill-1);
    animation: rise-in var(--dur) var(--ease);
  }
  .banner :global(svg) { flex: none; color: var(--text-2); }
  .banner.away, .banner.partial { background: light-dark(rgb(255 149 0 / .1), rgb(255 159 10 / .14)); }
  .banner.away :global(svg), .banner.partial :global(svg) { color: var(--orange); }
  span { flex: 1; }
  .small { height: 24px; font-size: var(--fs-12); }
</style>
