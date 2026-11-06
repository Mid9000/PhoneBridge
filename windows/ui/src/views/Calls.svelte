<script lang="ts">
  import { CircleAlert, Phone, PhoneIncoming, PhoneMissed, PhoneOff, PhoneOutgoing } from '@lucide/svelte';
  import { t } from '../i18n';
  import { stamp } from '../lib/format';
  import { app, send } from '../lib/store.svelte';
  import Avatar from '../components/Avatar.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import StatusBanner from '../components/StatusBanner.svelte';
  import Toolbar from '../components/Toolbar.svelte';

  const error = $derived(app.sectionErrors.calls);
  const ringing = $derived(app.calls.filter((c) => c.ringing));
  const history = $derived(app.calls.filter((c) => !c.ringing));
</script>

<Toolbar title={t.nav.calls} />
<StatusBanner />

{#if error}
  <EmptyState icon={CircleAlert} tone="error" title={t.common.errorTitle} body={error}>
    <button class="btn" onclick={() => send({ type: 'retrySection', section: 'calls' })}>{t.common.retry}</button>
  </EmptyState>
{:else if !app.calls.length}
  <EmptyState icon={Phone} title={t.calls.emptyTitle} body={t.calls.emptyBody} />
{:else}
  <div class="page">
    <div class="page-inner">
      {#each ringing as c (c.id)}
        <section class="ring" aria-label={t.calls.ringing(c.name)}>
          <span class="pulse"><Avatar name={c.name} size={48} /></span>
          <div class="ring-text">
            <strong role="alert">{t.calls.ringing(c.name)}</strong>
            <span>{c.handle}</span>
            <span class="hint">{t.calls.answerHint}</span>
          </div>
          <div class="ring-actions">
            <button class="round danger" aria-label={t.calls.decline} title={t.calls.decline} onclick={() => send({ type: 'callAction', id: c.id, action: 'decline' })}><PhoneOff size={18} strokeWidth={1.9} /></button>
            <button class="round success" aria-label={t.calls.answer} title={t.calls.answer} onclick={() => send({ type: 'callAction', id: c.id, action: 'answer' })}><Phone size={18} strokeWidth={1.9} /></button>
          </div>
        </section>
      {/each}

      <ul class="group">
        {#each history as c (c.id)}
          <li class="row" class:missed={c.kind === 'missed'}>
            <Avatar name={c.name} size={32} />
            <div class="main">
              <span class="name">{c.name}</span>
              <span class="meta">
                {#if c.kind === 'incoming'}<PhoneIncoming size={12} strokeWidth={2} />{:else if c.kind === 'outgoing'}<PhoneOutgoing size={12} strokeWidth={2} />{:else}<PhoneMissed size={12} strokeWidth={2} />{/if}
                {t.calls.kind[c.kind]}{#if c.durationSec}&nbsp;· {t.calls.duration(c.durationSec)}{/if}
              </span>
            </div>
            <time datetime={c.at}>{stamp(c.at)}</time>
          </li>
        {/each}
      </ul>
    </div>
  </div>
{/if}

<style>
  ul { list-style: none; margin: var(--s-2) 0 0; padding: 0; }
  .row { display: flex; align-items: center; gap: var(--s-3); padding: 10px var(--s-4); }
  .main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .name { font-weight: var(--fw-medium); }
  .missed .name, .missed .meta { color: var(--red-text); }
  .meta { display: flex; align-items: center; gap: 5px; font-size: var(--fs-12); color: var(--text-2); }
  time { font-size: var(--fs-12); color: var(--text-2); }

  .ring {
    display: flex; align-items: center; gap: var(--s-4); padding: var(--s-4) var(--s-5); margin: var(--s-2) 0 var(--s-4);
    border-radius: var(--r-14); background: var(--bg-elevated); box-shadow: var(--shadow-2); border: 1px solid var(--separator);
    animation: pop-in var(--dur-slow) var(--ease);
  }
  .pulse { border-radius: 50%; animation: pulse 1.6s var(--ease) infinite; }
  @keyframes pulse { 0% { box-shadow: 0 0 0 0 rgb(52 199 89 / .45); } 100% { box-shadow: 0 0 0 14px rgb(52 199 89 / 0); } }
  .ring-text { flex: 1; display: flex; flex-direction: column; gap: 1px; }
  .ring-text strong { font-size: var(--fs-15); font-weight: var(--fw-semibold); }
  .ring-text span:not(.hint) { color: var(--text-2); }
  .ring-actions { display: flex; gap: var(--s-3); }
  .round { width: 42px; height: 42px; border: 0; border-radius: 50%; display: grid; place-items: center; color: #fff; transition: transform var(--dur-fast) var(--ease); }
  .round:active { transform: scale(.94); }
  .round:focus-visible { border-radius: 50%; }
  .round.danger { background: var(--red-fill); }
  .round.success { background: var(--green-fill); }
</style>
