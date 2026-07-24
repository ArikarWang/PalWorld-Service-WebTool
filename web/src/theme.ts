import { ref, watch } from 'vue'

export type ThemeMode = 'light' | 'dark'

const THEME_KEY = 'pal.theme'

function readTheme(): ThemeMode {
  try {
    const v = localStorage.getItem(THEME_KEY)
    return v === 'dark' ? 'dark' : 'light'
  } catch {
    return 'light'
  }
}

function applyTheme(mode: ThemeMode) {
  document.documentElement.setAttribute('data-theme', mode)
  try { localStorage.setItem(THEME_KEY, mode) } catch { /* ignore */ }
}

export const theme = ref<ThemeMode>(readTheme())
applyTheme(theme.value)

watch(theme, (mode) => applyTheme(mode))

export function toggleTheme() {
  theme.value = theme.value === 'light' ? 'dark' : 'light'
}

export function setTheme(mode: ThemeMode) {
  theme.value = mode
}
