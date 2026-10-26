import { reactive, watch } from 'vue'
import { defaultSettings, loadSettings, saveSettings } from '../settings'

// The settings every tool reads, shared by the components and saved as they change.

const settings = reactive(loadSettings())
watch(settings, value => saveSettings(value), { deep: true })

export function useSettings() {
  /** Back to the defaults, staying on the current tool. */
  function reset() {
    Object.assign(settings, { ...defaultSettings(), tool: settings.tool })
  }

  return { settings, reset }
}
