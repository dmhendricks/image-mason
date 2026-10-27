// Checks on the user's file before it's sent, and names for the results they download.

import type { Limits } from './api/types'
import { formatBytes, formatMegapixels, formatSize } from './format'
import type { Size } from './geometry'
import { type ImageFormat, imageFormats } from './settings'

const ascii = (bytes: Uint8Array, start: number, length: number) =>
  String.fromCharCode(...bytes.subarray(start, start + length))

/** The format from the file's first bytes, as the server detects it (Imaging/ImageFormat.cs); null for anything else. */
export function sniffFormat(bytes: Uint8Array): ImageFormat | null {
  const startsWith = (signature: number[]) => signature.every((byte, i) => bytes[i] === byte)

  if (startsWith([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
    return 'png'
  if (startsWith([0xFF, 0xD8, 0xFF]))
    return 'jpeg'
  if (bytes.length >= 12 && ascii(bytes, 0, 4) === 'RIFF' && ascii(bytes, 8, 4) === 'WEBP')
    return 'webp'
  if (isAvif(bytes))
    return 'avif'
  // JPEG XL: a bare codestream, or the container's signature box.
  if (startsWith([0xFF, 0x0A]) || startsWith([0, 0, 0, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A]))
    return 'jxl'
  return null
}

function isAvif(bytes: Uint8Array): boolean {
  if (bytes.length < 16 || ascii(bytes, 4, 4) !== 'ftyp')
    return false

  const boxSize = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint32(0)
  const size = Math.min(boxSize, bytes.length)
  // Major brand at 8, minor version at 12, compatible brands from 16.
  for (let offset = 8; offset + 4 <= size; offset += offset === 8 ? 8 : 4) {
    const brand = ascii(bytes, offset, 4)
    if (brand === 'avif' || brand === 'avis')
      return true
  }
  return false
}

/** HEIC and friends: worth naming, since phones save them by default. */
export function isHeif(bytes: Uint8Array): boolean {
  return bytes.length >= 12 && ascii(bytes, 4, 4) === 'ftyp' && /^(heic|heix|hevc|heim|heis|mif1|msf1)$/.test(ascii(bytes, 8, 4))
}

/**
 * Why the server would refuse the file, or null. Checked before the file is read (size known, dimensions null) and
 * again once the browser has measured it; without the server's limits, only an empty file is refused.
 */
export function limitProblem(name: string, bytes: number, dimensions: Size | null,
  limits: Limits | null): string | null {
  if (bytes === 0)
    return `“${name}” is empty.`
  if (limits && bytes > limits.maxInputSize)
    return `“${name}” is ${formatBytes(bytes)}; this server accepts files up to ${formatBytes(limits.maxInputSize)}.`
  if (limits && dimensions && dimensions.width * dimensions.height > limits.maxPixels) {
    const { width, height } = dimensions
    return `“${name}” is ${formatSize(width, height)} (${formatMegapixels(width * height)}); `
      + `this server accepts up to ${formatMegapixels(limits.maxPixels)}.`
  }
  return null
}

export interface Measured extends Size {
  /** Object URL of the file, for downloads and full-size display; the caller revokes it. */
  url: string
  /** A screen-sized copy to display instead, when the image is much larger than any view of it; the caller closes it. */
  preview: ImageBitmap | null
}

/** An image whose size is known but whose pixels aren't decoded yet, so its size can be checked first. */
export interface LoadedImage extends Size {
  /** Object URL of the file; the caller revokes it. */
  url: string
  /** Decodes it: the preview for a large image, else null once the browser has decoded it. Rejects when it can't. */
  decode: () => Promise<ImageBitmap | null>
}

/**
 * The image's size as the browser shows it, read without decoding the pixels; null when the browser can't read it.
 * Browsers apply the EXIF orientation, so these are the display dimensions the server's rect uses.
 */
export async function loadImage(file: Blob): Promise<LoadedImage | null> {
  const url = URL.createObjectURL(file)
  const element = new Image()
  element.src = url
  try {
    await new Promise((resolve, reject) => {
      element.onload = resolve
      element.onerror = reject
    })
  } catch {
    URL.revokeObjectURL(url)
    return null
  }
  const size = { width: element.naturalWidth, height: element.naturalHeight }
  return {
    ...size,
    url,
    // A large image is decoded once, for its preview.
    decode: async () => {
      const preview = await makePreview(file, size)
      if (!preview)
        await element.decode()
      return preview
    },
  }
}

/** Loads and decodes an image: its size, an object URL and, for a large image, a preview; null when it can't. */
export async function measure(file: Blob): Promise<Measured | null> {
  const loaded = await loadImage(file)
  if (!loaded)
    return null
  try {
    return { width: loaded.width, height: loaded.height, url: loaded.url, preview: await loaded.decode() }
  } catch {
    URL.revokeObjectURL(loaded.url)
    return null
  }
}

/**
 * A 43 MP photo shown as an <img> added about 200 MB to the browser, against about 125 MB for its preview, though the
 * board shows it at about a screen's width. Null when the image is small enough, or the browser can't resize while
 * decoding (then the full image is shown).
 */
async function makePreview(file: Blob, size: Size): Promise<ImageBitmap | null> {
  const drawn = previewSize(size, Math.max(screen.width, screen.height) * (window.devicePixelRatio || 1))
  if (!drawn)
    return null
  try {
    return await createImageBitmap(file, {
      imageOrientation: 'from-image',
      resizeWidth: drawn.width,
      resizeHeight: drawn.height,
      resizeQuality: 'high',
    })
  } catch {
    return null
  }
}

/**
 * The size to preview an image at so its longest side is maxSide (a screen's longest side in device pixels), or null
 * when it's within a quarter of that already, since the saving would be small.
 */
export function previewSize(size: Size, maxSide: number): Size | null {
  const longest = Math.max(size.width, size.height)
  if (!(maxSide > 0) || longest <= maxSide * 1.25)
    return null
  const scale = maxSide / longest
  return { width: Math.max(1, Math.round(size.width * scale)), height: Math.max(1, Math.round(size.height * scale)) }
}

/** Each format's media type and file extensions; results are saved with the first extension. */
const fileTypes: Record<ImageFormat, { type: string, extensions: string[] }> = {
  jpeg: { type: 'image/jpeg', extensions: ['.jpg', '.jpeg'] },
  png: { type: 'image/png', extensions: ['.png'] },
  webp: { type: 'image/webp', extensions: ['.webp'] },
  avif: { type: 'image/avif', extensions: ['.avif'] },
  jxl: { type: 'image/jxl', extensions: ['.jxl'] },
}

/** The file picker's accept attribute: the formats' media types, then their extensions. */
export const accept = [
  ...imageFormats.map(format => fileTypes[format].type),
  ...imageFormats.flatMap(format => fileTypes[format].extensions),
].join(',')

const formatsByType: Record<string, ImageFormat> =
  Object.fromEntries(imageFormats.map(format => [fileTypes[format].type, format]))

export function formatFromContentType(contentType: string | null): ImageFormat | null {
  return formatsByType[contentType?.split(';')[0]?.trim().toLowerCase() ?? ''] ?? null
}

/** "IMG 0042.jpeg" → "IMG 0042": the name without its extension, or "image" when nothing is left. */
export function baseName(fileName: string): string {
  const base = fileName.replace(/\.[^./\\]*$/, '').trim()
  return base || 'image'
}

/** "photo.jpg", the result labelled "Low, 800 × 533", as WebP → "photo-low-800x533.webp". */
export function resultFileName(original: string, label: string, format: ImageFormat): string {
  const suffix = label.toLowerCase().replace(/\s*×\s*/g, 'x').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')
  return `${baseName(original)}${suffix ? `-${suffix}` : ''}${fileTypes[format].extensions[0]}`
}

/** Whether pasted text is one web address (http or https), as a copied image link is. */
export function isWebUrl(text: string): boolean {
  if (!text || /\s/.test(text))
    return false
  try {
    const { protocol } = new URL(text)
    return protocol === 'http:' || protocol === 'https:'
  } catch {
    return false
  }
}

/** "https://example.com/a/photo%201.jpg?w=2" → "photo 1.jpg": the file name an address ends in, or a stand-in. */
export function fileNameFromUrl(address: string): string {
  try {
    return decodeURIComponent(new URL(address).pathname.split('/').pop() ?? '') || 'Image from URL'
  } catch {
    return 'Image from URL'
  }
}
