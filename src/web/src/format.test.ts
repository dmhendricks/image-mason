import { describe, expect, it } from 'vitest'
import { formatBytes, formatChange, formatMegapixels, formatName, sortFormats } from './format'

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

describe('formatChange', () => {
  it('shows savings as a minus and growth as a plus', () => {
    expect(formatChange(161713, 109718)).toBe('−32%')
    expect(formatChange(100, 105)).toBe('+5%')
    expect(formatChange(1000, 995)).toBe('−0.5%')
    expect(formatChange(1000, 1000)).toBe('0%')
  })
})

describe('formatName', () => {
  it('spells the formats as people write them', () => {
    expect(['jpeg', 'png', 'webp', 'avif'].map(formatName)).toEqual(['JPEG', 'PNG', 'WebP', 'AVIF'])
  })
})

describe('sortFormats', () => {
  it('orders formats alphabetically by the name shown, whatever order they come in', () => {
    expect(sortFormats(['jpeg', 'png', 'webp', 'avif', 'jxl'])).toEqual(['avif', 'jpeg', 'jxl', 'png', 'webp'])
    expect(sortFormats(['webp', 'jxl', 'jpeg'])).toEqual(['jpeg', 'jxl', 'webp'])
  })

  it('leaves the list it was given alone', () => {
    const formats = ['png', 'avif']
    sortFormats(formats)
    expect(formats).toEqual(['png', 'avif'])
  })
})
