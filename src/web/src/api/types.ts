// Response shapes of the Image Mason API (see Endpoints/Responses.cs).

/** GET /api/v1/limits */
export interface Limits {
  formats: FormatLimits[]
  maxInputSize: number
  maxPixels: number
  /** Each preset's minimum SSIMULACRA2 score; null for lossless. */
  presets: Record<string, number | null>
  defaultMaxWidth: number
  imageTtlMinutes: number
  timeoutSeconds: number
  urlFetch: boolean
  apiKeyRequired: boolean
}

export interface FormatLimits {
  name: string
  mimeType: string
  extension: string
  /** Largest width or height the format can store; absent when only the pixel limit applies. */
  maxSide?: number
}

/** The X-Image-Mason-* headers on an optimize, resize, crop or convert response. */
export interface ResultStats {
  quality: string
  originalSize: number
  size: number
  originalWidth: number
  originalHeight: number
  width: number
  height: number
  method: string
  durationMs: number
  /** SSIMULACRA2 score against the original (cropped and resized like the result); null when lossless. */
  score: number | null
  /** The floor the score had to meet; null when lossless. */
  minScore: number | null
}

/** Reads the stats from a response's headers. Missing numbers come back as 0 or null rather than failing. */
export function readStats(header: (name: string) => string | null): ResultStats {
  const text = (name: string) => header(`X-Image-Mason-${name}`) ?? ''
  const number = (name: string) => {
    const value = Number.parseFloat(text(name))
    return Number.isFinite(value) ? value : null
  }
  const dimensions = (name: string) => {
    const [width = 0, height = 0] = text(name).split('x').map(part => Number.parseInt(part, 10) || 0)
    return { width, height }
  }

  const original = dimensions('Original-Dimensions')
  const result = dimensions('Dimensions')
  return {
    quality: text('Quality'),
    originalSize: number('Original-Size') ?? 0,
    size: number('Optimized-Size') ?? 0,
    originalWidth: original.width,
    originalHeight: original.height,
    width: result.width,
    height: result.height,
    method: text('Method'),
    durationMs: number('Duration-Ms') ?? 0,
    score: number('Score'),
    minScore: number('Min-Score'),
  }
}

/** GET /health */
export interface Health {
  status: string
  uptime: string
  timestamp: string
  build: {
    version: string
    date: string
    dotnet: string
    arch: string
  }
}
