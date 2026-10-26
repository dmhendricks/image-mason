import { readonly, ref } from 'vue'

export type Theme = 'light' | 'dark'

const storageKey = 'image-mason.theme'
const systemDark = () => window.matchMedia?.('(prefers-color-scheme: dark)')

function savedTheme(): Theme | null {
  try {
    const value = localStorage.getItem(storageKey)
    return value === 'light' || value === 'dark' ? value : null
  } catch {
    // Storage blocked (private window, site data off): follow the system.
    return null
  }
}

/** Applies a theme the user chose earlier. Called before the app mounts, so the page doesn't flash. */
export function applySavedTheme(): void {
  const theme = savedTheme()
  if (theme)
    document.documentElement.dataset.theme = theme
}

function effective(): Theme {
  const chosen = document.documentElement.dataset.theme
  if (chosen === 'light' || chosen === 'dark')
    return chosen
  return systemDark()?.matches ? 'dark' : 'light'
}

const theme = ref<Theme>('light')
let started = false

/** The current theme: the user's choice once they toggle it, the system's until then. */
export function useTheme() {
  // Read on first use rather than when the module loads, so a saved choice has been applied (applySavedTheme).
  if (!started) {
    started = true
    theme.value = effective()
    // Without a choice, follow the system as it changes.
    systemDark()?.addEventListener('change', () => { theme.value = effective() })
  }

  function toggle() {
    const next: Theme = theme.value === 'dark' ? 'light' : 'dark'
    document.documentElement.dataset.theme = next
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // Not remembered, but still applied for this visit.
    }
    theme.value = next
  }

  return { theme: readonly(theme), toggle }
}
