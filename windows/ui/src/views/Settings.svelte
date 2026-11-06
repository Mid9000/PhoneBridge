<script lang="ts">
  import { TriangleAlert } from '@lucide/svelte';
  import { t } from '../i18n';
  import { app, send } from '../lib/store.svelte';
  import type { Settings } from '../lib/types';
  import Segmented from '../components/Segmented.svelte';
  import Toggle from '../components/Toggle.svelte';
  import Toolbar from '../components/Toolbar.svelte';

  const set = (patch: Partial<Settings>) => {
    Object.assign(app.settings, patch); // optimistic; the host answers with the authoritative `settings`
    send({ type: 'setSettings', patch });
  };
  const s = $derived(app.settings);
</script>

{#snippet toggleRow(key: 'toasts' | 'clipboardAuto' | 'otpAutoCopy' | 'keepHistory' | 'helloLock' | 'shortcutsHttp' | 'bluetooth', label: string, hint?: string)}
  <div class="row">
    <div class="text"><span id="lbl-{key}">{label}</span>{#if hint}<span class="hint">{hint}</span>{/if}</div>
    <Toggle checked={s[key]} {label} onchange={(v) => set({ [key]: v })} />
  </div>
{/snippet}

<Toolbar title={t.nav.settings} />

<div class="page">
  <div class="page-inner narrow">
    <h2 class="group-title">{t.settings.appearance}</h2>
    <div class="group">
      <div class="row">
        <span class="text">{t.settings.windowStyle}</span>
        <Segmented label={t.settings.windowStyle} value={s.windowStyle} onchange={(v) => set({ windowStyle: v })}
          options={[{ value: 'mac', label: t.settings.windowMac }, { value: 'windows', label: t.settings.windowWin }]} />
      </div>
      <div class="row">
        <span class="text">{t.settings.theme}</span>
        <Segmented label={t.settings.theme} value={s.theme} onchange={(v) => set({ theme: v })}
          options={[{ value: 'system', label: t.settings.themeSystem }, { value: 'light', label: t.settings.themeLight }, { value: 'dark', label: t.settings.themeDark }]} />
      </div>
    </div>

    <h2 class="group-title">{t.settings.notifications}</h2>
    <div class="group">{@render toggleRow('toasts', t.settings.toasts, t.settings.toastsHint)}</div>

    <h2 class="group-title">{t.settings.clipboard}</h2>
    <div class="group">
      {@render toggleRow('clipboardAuto', t.settings.clipboardAuto)}
      {@render toggleRow('otpAutoCopy', t.settings.otp)}
    </div>

    <h2 class="group-title">{t.settings.privacy}</h2>
    <div class="group">
      {@render toggleRow('keepHistory', t.settings.keepHistory, t.settings.keepHistoryHint)}
      {@render toggleRow('helloLock', t.settings.hello)}
    </div>

    <h2 class="group-title">{t.settings.bluetooth}</h2>
    <div class="group">
      {@render toggleRow('bluetooth', t.settings.bluetoothToggle, t.settings.bluetoothHint)}
      {#if s.bluetooth && app.connection.bluetoothIssue}
        <div class="row warn" role="status"><TriangleAlert size={14} strokeWidth={1.9} /><span class="hint">{t.phone.issue[app.connection.bluetoothIssue]}</span></div>
      {/if}
    </div>

    <h2 class="group-title">{t.settings.network}</h2>
    <div class="group">
      {@render toggleRow('shortcutsHttp', t.settings.shortcutsHttp)}
      <div class="row warn"><TriangleAlert size={14} strokeWidth={1.9} /><span class="hint">{t.settings.shortcutsHttpHint}</span></div>
      <div class="row">
        <div class="text"><span>{t.settings.downloads}</span><span class="hint path selectable">{s.downloadDir}</span></div>
        <button class="btn" onclick={() => send({ type: 'pickDownloadDir' })}>{t.settings.change}</button>
      </div>
    </div>

    <h2 class="group-title">{t.settings.about}</h2>
    <div class="group">
      <div class="row"><div class="text"><span>{t.settings.version(app.host.version)}</span><span class="hint">{t.settings.localOnly}</span></div></div>
    </div>
  </div>
</div>

<style>
  .narrow { max-width: 620px; padding-top: var(--s-2); }
  .row { display: flex; align-items: center; justify-content: space-between; gap: var(--s-4); min-height: 44px; padding: 8px var(--s-4); }
  .text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .path { overflow-wrap: anywhere; }
  .warn { justify-content: flex-start; align-items: flex-start; gap: var(--s-2); color: var(--orange); min-height: 0; border-top: 0 !important; padding-top: 0; }
  .warn :global(svg) { flex: none; margin-top: 1px; }
</style>
