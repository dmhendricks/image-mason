import { reactive, watch } from 'vue'
import { defaultSettings, loadSettings, saveSettings } from '../settings'

// The settings the panel edits, shared by the components and saved as they change.

const settings = reactive(loadSettings())
watch(settings, value => saveSettings(value), { deep: true })

export function useSettings() {
  function reset() {
    Object.assign(settings, defaultSettings())
  }

  return { settings, reset }
}
