<script lang="ts">
  import { User } from '@lucide/svelte';
  import { hue, initials } from '../lib/format';

  let { name, size = 40, square = false }: { name: string; size?: number; square?: boolean } = $props();
  const letters = $derived(square ? name.slice(0, 1).toUpperCase() : initials(name));
  const h = $derived(hue(name));
</script>

<span
  class="avatar"
  class:square
  aria-hidden="true"
  style:width="{size}px"
  style:height="{size}px"
  style:font-size="{Math.round(size * (square ? 0.5 : 0.4))}px"
  style:--h={h}
>
  {#if letters}{letters}{:else}<User size={Math.round(size * 0.5)} strokeWidth={1.75} />{/if}
</span>

<style>
  .avatar {
    flex: none; display: grid; place-items: center; border-radius: 50%;
    color: #fff; font-weight: var(--fw-semibold); letter-spacing: .01em;
    background: linear-gradient(180deg, oklch(.74 .1 var(--h)), oklch(.56 .12 var(--h)));
  }
  .avatar.square { border-radius: 24%; }
</style>
