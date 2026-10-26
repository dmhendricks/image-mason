// Checks on the user's file before it's sent, and names for the results they download.

import type { ImageFormat } from './settings'

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

export const extensions: Record<ImageFormat, string> = { jpeg: '.jpg', png: '.png', webp: '.webp', avif: '.avif', jxl: '.jxl' }

const formatsByType: Record<string, ImageFormat> = {
  'image/jpeg': 'jpeg',
  'image/png': 'png',
  'image/webp': 'webp',
  'image/avif': 'avif',
  'image/jxl': 'jxl',
}

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
  return `${baseName(original)}${suffix ? `-${suffix}` : ''}${extensions[format]}`
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
