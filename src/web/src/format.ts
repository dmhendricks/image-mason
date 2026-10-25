// Display formatting. Sizes use 1024-based units, as the server's IMAGE_MASON_*_SIZE settings do.

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
