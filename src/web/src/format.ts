// Display formatting. Sizes use 1024-based units, as the server's IMAGE_MASON_*_SIZE settings do.

import type { Limits } from './api/types'
import { type ImageFormat, imageFormats } from './settings'

const units = ['bytes', 'KB', 'MB', 'GB']

/** 15000 → "14.6 KB", 161713 → "158 KB", 26214400 → "25 MB": one decimal below 100, none from 100 up. */
export function formatBytes(bytes: number): string {
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  if (unit === 0)
    return `${bytes} ${bytes === 1 ? 'byte' : 'bytes'}`

  const rounded = value >= 100 || Number.isInteger(value) ? Math.round(value).toString() : value.toFixed(1)
  return `${rounded} ${units[unit]}`
}

/** 50000000 → "50 megapixels", 2500000 → "2.5 megapixels". */
export function formatMegapixels(pixels: number): string {
  const megapixels = pixels / 1_000_000
  return `${Number.isInteger(megapixels) ? megapixels : megapixels.toFixed(1)} megapixels`
}

const formatNames: Record<string, string> = { jpeg: 'JPEG', png: 'PNG', webp: 'WebP', avif: 'AVIF', jxl: 'JPEG XL' }

/** "webp" → "WebP". */
export function formatName(format: string): string {
  return formatNames[format] ?? format.toUpperCase()
}

/** Format names in the order they're listed: alphabetical by the name shown, so the API's order doesn't leak into menus. */
export function sortFormats<T extends string>(formats: readonly T[]): T[] {
  return [...formats].sort((a, b) => formatName(a).localeCompare(formatName(b), 'en', { sensitivity: 'base' }))
}

/**
 * The formats to offer for output: those the server lists that the interface knows (all of them until it answers), in
 * menu order.
 */
export function outputFormats(limits: Limits | null): ImageFormat[] {
  return sortFormats((limits?.formats.map(f => f.name) ?? [...imageFormats])
    .filter((f): f is ImageFormat => (imageFormats as readonly string[]).includes(f)))
}

/** 640, 480 → "640 × 480". */
export function formatSize(width: number, height: number): string {
  return `${width} × ${height}`
}

/** Change from one size to another: 161713 → 109718 is "−32%", a larger result is "+5%". Under 1% shows a decimal. */
export function formatChange(from: number, to: number): string {
  if (from === 0)
    return '0%'
  const percent = (to - from) / from * 100
  const magnitude = Math.abs(percent)
  const shown = magnitude >= 1 || magnitude === 0 ? Math.round(magnitude).toString() : magnitude.toFixed(1)
  if (shown === '0' || shown === '0.0')
    return '0%'
  return `${percent < 0 ? '−' : '+'}${shown}%`
}
