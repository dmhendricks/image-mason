import { readonly, ref } from 'vue'

// The API key, for servers that set IMAGE_MASON_API_KEY. "Remember on this device" keeps it in localStorage;
// otherwise sessionStorage keeps it until the tab closes. If storage is blocked, it lives in memory for the visit.

const storageKey = 'image-mason.api-key'

function read(storage: () => Storage): string | null {
  try {
    return storage().getItem(storageKey)
  } catch {
    return null
  }
}

function write(storage: () => Storage, value: string | null): void {
  try {
    if (value === null)
      storage().removeItem(storageKey)
    else
      storage().setItem(storageKey, value)
  } catch {
    // Storage blocked: the key stays in memory only.
  }
}

const local = () => localStorage
const session = () => sessionStorage

const key = ref<string | null>(read(local) ?? read(session))
const remembered = ref(read(local) !== null)
/** Set when the server answered 401 to the saved key, so the prompt can say why it's back. */
const rejected = ref(false)

export function useApiKey() {
  function save(value: string, remember: boolean) {
    write(remember ? local : session, value)
    write(remember ? session : local, null)
    key.value = value
    remembered.value = remember
    rejected.value = false
  }

  function forget() {
    write(local, null)
    write(session, null)
    key.value = null
    remembered.value = false
  }

  /** The server refused the key: forget it and say so. */
  function reject() {
    forget()
    rejected.value = true
  }

  /** Headers for an API request: X-Api-Key when a key is set. */
  function headers(): Record<string, string> {
    return key.value ? { 'X-Api-Key': key.value } : {}
  }

  return {
    key: readonly(key),
    remembered: readonly(remembered),
    rejected: readonly(rejected),
    save,
    forget,
    reject,
    headers,
  }
}
