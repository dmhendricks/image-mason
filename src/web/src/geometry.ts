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

/** "rect" as the API takes it: left,top,width,height. */
export function rectParameter(rect: Rect): string {
  return `${rect.left},${rect.top},${rect.width},${rect.height}`
}
