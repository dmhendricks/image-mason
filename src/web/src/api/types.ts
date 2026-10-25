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
