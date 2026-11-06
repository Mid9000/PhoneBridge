<script lang="ts">
  import { Bluetooth, Clock, QrCode, Smartphone, Wifi, Zap } from '@lucide/svelte';
  import { t } from '../i18n';
  import { ago, longDate } from '../lib/format';
  import { app, phoneStatus, send } from '../lib/store.svelte';
  import type { LinkState } from '../lib/types';
  import BatteryGauge from '../components/BatteryGauge.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import Toolbar from '../components/Toolbar.svelte';

  const status = $derived(phoneStatus(app.connection));
  const b = $derived(app.battery);
  const tone = (s: LinkState) => (s === 'connected' ? 'ok' : s === 'connecting' ? 'wait' : 'off');
</script>

<Toolbar title={t.nav.phone}>
  {#if app.connection.paired}
    <button class="btn" onclick={() => send({ type: 'startPairing' })}><QrCode size={14} strokeWidth={1.75} />{t.phone.repair}</button>
  {/if}
</Toolbar>

{#if !app.connection.paired}
  <EmptyState icon={Smartphone} title={t.phone.unpairedTitle} body={t.phone.unpairedBody}>
    <button class="btn primary" onclick={() => send({ type: 'startPairing' })}><QrCode size={14} strokeWidth={1.9} />{t.phone.pair}</button>
  </EmptyState>
{:else}
  <div class="page">
    <div class="page-inner grid">
      <section class="card hero" aria-label={t.phone.status}>
        <div class="device-art" aria-hidden="true"><span class="screen"><Smartphone size={26} strokeWidth={1.4} /></span></div>
        <div class="hero-text">
          <h2>{app.connection.phoneName}</h2>
          <span class="pill {status}"><span class="dot"></span>{t.status[status]}</span>
        </div>
        <dl class="rows">
          <div><dt><Wifi size={15} strokeWidth={1.75} />{t.phone.wifi}</dt><dd class={tone(app.connection.wifi)}>{t.link[app.connection.wifi]}</dd></div>
          <div><dt><Bluetooth size={15} strokeWidth={1.75} />{t.phone.bluetooth}</dt><dd class={tone(app.connection.bluetooth)}>{t.link[app.connection.bluetooth]}</dd></div>
          <div><dt><Clock size={15} strokeWidth={1.75} />{t.phone.lastSeen}</dt><dd>{ago(app.connection.lastSeenAt)}</dd></div>
        </dl>
        {#if app.connection.bluetoothIssue}<p class="hint issue" role="status">{t.phone.issue[app.connection.bluetoothIssue]}</p>{/if}
      </section>

      <section class="card battery" aria-label={t.phone.battery}>
        <div class="b-head">
          <h2>{t.phone.battery}</h2>
          {#if b.level != null && b.level <= 20 && !b.charging}<span class="low">{t.phone.low}</span>{/if}
        </div>
        <div class="b-value">
          {#if b.level == null}<span class="unknown">—</span>{:else}<span class="num">{b.level}</span><span class="unit">%</span>{/if}
          {#if b.charging}<span class="charging"><Zap size={14} strokeWidth={2} />{t.phone.charging}</span>{:else if b.level != null}<span class="sub">{t.phone.onBattery}</span>{/if}
        </div>
        <BatteryGauge level={b.level} charging={b.charging} size="lg" />
        <p class="hint">{b.updatedAt ? t.phone.updated(ago(b.updatedAt)) : t.phone.unknown} · {t.phone.batteryHint}</p>
      </section>

      <section class="devices">
        <h2 class="group-title">{t.phone.devices}</h2>
        <ul class="group">
          {#each app.devices as d (d.id)}
            <li class="row">
              <Smartphone size={18} strokeWidth={1.5} />
              <div class="main">
                <span class="name">{d.name}{#if d.current}<span class="tag">{t.phone.current}</span>{/if}</span>
                <span class="hint">{t.phone.pairedOn(longDate(d.pairedAt))}</span>
              </div>
              <button class="btn ghost danger-text" onclick={() => send({ type: 'revokeDevice', id: d.id })}>{t.phone.revoke}</button>
            </li>
          {/each}
        </ul>
      </section>
    </div>
  </div>
{/if}

<style>
  .grid { display: grid; grid-template-columns: 1.25fr 1fr; gap: var(--s-4); padding-top: var(--s-2); }
  .card { border-radius: var(--r-14); background: var(--bg-elevated); border: 1px solid var(--separator); box-shadow: var(--shadow-1); padding: var(--s-5); }
  .card h2 { margin: 0; font-size: var(--fs-17); font-weight: var(--fw-semibold); letter-spacing: -.01em; }
  .hero { display: grid; grid-template-columns: auto 1fr; gap: var(--s-4) var(--s-5); align-items: center; }
  .device-art {
    width: 58px; height: 96px; border-radius: 14px; padding: 4px;
    background: linear-gradient(160deg, var(--gray-3), var(--gray-5)); box-shadow: var(--shadow-2);
  }
  .screen { width: 100%; height: 100%; border-radius: 11px; display: grid; place-items: center; background: var(--bg); color: var(--accent); }
  .hero-text { display: flex; flex-direction: column; align-items: flex-start; gap: var(--s-2); }
  .pill { display: inline-flex; align-items: center; gap: 6px; padding: 2px 10px; border-radius: var(--r-pill); font-size: var(--fs-12); font-weight: var(--fw-medium); background: var(--fill-1); }
  .pill .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--gray-5); }
  .pill.connected .dot { background: var(--green); }
  .pill.partial .dot { background: var(--orange); }
  .rows { grid-column: 1 / -1; margin: 0; display: flex; flex-direction: column; border-top: 1px solid var(--separator); }
  .rows div { display: flex; justify-content: space-between; align-items: center; padding: 9px 0; border-bottom: 1px solid var(--separator); }
  .rows div:last-child { border-bottom: 0; padding-bottom: 0; }
  dt { display: flex; align-items: center; gap: var(--s-2); color: var(--text-2); }
  dd { margin: 0; font-weight: var(--fw-medium); }
  dd.ok { color: var(--text); }
  dd.off { color: var(--text-2); }
  .issue { grid-column: 1 / -1; margin: 0; }

  .battery { display: flex; flex-direction: column; gap: var(--s-3); }
  .b-head { display: flex; align-items: center; justify-content: space-between; }
  .low { font-size: var(--fs-12); font-weight: var(--fw-semibold); color: var(--red-text); }
  .b-value { display: flex; align-items: baseline; gap: 2px; }
  .num { font-size: 40px; font-weight: var(--fw-semibold); letter-spacing: -.03em; line-height: 1; font-variant-numeric: tabular-nums; }
  .unit { font-size: var(--fs-22); font-weight: var(--fw-medium); color: var(--text-2); margin-right: var(--s-3); }
  .unknown { font-size: 40px; color: var(--text-3); line-height: 1; margin-right: var(--s-3); }
  .charging { display: inline-flex; align-items: center; gap: 3px; color: light-dark(#1F8236, var(--green)); font-weight: var(--fw-medium); }
  .sub { color: var(--text-2); }
  .battery .hint { margin: 0; }

  .devices { grid-column: 1 / -1; }
  ul { list-style: none; margin: 0; padding: 0; }
  .row { display: flex; align-items: center; gap: var(--s-3); padding: 10px var(--s-4); color: var(--text-2); }
  .main { flex: 1; display: flex; flex-direction: column; }
  .name { color: var(--text); font-weight: var(--fw-medium); display: flex; align-items: center; gap: var(--s-2); }
  .tag { font-size: 11px; padding: 0 7px; border-radius: var(--r-pill); background: var(--accent-soft); color: var(--accent-text); font-weight: var(--fw-medium); }
  .danger-text { color: var(--red-text); }
  @media (max-width: 860px) { .grid { grid-template-columns: 1fr; } }
</style>
