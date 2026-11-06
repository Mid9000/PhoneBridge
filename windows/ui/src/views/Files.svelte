<script lang="ts">
  import { ArrowDownLeft, ArrowUpRight, CircleAlert, File, FileArchive, FileImage, FileText, FileVideo, FolderOpen, RotateCcw, Upload, X } from '@lucide/svelte';
  import type { Component } from 'svelte';
  import { t } from '../i18n';
  import { ago, bytes } from '../lib/format';
  import { app, send, sendFiles } from '../lib/store.svelte';
  import type { Transfer } from '../lib/types';
  import EmptyState from '../components/EmptyState.svelte';
  import StatusBanner from '../components/StatusBanner.svelte';
  import Toolbar from '../components/Toolbar.svelte';

  let input: HTMLInputElement;
  const error = $derived(app.sectionErrors.files);

  function iconFor(name: string): Component<any> {
    const ext = name.split('.').pop()?.toLowerCase() ?? '';
    if (['jpg', 'jpeg', 'png', 'heic', 'gif', 'webp'].includes(ext)) return FileImage;
    if (['mov', 'mp4', 'm4v', 'avi', 'mkv'].includes(ext)) return FileVideo;
    if (['zip', '7z', 'rar'].includes(ext)) return FileArchive;
    if (['pdf', 'txt', 'doc', 'docx', 'md'].includes(ext)) return FileText;
    return File;
  }
  const pct = (x: Transfer) => (x.size ? Math.round((x.transferred / x.size) * 100) : 0);
</script>

<Toolbar title={t.nav.files}>
  <button class="btn ghost" onclick={() => send({ type: 'openDownloads' })}><FolderOpen size={15} strokeWidth={1.75} />{t.files.openFolder}</button>
</Toolbar>
<StatusBanner />

