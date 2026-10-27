import { describe, expect, it } from 'vitest'
import type { Limits } from './api/types'
import {
  baseName,
  fileNameFromUrl,
  formatFromContentType,
  isHeif,
  isWebUrl,
  limitProblem,
  previewSize,
  resultFileName,
  sniffFormat,
} from './files'

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
    expect(sniffFormat(bytes([0xFF, 0x0A, 0xFA, 0x1F]))).toBe('jxl')
    expect(sniffFormat(bytes([0, 0, 0, 0x0C], 'JXL ', [0x0D, 0x0A, 0x87, 0x0A]))).toBe('jxl')
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

describe('isWebUrl', () => {
  it('accepts one http or https address', () => {
    expect(isWebUrl('https://images.example.com/cdn/2026/287/photo-1052x615.jpg')).toBe(true)
    expect(isWebUrl('http://localhost:8180/images/a.png')).toBe(true)
  })

  it('rejects other schemes, plain text and more than one address', () => {
    expect(isWebUrl('file:///Users/me/photo.jpg')).toBe(false)
    expect(isWebUrl('data:image/png;base64,AAAA')).toBe(false)
    expect(isWebUrl('photo.jpg')).toBe(false)
    expect(isWebUrl('https://a.example/1.jpg https://a.example/2.jpg')).toBe(false)
    expect(isWebUrl('')).toBe(false)
  })
})

describe('fileNameFromUrl', () => {
  it('takes the last part of the path, decoded, without the query', () => {
    expect(fileNameFromUrl('https://example.com/a/photo%201.jpg?w=2')).toBe('photo 1.jpg')
  })

  it('falls back when the path has no file name or is malformed', () => {
    expect(fileNameFromUrl('https://example.com/')).toBe('Image from URL')
    expect(fileNameFromUrl('https://example.com/%E0%A4%A')).toBe('Image from URL')
  })
})

describe('limitProblem', () => {
  const limits = { maxInputSize: 26214400, maxPixels: 50_000_000 } as Limits

  it('refuses an empty file, even before the server\'s limits arrive', () => {
    expect(limitProblem('blank.png', 0, null, null)).toBe('“blank.png” is empty.')
  })

  it('refuses a file over the size limit before it is measured', () => {
    expect(limitProblem('scan.png', 30 * 1024 ** 2, null, limits)).toBe('“scan.png” is 30 MB; this server accepts files up to 25 MB.')
  })

  it('refuses an image over the pixel limit once measured', () => {
    expect(limitProblem('pano.jpg', 1000, { width: 10000, height: 6000 }, limits))
      .toBe('“pano.jpg” is 10000 × 6000 (60 megapixels); this server accepts up to 50 megapixels.')
  })

  it('accepts what fits, edges included', () => {
    expect(limitProblem('photo.jpg', 26214400, { width: 10000, height: 5000 }, limits)).toBeNull()
    expect(limitProblem('photo.jpg', 30 * 1024 ** 2, { width: 10000, height: 6000 }, null)).toBeNull()
  })
})

describe('previewSize', () => {
  it('scales the longest side to the screen\'s', () => {
    expect(previewSize({ width: 8660, height: 5773 }, 2880)).toEqual({ width: 2880, height: 1920 })
    expect(previewSize({ width: 3000, height: 9000 }, 2880)).toEqual({ width: 960, height: 2880 })
  })

  it('skips images within a quarter of the screen, and unknown screens', () => {
    expect(previewSize({ width: 3600, height: 2400 }, 2880)).toBeNull()
    expect(previewSize({ width: 640, height: 480 }, 2880)).toBeNull()
    expect(previewSize({ width: 8000, height: 6000 }, 0)).toBeNull()
  })
})
