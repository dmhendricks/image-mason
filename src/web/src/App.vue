<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import AppHeader from './components/AppHeader.vue'
import ImageCanvas from './components/canvas/ImageCanvas.vue'
import ToolInspector from './components/inspector/ToolInspector.vue'
import KeyDialog from './components/KeyDialog.vue'
import StatusBar from './components/StatusBar.vue'
import ToolRail from './components/ToolRail.vue'
import { useApiKey } from './composables/useApiKey'
import { useImage } from './composables/useImage'
import { useResults } from './composables/useResults'
import { useServer } from './composables/useServer'
import { accept, isWebUrl } from './files'

const { limits } = useServer()
const { open, openUrl } = useImage()
const { hasUnsavedResults } = useResults()
const apiKey = useApiKey()

const fileInput = ref<HTMLInputElement | null>(null)
const keyOpen = ref(false)
const fileOver = ref(false)

// Ask for the key once the server says it needs one, and again if it rejects the saved one.
watch(() => limits.value?.apiKeyRequired, required => {
  if (required && !apiKey.key.value)
    keyOpen.value = true
})
watch(apiKey.rejected, rejected => {
  if (rejected)
    keyOpen.value = true
})

function chooseFile() {
  fileInput.value?.click()
}

// Asked only when there are results not yet downloaded, once the new image is ready to replace them.
const confirmDiscard = () => window.confirm('Open another image? Results you haven\'t downloaded will be lost.')

const openFile = (file: Blob, name: string) => open(file, name, confirmDiscard)
const openLink = (address: string) => openUrl(address, confirmDiscard)

function onFileChosen(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (file)
    void openFile(file, file.name)
}

// Drop an image anywhere on the page, or paste one or its web address. Not while the key dialog is open: a drop is
// refused (still cancelled, or the browser would open the file in place of the page) and a paste is left to its field.
let dragDepth = 0
const carriesFiles = (event: DragEvent) => event.dataTransfer?.types.includes('Files') ?? false

function onDragEnter(event: DragEvent) {
  if (!carriesFiles(event) || keyOpen.value)
    return
  dragDepth++
  fileOver.value = true
}

function onDragLeave(event: DragEvent) {
  if (carriesFiles(event) && --dragDepth <= 0) {
    dragDepth = 0
    fileOver.value = false
  }
}

function onDragOver(event: DragEvent) {
  if (!carriesFiles(event))
    return
  event.preventDefault()
  if (keyOpen.value && event.dataTransfer)
    event.dataTransfer.dropEffect = 'none'
}

function onDrop(event: DragEvent) {
  if (!carriesFiles(event))
    return
  event.preventDefault()
  dragDepth = 0
  fileOver.value = false
  if (keyOpen.value)
    return
  const file = event.dataTransfer?.files[0]
  if (file)
    void openFile(file, file.name)
}

function onPaste(event: ClipboardEvent) {
  if (keyOpen.value)
    return
  const file = event.clipboardData?.files[0]
  if (file) {
    event.preventDefault()
    void openFile(file, file.name || 'Pasted image')
    return
  }

  // A pasted image address, unless it's going into a field.
  const target = event.target instanceof HTMLElement ? event.target : null
  if (target?.isContentEditable || target?.closest('input, textarea, select'))
    return
  const text = event.clipboardData?.getData('text/plain').trim() ?? ''
  if (!isWebUrl(text))
    return
  event.preventDefault()
  void openLink(text)
}

// Leaving the page loses the results; the browser asks first.
function onBeforeUnload(event: BeforeUnloadEvent) {
  if (hasUnsavedResults.value)
    event.preventDefault()
}

onMounted(() => {
  window.addEventListener('dragenter', onDragEnter)
  window.addEventListener('dragleave', onDragLeave)
  window.addEventListener('dragover', onDragOver)
  window.addEventListener('drop', onDrop)
  window.addEventListener('paste', onPaste)
  window.addEventListener('beforeunload', onBeforeUnload)
})

onBeforeUnmount(() => {
  window.removeEventListener('dragenter', onDragEnter)
  window.removeEventListener('dragleave', onDragLeave)
  window.removeEventListener('dragover', onDragOver)
  window.removeEventListener('drop', onDrop)
  window.removeEventListener('paste', onPaste)
  window.removeEventListener('beforeunload', onBeforeUnload)
})
</script>

<template>
  <div class="app">
    <AppHeader class="top" @open="chooseFile" @manage-key="keyOpen = true" />
    <ToolRail class="rail" />
    <ImageCanvas class="canvas" :file-over="fileOver" @open="chooseFile" />
    <ToolInspector class="inspector" @enter-key="keyOpen = true" />
    <StatusBar class="status" />

    <input
      ref="fileInput"
      type="file"
      :accept="accept"
      hidden
      @change="onFileChosen"
    >
    <KeyDialog :open="keyOpen" @close="keyOpen = false" />
  </div>
</template>

<style scoped lang="scss">
// Mockup B ("Survey"): tool rail, canvas and inspector between a top bar and a status bar.
.app {
  height: 100dvh;
  min-height: 560px;
  display: grid;
  grid-template-columns: 84px minmax(0, 1fr) 320px;
  grid-template-rows: 48px minmax(0, 1fr) 30px;
  grid-template-areas:
    "top top top"
    "rail canvas inspector"
    "status status status";
}

.top { grid-area: top; }
.rail { grid-area: rail; }
.canvas { grid-area: canvas; }
.inspector { grid-area: inspector; }
.status { grid-area: status; }

@media (max-width: 900px) {
  .app {
    height: auto;
    min-height: 100dvh;
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: 48px auto auto auto auto;
    grid-template-areas: "top" "rail" "canvas" "inspector" "status";
  }
}
</style>
