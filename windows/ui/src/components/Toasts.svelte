<script lang="ts">
  import { CircleAlert, CircleCheck } from '@lucide/svelte';
  import { app } from '../lib/store.svelte';
</script>

<div class="toasts" role="status" aria-live="polite">
  {#each app.toasts as toast (toast.id)}
    <div class="toast {toast.kind}">
      {#if toast.kind === 'error'}<CircleAlert size={15} strokeWidth={1.9} />{:else}<CircleCheck size={15} strokeWidth={1.9} />{/if}
      <span>{toast.text}</span>
    </div>
  {/each}
</div>

<style>
  .toasts { position: fixed; left: 50%; bottom: var(--s-6); transform: translateX(-50%); display: flex; flex-direction: column; align-items: center; gap: var(--s-2); pointer-events: none; z-index: 10; }
  .toast {
    display: flex; align-items: center; gap: var(--s-2); max-width: 480px; padding: 8px 14px; border-radius: var(--r-pill);
    background: var(--bg-popover); backdrop-filter: blur(30px) saturate(1.6); box-shadow: var(--shadow-pop);
    font-size: var(--fs-13); animation: rise-in var(--dur) var(--ease);
  }
  .toast :global(svg) { flex: none; color: var(--green); }
  .toast.error :global(svg) { color: var(--red); }
</style>
