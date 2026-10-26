<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'
import { useApiKey } from '../composables/useApiKey'

// Asks for the API key on servers that need one. A real form with a password field, so password managers can
// save and fill the key.

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ close: [] }>()

const apiKey = useApiKey()
const dialog = ref<HTMLDialogElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const value = ref('')
const remember = ref(false)
const host = window.location.host

function sync(open: boolean) {
  const element = dialog.value
  if (!element)
    return
  if (open && !element.open) {
    value.value = ''
    remember.value = apiKey.remembered.value
    element.showModal()
    void nextTick(() => input.value?.focus())
  } else if (!open && element.open) {
    element.close()
  }
}

watch(() => props.open, sync)
onMounted(() => sync(props.open))

function save() {
  const key = value.value.trim()
  if (!key)
    return
  apiKey.save(key, remember.value)
  emit('close')
}

function forget() {
  apiKey.forget()
  emit('close')
}
</script>

<template>
  <dialog ref="dialog" class="key-dialog" aria-labelledby="key-title" @close="emit('close')">
    <form @submit.prevent="save">
      <h2 id="key-title">API key</h2>

      <p v-if="apiKey.rejected.value" class="alert">The server rejected the saved key. It may have been changed.</p>
      <p v-else-if="apiKey.key.value" class="lede">
        A key is saved {{ apiKey.remembered.value ? 'on this device' : 'until this tab closes' }}. Enter a new one to replace it.
      </p>
      <p v-else class="lede">This server needs its API key for every image it processes.</p>

      <input type="text" name="username" autocomplete="username" :value="`Image Mason (${host})`" hidden readonly>
      <label class="key-field">
        <span>Key</span>
        <input
          ref="input"
          v-model="value"
          class="field"
          type="password"
          name="password"
          autocomplete="current-password"
          spellcheck="false"
          required
        >
      </label>

      <label class="check">
        <input v-model="remember" type="checkbox">
        <span>
          Remember on this device
          <small>Otherwise it's forgotten when this tab closes. Don't tick this on a shared computer.</small>
        </span>
      </label>

      <p class="hint">
        The key stays in this browser and is only sent to this server. Anyone using this browser profile can read it.
      </p>

      <div class="actions">
        <button v-if="apiKey.key.value" class="button" type="button" @click="forget">Forget key</button>
        <button class="button cancel" type="button" @click="emit('close')">Cancel</button>
        <button class="button primary" type="submit">Save key</button>
      </div>
    </form>
  </dialog>
</template>

<style scoped lang="scss">
.key-dialog {
  width: min(420px, calc(100% - 32px));
  padding: 0;
  border: 1px solid var(--rule);
  border-radius: 8px;
  background: var(--panel);
  color: var(--ink);
  box-shadow: 0 12px 40px rgb(0 0 0 / 0.3);

  &::backdrop { background: rgb(0 0 0 / 0.45); }
}

form {
  display: grid;
  gap: 10px;
  padding: 18px 20px 20px;
}

h2 {
  margin: 0;
  font: 700 20px/1.2 var(--font-cond);
}

.lede, .hint { margin: 0; }
.lede { color: var(--muted); }

.alert {
  margin: 0;
  padding: 8px 10px;
  border-radius: var(--radius);
  background: var(--flag-soft);
}

.key-field {
  display: grid;
  gap: 4px;

  span { color: var(--muted); }
}

.check { margin-top: 0; }

.actions {
  display: flex;
  gap: 8px;
  margin-top: 6px;

  .cancel { margin-left: auto; }
  .button { height: 36px; }
  .primary { padding: 0 16px; }
}
</style>
