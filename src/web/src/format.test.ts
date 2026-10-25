import { describe, expect, it } from 'vitest'
import { formatBytes, formatMegapixels } from './format'

describe('formatBytes', () => {
  it.each([
    [0, '0 bytes'],
    [1, '1 byte'],
    [1023, '1023 bytes'],
    [1024, '1 KB'],
    [15000, '14.6 KB'],
    [161713, '158 KB'],
    [26214400, '25 MB'],
    [10 * 1024 ** 3, '10 GB'],
    [150 * 1024 ** 2, '150 MB'],
  ])('%i → %s', (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected)
  })
})

describe('formatMegapixels', () => {
  it.each([
    [50_000_000, '50 megapixels'],
    [2_500_000, '2.5 megapixels'],
  ])('%i → %s', (pixels, expected) => {
    expect(formatMegapixels(pixels)).toBe(expected)
  })
})
