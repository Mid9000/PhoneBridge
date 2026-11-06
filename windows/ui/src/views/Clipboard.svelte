<script lang="ts">
  import { CircleAlert, ClipboardList, Copy, ExternalLink, Laptop, Link, Send, Smartphone, Trash2, Type, Image as ImageIcon, ShieldCheck } from '@lucide/svelte';
  import { t } from '../i18n';
  import { ago } from '../lib/format';
  import { app, send } from '../lib/store.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import StatusBanner from '../components/StatusBanner.svelte';
  import Toolbar from '../components/Toolbar.svelte';

  const error = $derived(app.sectionErrors.clipboard);
  const copy = (id: string) => send({ type: 'copyClipboardItem', id });
</script>

<Toolbar title={t.nav.clipboard}>
  {#if app.clipboard.length}
    <button class="btn ghost" onclick={() => send({ type: 'clearClipboard' })}>{t.clipboard.clear}</button>
  {/if}
  <button class="btn primary" disabled={!app.connection.paired} onclick={() => send({ type: 'pushClipboard' })}>
    <Send size={14} strokeWidth={1.9} />{t.clipboard.push}
  </button>
</Toolbar>
<StatusBanner />

{#if error}
  <EmptyState icon={CircleAlert} tone="error" title={t.common.errorTitle} body={error}>
    <button class="btn" onclick={() => send({ type: 'retrySection', section: 'clipboard' })}>{t.common.retry}</button>
  </EmptyState>
{:else if !app.clipboard.length}
  <EmptyState icon={ClipboardList} title={t.clipboard.emptyTitle} body={t.clipboard.emptyBody} />
{:else}
  <div class="page">
    <div class="page-inner">
      <ul class="group">
        {#each app.clipboard as k (k.id)}
          <li class="row">
            <button class="content" onclick={() => copy(k.id)} title={t.clipboard.copyItem} aria-label="{t.clipboard.copyItem} : {k.kind === 'image' ? t.clipboard.image : k.text}">
              <span class="kind">
                {#if k.kind === 'url'}<Link size={15} strokeWidth={1.75} />{:else if k.kind === 'image'}<ImageIcon size={15} strokeWidth={1.75} />{:else}<Type size={15} strokeWidth={1.75} />{/if}
              </span>
              <span class="main">
                {#if k.kind === 'image' && k.imageDataUrl}
                  <img src={k.imageDataUrl} alt={t.clipboard.image} />
                {:else}
                  <span class="text" class:url={k.kind === 'url'}>{k.text}</span>
                {/if}
                <span class="meta">
                  <span class="origin">
                    {#if k.origin === 'phone'}<Smartphone size={11} strokeWidth={2} />{t.clipboard.fromPhone}{:else}<Laptop size={11} strokeWidth={2} />{t.clipboard.fromPc}{/if}
                  </span>
                  <span>{ago(k.at)}</span>
                </span>
              </span>
            </button>
            <div class="actions">
              {#if k.kind === 'url' && k.text}
                <button class="icon-btn" aria-label={t.clipboard.openLink} title={t.clipboard.openLink} onclick={() => send({ type: 'openLink', url: k.text! })}><ExternalLink size={15} strokeWidth={1.75} /></button>
              {/if}
              <button class="icon-btn" aria-label={t.common.copy} title={t.common.copy} onclick={() => copy(k.id)}><Copy size={15} strokeWidth={1.75} /></button>
              <button class="icon-btn" aria-label={t.common.delete} title={t.common.delete} onclick={() => send({ type: 'deleteClipboardItem', id: k.id })}><Trash2 size={15} strokeWidth={1.75} /></button>
            </div>
          </li>
        {/each}
      </ul>
      <p class="note hint"><ShieldCheck size={13} strokeWidth={1.75} />{t.clipboard.note}</p>
    </div>
  </div>
{/if}

<style>
  ul { list-style: none; margin: var(--s-2) 0 0; padding: 0; }
  .row { display: flex; align-items: center; gap: var(--s-2); padding-right: var(--s-3); transition: background var(--dur-fast) var(--ease); }
  .row:hover { background: var(--fill-1); }
  .content { flex: 1; min-width: 0; display: flex; align-items: flex-start; gap: var(--s-3); padding: 12px var(--s-4); border: 0; background: transparent; text-align: left; }
  .content:focus-visible { border-radius: var(--r-10); }
  .kind { width: 28px; height: 28px; flex: none; display: grid; place-items: center; border-radius: var(--r-6); background: var(--fill-1); color: var(--text-2); }
  .main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
  .text { white-space: pre-wrap; overflow-wrap: anywhere; display: -webkit-box; -webkit-line-clamp: 3; line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; }
  .text.url { color: var(--accent-text); }
  img { width: 96px; height: 64px; object-fit: cover; border-radius: var(--r-6); box-shadow: var(--shadow-1); }
  .meta { display: flex; gap: var(--s-2); font-size: var(--fs-12); color: var(--text-2); }
  .origin { display: inline-flex; align-items: center; gap: 4px; padding: 0 7px; border-radius: var(--r-pill); background: var(--fill-1); font-weight: var(--fw-medium); }
  .actions { display: flex; gap: 2px; opacity: 0; transition: opacity var(--dur-fast) var(--ease); }
  .row:hover .actions, .row:focus-within .actions { opacity: 1; }
  .note { display: flex; align-items: center; gap: 6px; margin: var(--s-3) var(--s-1) 0; }
</style>
