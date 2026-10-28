import { describe, expect, it } from 'vitest'
import {
  type Box,
  centeredRect,
  dragRect,
  fitScale,
  insetRect,
  outputDrawing,
  percentWithin,
  rectFits,
  rectWithin,
  resizeOutput,
  roundHalfEven,
  scaledRect,
} from './geometry'

describe('resizeOutput', () => {
  // The same cases as ResizeRequestTests.OutputSize_MatchesVipsthumbnail: a 640x427 photo.
  it.each<[number | null, number | null, Box['fit'], boolean, number, number]>([
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

describe('rectWithin', () => {
  const outer = { left: 100, top: 50, width: 200, height: 100 }

  it('accepts areas inside, edges included, and rejects any that reach out', () => {
    expect(rectWithin(outer, outer)).toBe(true)
    expect(rectWithin({ left: 150, top: 60, width: 50, height: 50 }, outer)).toBe(true)
    expect(rectWithin({ left: 99, top: 60, width: 50, height: 50 }, outer)).toBe(false)
    expect(rectWithin({ left: 150, top: 60, width: 151, height: 50 }, outer)).toBe(false)
  })
})

describe('dragRect', () => {
  const image = { left: 0, top: 0, width: 640, height: 480 }
  const start = { left: 100, top: 100, width: 200, height: 100 }

  it('stops at the bounds it is given, such as the part of the image in view', () => {
    const view = { left: 50, top: 80, width: 300, height: 150 }
    expect(dragRect(start, 'move', -500, 500, view)).toEqual({ left: 50, top: 130, width: 200, height: 100 })
    expect(dragRect(start, 'se', 500, 500, view)).toEqual({ left: 100, top: 100, width: 250, height: 130 })
  })

  it('moves the area without resizing it, stopping at the image edges', () => {
    expect(dragRect(start, 'move', 10.4, -20.6, image)).toEqual({ left: 110, top: 79, width: 200, height: 100 })
    expect(dragRect(start, 'move', 1000, -1000, image)).toEqual({ left: 440, top: 0, width: 200, height: 100 })
  })

  it('moves only the dragged edges', () => {
    expect(dragRect(start, 'e', 50, 30, image)).toEqual({ left: 100, top: 100, width: 250, height: 100 })
    expect(dragRect(start, 'n', 30, -40, image)).toEqual({ left: 100, top: 60, width: 200, height: 140 })
    expect(dragRect(start, 'nw', -20, -10, image)).toEqual({ left: 80, top: 90, width: 220, height: 110 })
    expect(dragRect(start, 'se', 20, 10, image)).toEqual({ left: 100, top: 100, width: 220, height: 110 })
  })

  it('keeps a shape on corner drags, the opposite corner staying put', () => {
    const square = { left: 100, top: 100, width: 200, height: 200 }
    expect(dragRect(square, 'se', 50, 10, image, 1)).toEqual({ left: 100, top: 100, width: 210, height: 210 })
    expect(dragRect(square, 'nw', -40, -10, image, 1)).toEqual({ left: 90, top: 90, width: 210, height: 210 })
    expect(dragRect(square, 'se', 500, 500, image, 4 / 3)).toEqual({ left: 100, top: 100, width: 507, height: 380 })
    expect(dragRect(square, 'e', 50, 0, image, 1)).toEqual({ left: 100, top: 100, width: 250, height: 200 })
  })

  it('keeps edges inside the image and the area at least 1 pixel', () => {
    expect(dragRect(start, 'sw', -500, 500, image)).toEqual({ left: 0, top: 100, width: 300, height: 380 })
    expect(dragRect(start, 'w', 500, 0, image)).toEqual({ left: 299, top: 100, width: 1, height: 100 })
    expect(dragRect(start, 'ne', -500, 500, image)).toEqual({ left: 100, top: 199, width: 1, height: 1 })
  })
})

describe('insetRect', () => {
  it('starts a tenth of the shorter side inside every edge', () => {
    expect(insetRect({ width: 640, height: 427 })).toEqual({ left: 43, top: 43, width: 554, height: 341 })
  })
})

describe('scaledRect', () => {
  const area = { left: 64, top: 48, width: 320, height: 240 }

  it('keeps the same pixels on an image of the same size', () => {
    expect(scaledRect(area, { width: 640, height: 480 }, { width: 640, height: 480 })).toEqual(area)
  })

  it('keeps the area in proportion on another size', () => {
    expect(scaledRect(area, { width: 640, height: 480 }, { width: 3072, height: 4080 }))
      .toEqual({ left: 307, top: 408, width: 1536, height: 2040 })
  })

  it('gives nothing for an area that wasn\'t inside the image', () => {
    expect(scaledRect({ ...area, width: NaN }, { width: 640, height: 480 }, { width: 100, height: 100 })).toBeNull()
    expect(scaledRect({ ...area, left: 400 }, { width: 640, height: 480 }, { width: 100, height: 100 })).toBeNull()
  })
})

describe('fitScale', () => {
  it('shrinks to fit the tighter side, and never enlarges', () => {
    expect(fitScale({ width: 1280, height: 427 }, { width: 640, height: 400 })).toBe(0.5)
    expect(fitScale({ width: 640, height: 800 }, { width: 640, height: 400 })).toBe(0.5)
    expect(fitScale({ width: 320, height: 200 }, { width: 640, height: 400 })).toBe(1)
  })
})

describe('percentWithin', () => {
  const view = { left: 100, top: 50, width: 200, height: 100 }

  it('places an area inside the view', () => {
    expect(percentWithin({ left: 150, top: 75, width: 100, height: 25 }, view)).toEqual({ left: 25, top: 25, width: 50, height: 25 })
    expect(percentWithin(view, view)).toEqual({ left: 0, top: 0, width: 100, height: 100 })
  })

  it('places the whole image behind the view, past its edges', () => {
    expect(percentWithin({ left: 0, top: 0, width: 400, height: 300 }, view)).toEqual({ left: -50, top: -50, width: 200, height: 300 })
  })
})

describe('outputDrawing', () => {
  const image = { width: 640, height: 427 }
  const rounded = (size: { width: number, height: number }) => ({ width: +size.width.toFixed(2), height: +size.height.toFixed(2) })

  it('draws a smaller output to scale', () => {
    expect(rounded(outputDrawing({ width: 320, height: 214 }, image))).toEqual({ width: 50, height: 50.12 })
  })

  it('shrinks an enlarged output to fit the image, keeping its shape', () => {
    expect(rounded(outputDrawing({ width: 1280, height: 854 }, image))).toEqual({ width: 100, height: 100 })
    expect(rounded(outputDrawing({ width: 1000, height: 100 }, image))).toEqual({ width: 100, height: 14.99 })
  })
})
