import { describe, expect, it } from 'vitest'
import { type Box, centeredRect, rectFits, resizeOutput, roundHalfEven } from './geometry'
import type { Fit } from './settings'

describe('resizeOutput', () => {
  // The same cases as ResizeRequestTests.OutputSize_MatchesVipsthumbnail: a 640x427 photo.
  it.each<[number | null, number | null, Fit, boolean, number, number]>([
    [320, 320, 'contain', false, 320, 214],
    [320, null, 'contain', false, 320, 214],
    [null, 200, 'contain', false, 300, 200],
    [1000, 1000, 'contain', false, 640, 427],
    [1000, 1000, 'contain', true, 1000, 667],
    [300, 300, 'cover', false, 300, 300],
    [1000, 300, 'cover', false, 640, 300],
    [500, 500, 'cover', false, 500, 427],
    [1000, 300, 'cover', true, 1000, 300],
    [300, 300, 'fill', false, 300, 300],
    [1000, 100, 'fill', false, 1000, 100],
  ])('%s x %s %s (upscale %s) is %s x %s', (width, height, fit, upscale, expectedWidth, expectedHeight) => {
    const box: Box = { width, height, fit, upscale }

    expect(resizeOutput(box, { width: 640, height: 427 })).toEqual({ width: expectedWidth, height: expectedHeight })
  })
})

describe('roundHalfEven', () => {
  it('rounds halves to the even neighbor, like .NET', () => {
    expect([0.5, 1.5, 2.5, 2.4, 2.6, 213.5].map(roundHalfEven)).toEqual([0, 2, 2, 2, 3, 214])
  })
})

describe('rectFits', () => {
  const image = { width: 640, height: 480 }

  it('accepts areas inside the image, edges included', () => {
    expect(rectFits({ left: 0, top: 0, width: 640, height: 480 }, image)).toBe(true)
    expect(rectFits({ left: 160, top: 0, width: 480, height: 480 }, image)).toBe(true)
  })

  it('rejects areas past an edge or empty', () => {
    expect(rectFits({ left: 161, top: 0, width: 480, height: 480 }, image)).toBe(false)
    expect(rectFits({ left: 0, top: 0, width: 0, height: 480 }, image)).toBe(false)
    expect(rectFits({ left: -1, top: 0, width: 10, height: 10 }, image)).toBe(false)
  })
})

describe('centeredRect', () => {
  it('fits the largest area of the shape, centered', () => {
    expect(centeredRect(1, { width: 640, height: 480 })).toEqual({ left: 80, top: 0, width: 480, height: 480 })
    expect(centeredRect(16 / 9, { width: 640, height: 480 })).toEqual({ left: 0, top: 60, width: 640, height: 360 })
    expect(centeredRect(3 / 4, { width: 480, height: 640 })).toEqual({ left: 0, top: 0, width: 480, height: 640 })
  })
})
