<script lang="ts">
  let { checked, label, onchange }: { checked: boolean; label: string; onchange: (v: boolean) => void } = $props();
</script>

<button class="switch" role="switch" aria-checked={checked} aria-label={label} onclick={() => onchange(!checked)}>
  <span class="knob"></span>
</button>

<style>
  .switch {
    flex: none; position: relative; width: 38px; height: 22px; padding: 0; border: 0; border-radius: var(--r-pill);
    background: var(--fill-3); transition: background var(--dur) var(--ease);
  }
  .switch[aria-checked='true'] { background: var(--green); }
  .knob {
    position: absolute; top: 2px; left: 2px; width: 18px; height: 18px; border-radius: 50%; background: #fff;
    box-shadow: 0 1px 3px rgb(0 0 0 / .25), 0 0 0 .5px rgb(0 0 0 / .06);
    transition: transform var(--dur) var(--ease);
  }
  .switch[aria-checked='true'] .knob { transform: translateX(16px); }
  .switch:focus-visible { border-radius: var(--r-pill); }
</style>
