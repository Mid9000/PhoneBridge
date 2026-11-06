<script lang="ts">
  import { BellOff, CircleAlert, Mail, MessageCircle, Newspaper, Phone, Users, Bell, X } from '@lucide/svelte';
  import type { Component } from 'svelte';
  import { t } from '../i18n';
  import { stamp } from '../lib/format';
  import { app, send } from '../lib/store.svelte';
  import type { NotificationCategory, PhoneNotification } from '../lib/types';
  import Avatar from '../components/Avatar.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import StatusBanner from '../components/StatusBanner.svelte';
  import Toolbar from '../components/Toolbar.svelte';

  const categoryIcon: Record<NotificationCategory, Component<any>> = {
    message: MessageCircle, social: Users, email: Mail, call: Phone, news: Newspaper, other: Bell,
  };

  const error = $derived(app.sectionErrors.notifications);
  const groups = $derived.by(() => {
    const map = new Map<string, PhoneNotification[]>();
    for (const n of [...app.notifications].sort((a, b) => b.at.localeCompare(a.at))) {
      if (!map.has(n.appId)) map.set(n.appId, []);
      map.get(n.appId)!.push(n);
    }
    return [...map.values()];
  });
</script>

<Toolbar title={t.nav.notifications}>
  {#if app.notifications.length}
    <button class="btn ghost" onclick={() => send({ type: 'clearNotifications' })}>{t.common.clearAll}</button>
  {/if}
</Toolbar>
<StatusBanner />

{#if error}
  <EmptyState icon={CircleAlert} tone="error" title={t.common.errorTitle} body={error}>
    <button class="btn" onclick={() => send({ type: 'retrySection', section: 'notifications' })}>{t.common.retry}</button>
  </EmptyState>
{:else if !groups.length}
  <EmptyState icon={BellOff} title={t.notifications.emptyTitle} body={t.notifications.emptyBody} />
{:else}
  <div class="page">
    <div class="page-inner stack">
      {#each groups as list (list[0]!.appId)}
        {@const first = list[0]!}
        {@const Icon = categoryIcon[first.category]}
        <section class="app-group" aria-label={t.notifications.group(first.appName, list.length)}>
          <header>
            <Avatar name={first.appName} size={20} square />
            <h2>{first.appName}</h2>
            <span class="cat" aria-hidden="true"><Icon size={13} strokeWidth={1.75} /></span>
            <span class="count">{list.length}</span>
          </header>
          {#each list as n (n.id)}
            <article class="card" aria-label="{n.appName} : {n.title}">
              <div class="card-main selectable">
                <div class="card-top">
                  <span class="title">{n.title}</span>
                  <time datetime={n.at}>{stamp(n.at)}</time>
                </div>
                {#if n.subtitle}<div class="subtitle">{n.subtitle}</div>{/if}
                <p class="body">{n.body}</p>
              </div>
              <div class="actions">
                {#if n.positiveAction}
                  <button class="btn small" onclick={() => send({ type: 'notificationAction', id: n.id, action: 'positive' })}>{n.positiveAction}</button>
                {/if}
                <button class="icon-btn dismiss" aria-label="{t.notifications.dismiss} : {n.title}" title={t.notifications.dismiss}
                  onclick={() => send({ type: 'dismissNotification', id: n.id })}>
                  <X size={14} strokeWidth={2} />
                </button>
              </div>
            </article>
          {/each}
        </section>
      {/each}
    </div>
  </div>
{/if}

<style>
  .stack { display: flex; flex-direction: column; gap: var(--s-6); padding-top: var(--s-2); }
  .app-group { display: flex; flex-direction: column; gap: 6px; animation: rise-in var(--dur-slow) var(--ease); }
  header { display: flex; align-items: center; gap: var(--s-2); padding: 0 var(--s-1) 2px; }
  h2 { margin: 0; font-size: var(--fs-13); font-weight: var(--fw-semibold); }
  .cat { color: var(--text-3); display: grid; }
  .count { margin-left: auto; font-size: var(--fs-12); color: var(--text-2); }
  .card {
    position: relative; display: flex; gap: var(--s-3); padding: 10px var(--s-4);
    border-radius: var(--r-14); background: var(--bg-elevated); box-shadow: var(--shadow-1);
    border: 1px solid var(--separator);
    transition: box-shadow var(--dur) var(--ease), transform var(--dur) var(--ease);
  }
  .card:hover { box-shadow: var(--shadow-2); }
  .card-main { flex: 1; min-width: 0; }
  .card-top { display: flex; align-items: baseline; gap: var(--s-2); }
  .title { flex: 1; font-weight: var(--fw-semibold); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  time { font-size: var(--fs-12); color: var(--text-2); }
  .subtitle { font-size: var(--fs-12); font-weight: var(--fw-medium); color: var(--text-2); }
  .body { margin: 2px 0 0; color: var(--text); overflow-wrap: anywhere; }
  .actions {
    position: absolute; top: 8px; right: 10px; display: flex; align-items: center; gap: var(--s-1);
    padding-left: var(--s-3); background: linear-gradient(90deg, transparent, var(--bg-elevated) 12px);
    opacity: 0; transition: opacity var(--dur-fast) var(--ease);
  }
  .card:hover .actions, .card:focus-within .actions { opacity: 1; }
  .small { height: 24px; font-size: var(--fs-12); }
  .dismiss { width: 24px; height: 24px; border-radius: 50%; background: var(--fill-1); }
  .dismiss:focus-visible { border-radius: 50%; }
  @media (hover: none) { .actions { opacity: 1; } }
</style>
