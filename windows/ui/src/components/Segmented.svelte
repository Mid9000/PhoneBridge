<script lang="ts" generics="T extends string">
  let { options, value, label, onchange }: {
    options: { value: T; label: string }[]; value: T; label: string; onchange: (v: T) => void;
  } = $props();

  let root: HTMLDivElement;
  function onkeydown(e: KeyboardEvent) {
    const dir = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
    if (!dir) return;
    e.preventDefault();
    const i = options.findIndex((o) => o.value === value);
    const next = options[(i + dir + options.length) % options.length]!;
    onchange(next.value);
    queueMicrotask(() => root.querySelector<HTMLButtonElement>('[aria-checked="true"]')?.focus());
  }
</script>

<div class="seg" role="radiogroup" aria-label={label} bind:this={root} {onkeydown} tabindex="-1">
  {#each options as o (o.value)}
    <button role="radio" aria-checked={o.value === value} tabindex={o.value === value ? 0 : -1} onclick={() => onchange(o.value)}>
      {o.label}
    </button>
  {/each}
</div>

<style>
  .seg { display: inline-flex; padding: 2px; gap: 2px; border-radius: 8px; background: var(--fill-1); }
  button {
    height: 24px; padding: 0 var(--s-3); border: 0; border-radius: 6px; background: transparent;
    font-size: var(--fs-12); font-weight: var(--fw-medium); color: var(--text-2);
    transition: background var(--dur-fast) var(--ease), color var(--dur-fast) var(--ease);
  }
  button:hover { color: var(--text); }
  button[aria-checked='true'] { background: var(--bg-elevated); color: var(--text); box-shadow: var(--shadow-1); }
</style>
