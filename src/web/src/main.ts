import { createApp } from 'vue'
import '@fontsource/barlow/400.css'
import '@fontsource/barlow/500.css'
import '@fontsource/barlow/600.css'
import '@fontsource/barlow-semi-condensed/500.css'
import '@fontsource/barlow-semi-condensed/600.css'
import '@fontsource/barlow-semi-condensed/700.css'
import './styles/global.scss'
import './styles/controls.scss'
import App from './App.vue'
import { applySavedTheme } from './composables/useTheme'

applySavedTheme()
createApp(App).mount('#app')
