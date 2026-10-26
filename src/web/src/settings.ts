// The options behind every tool, kept in localStorage so they survive a reload. Each tool is a view of the same
// settings, so switching tools never loses anything.

export const tools = ['optimize', 'resize', 'crop', 'convert'] as const
export const qualities = ['lossless', 'high', 'medium', 'low'] as const
export const imageFormats = ['jpeg', 'png', 'webp', 'avif'] as const
export const fits = ['contain', 'cover', 'fill'] as const
export const focuses = ['center', 'attention', 'entropy'] as const

export type Tool = typeof tools[number]
export type Quality = typeof qualities[number]
export type ImageFormat = typeof imageFormats[number]
export type Fit = typeof fits[number]
export type Focus = typeof focuses[number]

export interface Settings {
  tool: Tool
  quality: Quality
  /** Send minScore instead of the preset's floor. */
  customScore: boolean
  minScore: number
  /** Output format for optimize, resize and crop; null keeps the input's. */
  format: ImageFormat | null
  /** Convert's format, which it always needs. */
  convertTo: ImageFormat
  /** Optimize: turn PNG photos into JPEG (ignored when a format is chosen). */
  preferJpeg: boolean
  /** Optimize, crop and convert; null uses the server's default, 0 turns it off. */
  maxWidth: number | null
  /** Resize's box; either side may be empty for contain. */
  width: number | null
  height: number | null
  fit: Fit
  focus: Focus
  upscale: boolean
}

export function defaultSettings(): Settings {
  return {
    tool: 'optimize',
    quality: 'high',
    customScore: false,
    minScore: 80,
    format: null,
    convertTo: 'webp',
    preferJpeg: false,
    maxWidth: null,
    width: null,
    height: null,
    fit: 'contain',
    focus: 'center',
    upscale: false,
  }
}

const storageKey = 'image-mason.settings'

/** Saved settings, with anything missing or invalid (an older version, a hand edit) back at its default. */
export function loadSettings(): Settings {
  let saved: unknown = null
  try {
    saved = JSON.parse(localStorage.getItem(storageKey) ?? 'null')
  } catch {
    // Storage blocked or not JSON: start from the defaults.
  }
  return sanitize(saved)
}

export function saveSettings(settings: Settings): void {
  try {
    localStorage.setItem(storageKey, JSON.stringify(settings))
  } catch {
    // Not remembered, but still used for this visit.
  }
}

export function sanitize(value: unknown): Settings {
  const settings = defaultSettings()
  if (typeof value !== 'object' || value === null)
    return settings

  const saved = value as Record<string, unknown>
  const pick = <T extends string>(options: readonly T[], key: string, fallback: T): T =>
    options.includes(saved[key] as T) ? saved[key] as T : fallback
  const bool = (key: string, fallback: boolean) => typeof saved[key] === 'boolean' ? saved[key] : fallback
  const size = (key: string) => Number.isInteger(saved[key]) && (saved[key] as number) >= 0 ? saved[key] as number : null

  settings.tool = pick(tools, 'tool', settings.tool)
  settings.quality = pick(qualities, 'quality', settings.quality)
  settings.customScore = bool('customScore', settings.customScore)
  if (typeof saved.minScore === 'number' && saved.minScore >= 0 && saved.minScore <= 100)
    settings.minScore = saved.minScore
  settings.format = imageFormats.includes(saved.format as ImageFormat) ? saved.format as ImageFormat : null
  settings.convertTo = pick(imageFormats, 'convertTo', settings.convertTo)
  settings.preferJpeg = bool('preferJpeg', settings.preferJpeg)
  settings.maxWidth = size('maxWidth')
  settings.width = size('width') || null
  settings.height = size('height') || null
  settings.fit = pick(fits, 'fit', settings.fit)
  settings.focus = pick(focuses, 'focus', settings.focus)
  settings.upscale = bool('upscale', settings.upscale)
  return settings
}
