import { describe, expect, it } from 'vitest'
import { describeMethod } from './methods'

describe('describeMethod', () => {
  it.each([
    ['jpegli-d1.15', 'JPEG (jpegli, distance 1.15)'],
    ['jpegtran', 'Lossless JPEG recompression (jpegtran)'],
    ['cwebp-q79', 'WebP (cwebp, quality 79)'],
    ['avifenc-lossless', 'Lossless AVIF (avifenc)'],
    ['pngquant+oxipng', 'PNG with fewer colors (pngquant, then oxipng)'],
    ['original', 'The original file: nothing smaller met the quality floor'],
    ['resize+jpegli-d1.063', 'Resized, then JPEG (jpegli, distance 1.063)'],
    ['crop+resize+oxipng', 'Cropped, resized, then lossless PNG (oxipng)'],
  ])('%s', (method, expected) => {
    expect(describeMethod(method)).toBe(expected)
  })

  it('passes an unknown method through', () => {
    expect(describeMethod('crop+newtool-x3')).toBe('Cropped, then newtool-x3')
  })
})
