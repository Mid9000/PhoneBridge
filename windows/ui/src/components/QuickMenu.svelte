<script lang="ts">
  import { Copy, Upload } from '@lucide/svelte';
  import { t } from '../i18n';
  import { ago } from '../lib/format';
  import { app, phoneStatus, send, sendFiles } from '../lib/store.svelte';
  import BatteryGauge from './BatteryGauge.svelte';

  /** standalone: rendered alone in the host's tray popup window (index.html#quick). */
  let { standalone = false }: { standalone?: boolean } = $props();

  let el: HTMLElement;
  let input: HTMLInputElement;
  let over = $state(false);
  const status = $derived(phoneStatus(app.connection));
  const last = $derived(app.clipboard[0]);

  function place(e: ToggleEvent) {
    if (standalone || e.newState !== 'open') return;
    const trigger = document.querySelector('[popovertarget="quick-menu"]')?.getBoundingClientRect();
    if (!trigger) return;
    el.style.left = `${trigger.left}px`;
    el.style.bottom = `${innerHeight - trigger.top + 8}px`;
  }
  const close = () => (standalone ? send({ type: 'openMainWindow' }) : el.hidePopover());

  function drop(e: DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    over = false;
    sendFiles([...(e.dataTransfer?.files ?? [])]);
    if (!standalone) el.hidePopover();
  }
</script>

<div
  class="quick"
  class:standalone
  id="quick-menu"
  popover={standalone ? undefined : 'auto'}
  bind:this={el}
  onbeforetoggle={place}
  role="dialog"
  aria-label={t.quick.label}
>
  <header>
    <div>
      <strong>{app.connection.paired ? app.connection.phoneName : t.status.unpaired}</strong>
      <span class="state"><span class="dot {status}"></span>{t.status[status]}</span>
    </div>
    {#if app.battery.level != null}
      <span class="batt"><span>{app.battery.level} %</span><BatteryGauge level={app.battery.level} charging={app.battery.charging} /></span>
    {/if}
  </header>

  <section>
    <h3>{t.quick.lastClipboard}</h3>
    {#if last}
      <div class="clip">
        <span class="clip-text">{last.kind === 'image' ? t.clipboard.image : last.text}</span>
        <span class="clip-meta">{last.origin === 'phone' ? t.clipboard.fromPhone : t.clipboard.fromPc} · {ago(last.at)}</span>
        <button class="icon-btn" aria-label={t.common.copy} title={t.common.copy} onclick={() => send({ type: 'copyClipboardItem', id: last.id })}><Copy size={14} strokeWidth={1.75} /></button>
      </div>
    {:else}
      <p class="none">{t.quick.nothing}</p>
    {/if}
  </section>

  <button
    class="drop"
    class:over
    disabled={!app.connection.paired}
    onclick={() => input.click()}
    ondragenter={(e) => { e.preventDefault(); over = true; }}
    ondragover={(e) => { e.preventDefault(); e.stopPropagation(); }}
    ondragleave={() => (over = false)}
    ondrop={drop}
  >
    <Upload size={15} strokeWidth={1.75} />{t.quick.drop}
  </button>
  <input bind:this={input} type="file" multiple hidden onchange={() => { sendFiles([...(input.files ?? [])]); input.value = ''; }} />

  <button class="btn primary open" onclick={close}>{t.quick.open}</button>
</div>

<style>
  .quick {
    width: 300px; padding: var(--s-3); margin: 0; inset: auto; position: fixed;
    border: 0; border-radius: var(--r-14); color: var(--text);
    background: var(--bg-popover); backdrop-filter: blur(30px) saturate(1.6); box-shadow: var(--shadow-pop);
    flex-direction: column; gap: var(--s-3);
  }
  .quick:popover-open { display: flex; animation: pop-in var(--dur) var(--ease); }
  .quick.standalone { display: flex; position: static; width: 100%; height: 100%; border-radius: 0; box-shadow: none; }
  header { display: flex; align-items: center; justify-content: space-between; gap: var(--s-3); padding: 2px var(--s-1) 0; }
  header div { display: flex; flex-direction: column; min-width: 0; }
  strong { font-size: var(--fs-13); font-weight: var(--fw-semibold); }
  .state { display: inline-flex; align-items: center; gap: 6px; font-size: var(--fs-12); color: var(--text-2); }
  .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--gray-5); }
  .dot.connected { background: var(--green); }
  .dot.partial { background: var(--orange); }
  .batt { display: flex; align-items: center; gap: 6px; font-size: var(--fs-12); color: var(--text-2); font-variant-numeric: tabular-nums; }
  section { border-top: 1px solid var(--separator); padding-top: var(--s-3); }
  h3 { margin: 0 var(--s-1) 6px; font-size: 11px; font-weight: var(--fw-semibold); color: var(--text-2); text-transform: uppercase; letter-spacing: .04em; }
  .clip { display: grid; grid-template-columns: 1fr auto; align-items: center; column-gap: var(--s-2); padding: 8px 6px 8px 10px; border-radius: var(--r-10); background: var(--fill-1); }
  .clip-text { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .clip-meta { grid-row: 2; font-size: 11px; color: var(--text-2); }
  .clip .icon-btn { grid-row: 1 / 3; grid-column: 2; }
  .none { margin: 0 var(--s-1); color: var(--text-2); }
  .drop {
    display: flex; align-items: center; justify-content: center; gap: var(--s-2); height: 56px;
    border: 1.5px dashed var(--gray-4); border-radius: var(--r-10); background: transparent; color: var(--text-2);
    transition: border-color var(--dur-fast) var(--ease), background var(--dur-fast) var(--ease), color var(--dur-fast) var(--ease);
  }
  .drop:hover, .drop.over { border-color: var(--accent); background: var(--accent-soft); color: var(--accent-text); }
  .drop:disabled { opacity: .5; }
  .open { height: 30px; }
</style>