<div class="page">
  <div class="page-inner">
    <button class="dropzone" disabled={!app.connection.paired} onclick={() => input.click()} aria-describedby="drop-hint">
      <span class="drop-icon"><Upload size={22} strokeWidth={1.6} /></span>
      <span class="drop-title">{t.files.drop}</span>
      <span class="drop-hint" id="drop-hint">{app.connection.paired ? t.files.dropHint : t.files.disabled}</span>
      <span class="btn">{t.files.choose}</span>
    </button>
    <input bind:this={input} type="file" multiple hidden onchange={() => { sendFiles([...(input.files ?? [])]); input.value = ''; }} />

    <h2 class="group-title">{t.files.recent}</h2>
    {#if error}
      <EmptyState icon={CircleAlert} tone="error" title={t.common.errorTitle} body={error}>
        <button class="btn" onclick={() => send({ type: 'retrySection', section: 'files' })}>{t.common.retry}</button>
      </EmptyState>
    {:else if !app.transfers.length}
      <p class="empty hint">{t.files.emptyBody}</p>
    {:else}
      <ul class="group" aria-label={t.files.recent}>
        {#each app.transfers as x (x.id)}
          {@const Icon = iconFor(x.name)}
          <li class="row">
            <span class="file-icon"><Icon size={18} strokeWidth={1.5} /></span>
            <div class="main">
              <div class="top">
                <span class="name selectable">{x.name}</span>
                <span class="state {x.status}">
                  {#if x.status === 'active'}{pct(x)} %{:else}{t.files.status[x.status]}{/if}
                </span>
              </div>
              {#if x.status === 'active' || x.status === 'queued'}
                <div class="bar" role="progressbar" aria-label={x.name} aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct(x)}>
                  <span style:width="{pct(x)}%"></span>
                </div>
              {/if}
              <div class="meta">
                {#if x.direction === 'toPc'}<ArrowDownLeft size={12} strokeWidth={2} />{:else}<ArrowUpRight size={12} strokeWidth={2} />{/if}
                <span>{x.direction === 'toPc' ? t.files.toPc : t.files.toPhone}</span>
                <span aria-hidden="true">·</span>
                <span>{x.status === 'active' ? `${bytes(x.transferred)} / ${bytes(x.size)}` : bytes(x.size)}</span>
                <span aria-hidden="true">·</span>
                <span>{ago(x.at)}</span>
                {#if x.error}<span class="err">· {x.error}</span>{/if}
              </div>
            </div>
            <div class="actions">
              {#if x.status === 'active' || x.status === 'queued'}
                <button class="icon-btn" aria-label="{t.common.cancel} : {x.name}" title={t.common.cancel} onclick={() => send({ type: 'cancelTransfer', id: x.id })}><X size={15} strokeWidth={1.75} /></button>
              {:else if x.status === 'failed' || x.status === 'cancelled'}
                <button class="icon-btn" aria-label="{t.common.retry} : {x.name}" title={t.common.retry} onclick={() => send({ type: 'retryTransfer', id: x.id })}><RotateCcw size={15} strokeWidth={1.75} /></button>
              {:else if x.direction === 'toPc'}
                <button class="icon-btn" aria-label="{t.files.reveal} : {x.name}" title={t.files.reveal} onclick={() => send({ type: 'revealTransfer', id: x.id })}><FolderOpen size={15} strokeWidth={1.75} /></button>
              {/if}
            </div>
          </li>
        {/each}
      </ul>
    {/if}
  </div>
</div>

<style>
  .dropzone {
    width: 100%; display: flex; flex-direction: column; align-items: center; gap: 4px;
    padding: var(--s-8) var(--s-6); margin-top: var(--s-2);
    border: 1.5px dashed var(--gray-4); border-radius: var(--r-14); background: var(--bg-group);
    transition: border-color var(--dur) var(--ease), background var(--dur) var(--ease);
  }
  .dropzone:hover { border-color: var(--accent); background: var(--accent-soft); }
  .dropzone:disabled { opacity: .55; }
  .dropzone:focus-visible { border-radius: var(--r-14); }
  .drop-icon { width: 44px; height: 44px; border-radius: 50%; display: grid; place-items: center; background: var(--accent-soft); color: var(--accent); margin-bottom: var(--s-2); }
  .drop-title { font-size: var(--fs-15); font-weight: var(--fw-semibold); }
  .drop-hint { color: var(--text-2); }
  .dropzone .btn { margin-top: var(--s-3); background: var(--bg-elevated); box-shadow: var(--shadow-1); }

  ul { list-style: none; margin: 0; padding: 0; }
  .row { display: flex; align-items: center; gap: var(--s-3); padding: 10px var(--s-3) 10px var(--s-4); }
  .file-icon { width: 34px; height: 34px; flex: none; border-radius: var(--r-6); display: grid; place-items: center; background: var(--fill-1); color: var(--text-2); }
  .main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
  .top { display: flex; align-items: baseline; gap: var(--s-2); }
  .name { flex: 1; font-weight: var(--fw-medium); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .state { font-size: var(--fs-12); color: var(--text-2); font-variant-numeric: tabular-nums; }
  .state.failed { color: var(--red-text); }
  .state.done { color: var(--text-2); }
  .bar { height: 3px; border-radius: 2px; background: var(--fill-2); overflow: hidden; }
  .bar span { display: block; height: 100%; border-radius: 2px; background: var(--accent); transition: width var(--dur-slow) linear; }
  .meta { display: flex; align-items: center; gap: 5px; font-size: var(--fs-12); color: var(--text-2); white-space: nowrap; overflow: hidden; }
  .meta :global(svg) { flex: none; }
  .err { color: var(--red-text); overflow: hidden; text-overflow: ellipsis; }
  .actions { flex: none; width: 28px; }
  .empty { margin: 0; padding: var(--s-6) var(--s-4); text-align: center; border: 1px solid var(--separator); border-radius: var(--r-10); }
</style>
