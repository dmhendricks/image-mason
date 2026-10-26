import { describe, expect, it } from 'vitest'
import { baseName, formatFromContentType, isHeif, resultFileName, sniffFormat } from './files'

const bytes = (...parts: (number[] | string)[]) =>
  new Uint8Array(parts.flatMap(part => typeof part === 'string' ? [...part].map(c => c.charCodeAt(0)) : part))

/** An ftyp box: size, "ftyp", major brand, minor version, compatible brands. */
const ftyp = (major: string, ...compatible: string[]) =>
  bytes([0, 0, 0, 16 + compatible.length * 4], 'ftyp', major, [0, 0, 0, 0], ...compatible)

describe('sniffFormat', () => {
  it('recognizes the four formats', () => {
    expect(sniffFormat(bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))).toBe('png')
    expect(sniffFormat(bytes([0xFF, 0xD8, 0xFF, 0xE0]))).toBe('jpeg')
    expect(sniffFormat(bytes('RIFF', [0, 0, 0, 0], 'WEBPVP8 '))).toBe('webp')
    expect(sniffFormat(ftyp('avif', 'mif1'))).toBe('avif')
  })

  it('finds AVIF among the compatible brands', () => {
    expect(sniffFormat(ftyp('mif1', 'miaf', 'avif'))).toBe('avif')
  })

  it('rejects anything else', () => {
    expect(sniffFormat(bytes('GIF89a'))).toBeNull()
    expect(sniffFormat(ftyp('heic', 'mif1'))).toBeNull()
    expect(sniffFormat(bytes(''))).toBeNull()
  })
})

describe('isHeif', () => {
  it('names HEIC so the message can', () => {
    expect(isHeif(ftyp('heic', 'mif1'))).toBe(true)
    expect(isHeif(bytes([0xFF, 0xD8, 0xFF, 0xE0]))).toBe(false)
  })
})

describe('formatFromContentType', () => {
  it('maps the image types and ignores parameters', () => {
    expect(formatFromContentType('image/webp')).toBe('webp')
    expect(formatFromContentType('image/JPEG; charset=binary')).toBe('jpeg')
    expect(formatFromContentType('text/html')).toBeNull()
    expect(formatFromContentType(null)).toBeNull()
  })
})

describe('resultFileName', () => {
  it('adds the result\'s label and the output extension', () => {
    expect(resultFileName('photo.jpg', 'High', 'jpeg')).toBe('photo-high.jpg')
    expect(resultFileName('IMG 0042.jpeg', 'Low, 800 × 533', 'webp')).toBe('IMG 0042-low-800x533.webp')
    expect(resultFileName('Pasted image', 'Score 82.5, AVIF', 'avif')).toBe('Pasted image-score-82-5-avif.avif')
  })

  it('names a file with nothing but an extension "image"', () => {
    expect(baseName('.png')).toBe('image')
  })
})
