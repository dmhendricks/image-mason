// The options behind the one transform, kept in localStorage so they survive a reload and carry over to the next image.

/** The lossy presets. Lossless is its own setting, since it allows no difference at all rather than a level of it. */
export const qualities = ['high', 'medium', 'low'] as const
export const imageFormats = ['jpeg', 'png', 'webp', 'avif', 'jxl'] as const
/** Crop's shapes: Free, or an aspect ratio the frame keeps. */
export const shapes = ['free', '1:1', '4:3', '3:4', '16:9'] as const

export type Quality = typeof qualities[number]
export type ImageFormat = typeof imageFormats[number]
export type Shape = typeof shapes[number]

export interface Settings {
  /** Send quality=lossless; the preset and custom score are kept for when it's turned off. */
  lossless: boolean
  /** null uses the server's default (IMAGE_MASON_DEFAULT_QUALITY) until a preset is chosen. */
  quality: Quality | null
  /** Send minScore instead of the preset's floor ("Custom" in the quality switch). */
  customScore: boolean
  minScore: number
  /** Output format; null keeps the input's. */
  format: ImageFormat | null
  /** The Resize step is on. */
  resize: boolean
  /**
   * The typed size; null follows the crop area or the image. While locked, only the side typed last is kept and the
   * other follows the shape; unlocked, both are kept and the image is stretched to them.
   */
  width: number | null
  height: number | null
  /** Resize keeps the shape (the chain button). */
  lock: boolean
  /** The Crop step is on. The area itself belongs to the image (useImage's rect). */
  crop: boolean
  shape: Shape
}

/**
 * The format to ask for: null ("Keep") when none is chosen, or when the chosen one is the input's own. The
 * saved choice stays as it is, so it still converts the next image that's in another format.
 */
export function chosenFormat(format: ImageFormat | null, input: ImageFormat | undefined): ImageFormat | null {
  return format === input ? null : format
}

/** The preset in effect: the chosen one, else the server's default, else high (as the server's own default). */
export function effectiveQuality(quality: Quality | null, serverDefault: string | undefined): Quality {
  return quality ?? (qualities.includes(serverDefault as Quality) ? serverDefault as Quality : 'high')
}

/** A shape's width / height; null for Free. */
export function shapeAspect(shape: Shape): number | null {
  if (shape === 'free')
    return null
  const [width, height] = shape.split(':').map(Number) as [number, number]
  return width / height
}

export function defaultSettings(): Settings {
  return {
    lossless: false,
    quality: null,
    customScore: false,
    minScore: 80,
    format: null,
    resize: false,
    width: null,
    height: null,
    lock: true,
    crop: false,
    shape: 'free',
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
  const size = (key: string) => Number.isInteger(saved[key]) && (saved[key] as number) >= 1 ? saved[key] as number : null

  // Earlier versions saved lossless as a quality.
  settings.lossless = saved.quality === 'lossless' || bool('lossless', settings.lossless)
  settings.quality = qualities.includes(saved.quality as Quality) ? saved.quality as Quality : null
  settings.customScore = bool('customScore', settings.customScore)
  if (typeof saved.minScore === 'number' && saved.minScore >= 0 && saved.minScore <= 100)
    settings.minScore = saved.minScore
  settings.format = imageFormats.includes(saved.format as ImageFormat) ? saved.format as ImageFormat : null
  settings.resize = bool('resize', settings.resize)
  // Versions with tools saved Resize's box as width and height too, which meant something else; theirs had no resize.
  if (typeof saved.resize === 'boolean') {
    settings.width = size('width')
    settings.height = size('height')
  }
  settings.lock = bool('lock', settings.lock)
  settings.crop = bool('crop', settings.crop)
  settings.shape = pick(shapes, 'shape', settings.shape)
  return settings
}
