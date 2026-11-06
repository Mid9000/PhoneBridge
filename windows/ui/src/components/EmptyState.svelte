<script lang="ts">
  import type { Component, Snippet } from 'svelte';

  let {
    icon: Icon, title, body, tone = 'neutral', children,
  }: { icon: Component<any>; title: string; body?: string; tone?: 'neutral' | 'error' | 'away'; children?: Snippet } = $props();
</script>

<div class="empty" role={tone === 'error' ? 'alert' : undefined}>
  <div class="badge {tone}"><Icon size={26} strokeWidth={1.5} /></div>
  <h2>{title}</h2>
  {#if body}<p>{body}</p>{/if}
  {#if children}<div class="action">{@render children()}</div>{/if}
</div>

<style>
  .empty {
    flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center;
    text-align: center; padding: var(--s-10) var(--s-6); gap: var(--s-1); min-height: 240px;
    animation: rise-in var(--dur-slow) var(--ease);
  }
  .badge {
    width: 56px; height: 56px; border-radius: 50%; display: grid; place-items: center; margin-bottom: var(--s-3);
    background: var(--fill-1); color: var(--text-2);
  }
  .badge.error { background: light-dark(rgb(255 59 48 / .1), rgb(255 69 58 / .16)); color: var(--red-text); }
  .badge.away { background: light-dark(rgb(255 149 0 / .12), rgb(255 159 10 / .16)); color: var(--orange); }
  h2 { margin: 0; font-size: var(--fs-15); font-weight: var(--fw-semibold); }
  p { margin: 0; max-width: 360px; text-wrap: balance; color: var(--text-2); font-size: var(--fs-13); line-height: 1.5; }
  .action { margin-top: var(--s-4); display: flex; gap: var(--s-2); }
</style>
