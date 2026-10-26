// The server's method names (X-Image-Mason-Method) in plain words: "crop+resize+jpegli-d1.15" is
// "Cropped, resized, then JPEG (jpegli, distance 1.15)".

const encoders: [RegExp, (match: RegExpMatchArray) => string][] = [
  [/^original$/, () => 'The original file: nothing smaller met the quality floor'],
  [/^jpegtran$/, () => 'Lossless JPEG recompression (jpegtran)'],
  [/^jpegli-d([\d.]+)$/, m => `JPEG (jpegli, distance ${m[1]})`],
  [/^cwebp-lossless$/, () => 'Lossless WebP (cwebp)'],
  [/^cwebp-q(\d+)$/, m => `WebP (cwebp, quality ${m[1]})`],
  [/^avifenc-lossless$/, () => 'Lossless AVIF (avifenc)'],
  [/^avifenc-q(\d+)$/, m => `AVIF (avifenc, quality ${m[1]})`],
  [/^pngquant\+oxipng$/, () => 'PNG with fewer colors (pngquant, then oxipng)'],
  [/^oxipng$/, () => 'Lossless PNG (oxipng)'],
]

export function describeMethod(method: string): string {
  const steps: string[] = []
  let rest = method
  for (const [prefix, step] of [['crop+', 'Cropped'], ['resize+', 'resized']] as const) {
    if (rest.startsWith(prefix)) {
      steps.push(step)
      rest = rest.slice(prefix.length)
    }
  }

  let encoder = rest
  for (const [pattern, describe] of encoders) {
    const match = rest.match(pattern)
    if (match) {
      encoder = describe(match)
      break
    }
  }

  if (steps.length === 0)
    return encoder
  const done = steps.join(', ')
  return `${done[0]!.toUpperCase()}${done.slice(1)}, then ${encoder.replace(/^Lossless\b/, 'lossless')}`
}
