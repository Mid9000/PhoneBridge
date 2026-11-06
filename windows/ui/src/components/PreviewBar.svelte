<script lang="ts">
  // Browser preview only (mock host): switch theme, window style and demo scenario.
  import { t } from '../i18n';
  import { parseScenario } from '../lib/mock-data';
  import { app, send } from '../lib/store.svelte';
  import type { Settings } from '../lib/types';
  import Segmented from './Segmented.svelte';

  const scenario = parseScenario(new URLSearchParams(location.search).get('scenario'));
  const set = (patch: Partial<Settings>) => { Object.assign(app.settings, patch); send({ type: 'setSettings', patch }); };
  function pick(e: Event) {
    const params = new URLSearchParams(location.search);
    params.set('scenario', (e.target as HTMLSelectElement).value);
    params.set('theme', app.settings.theme);
    params.set('window', app.settings.windowStyle);
    location.search = params.toString();
  }
</script>

<div class="bar" role="toolbar" aria-label={t.preview.label}>
  <Segmented label={t.settings.theme} value={app.settings.theme} onchange={(v) => set({ theme: v })}
    options={[{ value: 'system', label: t.settings.themeSystem }, { value: 'light', label: t.settings.themeLight }, { value: 'dark', label: t.settings.themeDark }]} />
  <Segmented label={t.settings.windowStyle} value={app.settings.windowStyle} onchange={(v) => set({ windowStyle: v })}
    options={[{ value: 'mac', label: t.settings.windowMac }, { value: 'windows', label: t.settings.windowWin }]} />
  <label>
    <span class="sr-only">{t.preview.scenario}</span>
    <select value={scenario} onchange={pick}>
      {#each Object.entries(t.preview.scenarios) as [value, label] (value)}
        <option {value}>{label}</option>
      {/each}
    </select>
  </label>
</div>

<style>
  .bar {
    position: fixed; top: 10px; left: 50%; transform: translateX(-50%); z-index: 3;
    display: flex; align-items: center; gap: var(--s-2); padding: 4px; border-radius: 12px;
    background: var(--bg-popover); backdrop-filter: blur(30px) saturate(1.6); box-shadow: var(--shadow-2);
  }
  select { height: 28px; border: 0; border-radius: 8px; padding: 0 6px; background: var(--fill-1); font-size: var(--fs-12); }
</style>
