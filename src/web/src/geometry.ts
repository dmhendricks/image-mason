// Sizes as the server computes them (Imaging/ResizeRequest.cs and ImageOptimizer.cs), so the interface can draw
// the output to scale and refuse sizes the server would reject.

import type { Fit } from './settings'

export interface Size {
  width: number
  height: number
}

/** An area in display pixels (after the orientation is applied): the rect parameter. */
export interface Rect {
  left: number
  top: number
  width: number
  height: number
}

export interface Box {
  width: number | null
  height: number | null
  fit: Fit
  upscale: boolean
}

/** .NET's Math.Round: halves go to the even neighbor. */
export function roundHalfEven(value: number): number {
  const floor = Math.floor(value)
  const fraction = value - floor
  if (fraction !== 0.5)
    return Math.round(value)
  return floor % 2 === 0 ? floor : floor + 1
}

/** The size vipsthumbnail produces for an image of the given size (ResizeRequest.OutputSize). */
export function resizeOutput(box: Box, image: Size): Size {
  if (box.fit === 'fill')
    return { width: box.width ?? image.width, height: box.height ?? image.height }

  const scaleX = box.width !== null ? box.width / image.width : Infinity
  const scaleY = box.height !== null ? box.height / image.height : Infinity
  let scale = box.fit === 'cover' ? Math.max(scaleX, scaleY) : Math.min(scaleX, scaleY)
  if (!box.upscale || !Number.isFinite(scale))
    scale = Math.min(scale, 1)

  const width = Math.max(1, roundHalfEven(image.width * scale))
  const height = Math.max(1, roundHalfEven(image.height * scale))
  return box.fit === 'cover'
    ? { width: Math.min(box.width ?? width, width), height: Math.min(box.height ?? height, height) }
    : { width, height }
}

export function wholeImage(image: Size): Rect {
  return { left: 0, top: 0, width: image.width, height: image.height }
}

export function rectFits(rect: Rect, image: Size): boolean {
  return rect.left >= 0 && rect.top >= 0 && rect.width >= 1 && rect.height >= 1
    && rect.left + rect.width <= image.width && rect.top + rect.height <= image.height
}

export function rectCovers(rect: Rect, image: Size): boolean {
  return rect.left === 0 && rect.top === 0 && rect.width === image.width && rect.height === image.height
}

/** The largest area of the given shape (width / height), centered in the image. */
export function centeredRect(aspect: number, image: Size): Rect {
  let width = image.width
  let height = Math.round(width / aspect)
  if (height > image.height) {
    height = image.height
    width = Math.min(image.width, Math.round(height * aspect))
  }
  return {
    left: Math.floor((image.width - width) / 2),
    top: Math.floor((image.height - height) / 2),
    width,
    height,
  }
}

/** What a drag on the crop frame moves: the whole area, an edge, or a corner (by compass point). */
export type CropHandle = 'move' | 'n' | 'e' | 's' | 'w' | 'nw' | 'ne' | 'sw' | 'se'

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value))

/** Whether inner lies entirely inside outer, edges included. */
export function rectWithin(inner: Rect, outer: Rect): boolean {
  return inner.left >= outer.left && inner.top >= outer.top
    && inner.left + inner.width <= outer.left + outer.width && inner.top + inner.height <= outer.top + outer.height
}

/**
 * The area after dragging a handle by (dx, dy) image pixels from where it started: whole pixels, inside bounds (the
 * image, or the part of it in view), and at least 1 × 1. A moved area keeps its size; a dragged edge stops one pixel
 * short of the opposite edge.
 */
export function dragRect(start: Rect, handle: CropHandle, dx: number, dy: number, bounds: Rect): Rect {
  const minX = bounds.left
  const minY = bounds.top
  const maxX = bounds.left + bounds.width
  const maxY = bounds.top + bounds.height
  if (handle === 'move') {
    return {
      ...start,
      left: clamp(Math.round(start.left + dx), minX, maxX - start.width),
      top: clamp(Math.round(start.top + dy), minY, maxY - start.height),
    }
  }

  let left = start.left
  let top = start.top
  let right = start.left + start.width
  let bottom = start.top + start.height
  if (handle.endsWith('w'))
    left = clamp(Math.round(left + dx), minX, right - 1)
  if (handle.endsWith('e'))
    right = clamp(Math.round(right + dx), left + 1, maxX)
  if (handle.startsWith('n'))
    top = clamp(Math.round(top + dy), minY, bottom - 1)
  if (handle.startsWith('s'))
    bottom = clamp(Math.round(bottom + dy), top + 1, maxY)
  return { left, top, width: right - left, height: bottom - top }
}

/** "rect" as the API takes it: left,top,width,height. */
export function rectParameter(rect: Rect): string {
  return `${rect.left},${rect.top},${rect.width},${rect.height}`
}

// Drawing on the canvas: areas in image pixels placed over an element that shows part of the image, in percentages so
// they follow the element's size on screen.

/** The scale that fits a size into a space without enlarging it, so the pixels on screen stay honest. */
export function fitScale(size: Size, space: Size): number {
  return Math.min(space.width / size.width, space.height / size.height, 1)
}

/**
 * Where inner lies on an element that shows outer, in percentages of the element: the crop frame over the part of the
 * image in view, or the whole image behind that view (reaching past the element's edges).
 */
export function percentWithin(inner: Rect, outer: Rect): Rect {
  return {
    left: (inner.left - outer.left) / outer.width * 100,
    top: (inner.top - outer.top) / outer.height * 100,
    width: inner.width / outer.width * 100,
    height: inner.height / outer.height * 100,
  }
}

/** Percentages as an absolutely positioned element's style. */
export function percentStyle(area: Rect): Record<'left' | 'top' | 'width' | 'height', string> {
  return { left: `${area.left}%`, top: `${area.top}%`, width: `${area.width}%`, height: `${area.height}%` }
}

/**
 * The output drawn over the image from its top left corner, in percentages of the image. An enlarged output can't be
 * drawn past the image, so the drawing shrinks to fit, keeping its shape (the numbers beside it stay true).
 */
export function outputDrawing(output: Size, image: Size): Size {
  const fit = Math.min(1, image.width / output.width, image.height / output.height)
  return { width: output.width / image.width * fit * 100, height: output.height / image.height * fit * 100 }
}
