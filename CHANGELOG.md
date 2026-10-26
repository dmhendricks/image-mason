# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- A web interface at `/`. Open, drop or paste an image, then optimize, resize, crop or convert it with the
  API's options, compare the result with the original, and download it. Settings are checked against the
  server's limits before anything is sent. The interface asks for the API key on servers that need one,
  retries while the server is busy, and can cancel a running job. It's a Vue 3 app built into the image.
- `IMAGE_MASON_UI_ENABLED`: `false` turns the web interface off, and `/` redirects to `/docs`.

### Changed

- The usage examples that were at `/` moved to `/docs`.

## [0.2.1] - 2026-10-25

### Added

- Images are published for `linux/arm64` as well as `linux/amd64`, built and smoke tested on native runners.

## [0.2.0] - 2026-10-25

### Added

- `optimize` accepts an optional `format` (`jpeg`, `png`, `webp` or `avif`) to convert while optimizing; it
  overrides `preferJpeg`.
- `optimize` accepts `resize`'s `width`, `height`, `fit`, `crop` and `upscale`, as an alternative to `maxWidth`.
- `POST /api/v1/crop`: keeps an area given as `rect=left,top,width,height` in display pixels, then optimizes it
  like `optimize`, optionally resizing or converting the cropped area. `optimize`, `convert` and `resize`
  accept `rect` too.

### Changed

- `GET /api/v1/limits` no longer requires the API key, so clients can read `apiKeyRequired` before asking for
  one.

## [0.1.0] - 2026-10-25

### Added

- `POST /api/v1/optimize`: optimizes a JPEG, PNG, WebP or AVIF sent as the request body or fetched from
  `url`, with `lossless`, `high`, `medium` and `low` quality presets held to an SSIMULACRA2 score, optional
  `minScore`, `maxWidth` resizing and PNG-to-JPEG conversion for photos.
- `POST /api/v1/convert`: converts between JPEG, PNG, WebP and AVIF, optimized with the same presets.
- `POST /api/v1/resize`: resizes to a box (`contain`, `cover` with center or smart cropping, or `fill`), optionally
  converting, optimized with the same presets.
- `POST /api/v1/upload`: stores an image unchanged.
- `POST /api/v1/info`: format, dimensions, orientation, bit depth, alpha, ICC profile and animation, read from
  the image's headers, plus an EXIF summary (camera, date taken, exposure, author, GPS location) for JPEG, PNG
  and WebP.
- `GET /api/v1/limits`: the server's formats, size limits, quality presets and timeouts.
- Temporary hosting of results at `/images/{name}`, with a time limit and a total size cap, and
  `DELETE /images/{name}` to remove one early.
- `GET /health` with build information, used by the image's `HEALTHCHECK`.
- Landing page with usage examples.
- Optional API key, and an allow-list of hosts and extensions for fetching by URL.
- Memory admission control that sizes each job before it starts, so large images wait or are rejected
  instead of running the container out of memory.
- Errors returned as RFC 9457 Problem Details.

[Unreleased]: https://github.com/dmhendricks/image-mason/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/dmhendricks/image-mason/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/dmhendricks/image-mason/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/dmhendricks/image-mason/releases/tag/v0.1.0
