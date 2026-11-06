<script lang="ts">
  import { t } from '../i18n';

  let { level, charging, size = 'sm' }: { level: number | null; charging: boolean; size?: 'sm' | 'lg' } = $props();
  const pct = $derived(level == null ? 0 : Math.max(0, Math.min(100, Math.round(level))));
  const tone = $derived(level == null ? 'unknown' : charging ? 'charging' : pct <= 20 ? 'low' : 'normal');
</script>

<span
  class="gauge {size} {tone}"
  role="meter"
  aria-valuemin={0}
  aria-valuemax={100}
  aria-valuenow={level ?? undefined}
  aria-label={level == null ? t.phone.unknown : t.phone.gauge(pct)}
>
  <span class="body"><span class="fill" style:width="{pct}%"></span>
    {#if charging}
      <svg class="bolt" viewBox="0 0 12 16" aria-hidden="true"><path d="M7 1 2 9h3.5L4.5 15 10 7H6.5z" /></svg>
    {/if}
  </span>
  <span class="nub"></span>
</span>

<style>
  .gauge { display: inline-flex; align-items: center; gap: 1.5px; flex: none; }
  .body {
    position: relative; display: block; border-radius: 5px; overflow: hidden;
    box-shadow: inset 0 0 0 1px var(--fill-3); background: var(--fill-1);
  }
  .sm .body { width: 24px; height: 12px; border-radius: 4px; }
  .lg .body { width: 100%; height: 38px; border-radius: 12px; }
  .lg { width: 100%; }
  .fill {
    position: absolute; inset: 2px auto 2px 2px; max-width: calc(100% - 4px); border-radius: 2.5px; background: var(--text);
    transition: width var(--dur-slow) var(--ease), background var(--dur) var(--ease);
  }
  .lg .fill { inset: 3px auto 3px 3px; max-width: calc(100% - 6px); border-radius: 9px; }
  .charging .fill { background: var(--green); }
  .low .fill { background: var(--red); }
  .normal.lg .fill { background: var(--accent); }
  .nub { display: block; width: 2px; height: 5px; border-radius: 0 2px 2px 0; background: var(--fill-3); }
  .lg .nub { width: 4px; height: 14px; }
  .bolt { position: absolute; inset: 0; margin: auto; height: 80%; fill: #fff; stroke: light-dark(rgb(0 0 0 / .35), rgb(0 0 0 / .5)); stroke-width: .8; }
</style>
