# Changelog

All notable changes to this project are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Added a web interface. Open, drop or paste an image, then optimize, resize, crop or convert it with the API's options, compare the result with the original, and download it. Settings are checked against the server's limits before anything is sent. The interface asks for the API key on servers that need one, retries while the server is busy, and can cancel a running job. Each image is sent once: the first job stores it with `upload` (for `IMAGE_MASON_IMAGE_TTL_MINUTES`, like any stored image), later jobs name it as `source`, and opening another image deletes it. An image opened by web address is downloaded by the server again rather than uploaded. It's a Vue 3 app built into the image.
- `IMAGE_MASON_UI_ENABLED`: `false` turns the web interface off, and `/` redirects to `/docs`.
- `IMAGE_MASON_DEFAULT_QUALITY`: the preset (`high`, `medium` or `low`) for requests that don't send `quality`, also the web interface's starting choice. `GET /api/v1/limits` reports it as `defaultQuality`.
- `GET /api/v1/limits` reports the memory a job needs (`jobMemory`, and `bytesPerPixel` for each format), so a client can tell whether the server can process an image before sending it. The web interface uses it to disable Run, and says what fits: "with a quality floor, about 19.9–23.8 megapixels" in a 4 GB container. It also preloads the four fonts its first screen uses.
- `source` on `optimize`, `convert`, `resize`, `crop` and `info`: the name of an image stored with `upload`, used instead of a body or `url`, so several jobs on one image send it only once. 404 when it has expired. With `keepOriginal`, `input` is the stored image's URL rather than a second copy.
- JPEG XL, as input and output (`format=jxl`): lossy and lossless encoding with `cjxl`, and lossless recompression of JPEGs (`cjxl-jpeg`), typically 20% smaller with the same pixels. libjxl is now built as a shared library, so its four tools share one copy of the codec; the image grows by about 1 MB.

### Changed

- The usage examples that were at `/` moved to `/docs`.
- **Breaking:** the JSON `reductionPercent` is now `sizeChangePercent`, and the `X-Image-Mason-Reduction-Percent` header is now `X-Image-Mason-Size-Change-Percent`. The sign is flipped: negative when the result is smaller, positive when it's larger.
- The optimize log line says "larger" instead of a negative "smaller".
- **Breaking:** the default `quality` is now `medium` (was `high`), in the API and the web interface. Set `IMAGE_MASON_DEFAULT_QUALITY=high`, or send `quality=high`, for the previous results.
- libjxl 0.12.0 (`cjxl`, `djxl`, `ssimulacra2`; was 0.11.2), with about 30 security and hardening fixes to the decoder. jpegli moved out of libjxl in 0.12, so `cjpegli` is built from [google/jpegli](https://github.com/google/jpegli), pinned to a commit since it has no releases yet.
- oxipng 10.2.1 (was 9.1.5), from the project's static release build, since Alpine still packages 9.1.5.
- `IMAGE_MASON_MAX_CONCURRENT_JOBS` defaults to the container's CPU count (was 2). Scoring and most encoders use one core, so two jobs left half of a 4-CPU container idle; 20 small photos now optimize in 1.1 s instead of about 2 s. Large images still wait for memory.
- `latest`, on Docker Hub and on GitHub's releases, moves only to the highest version, so releasing a fix for an older line no longer takes it over.
- A PNG (or a WebP, AVIF or JPEG XL) whose alpha channel is opaque everywhere is scored without it, since ssimulacra2 scores images with alpha on two backgrounds. Results are byte for byte the same; jobs on such images are 25–35% faster (WebP, AVIF and JPEG XL output) and about 10% faster to PNG.

### Fixed

- `upload`, `info` and `fetch` no longer run without a limit (or, for `upload`, wait behind optimization jobs): they share their own, sized so the images they hold fit in the memory the service keeps back from jobs (5 at once with the default 25 MB input limit). Request and download bodies with a known length are read into one buffer instead of being grown and copied.
- A download that broke off partway (`url`) returned 500; it now gets 502.
- A crafted JPEG could be measured as smaller than the tools decode it, getting past `IMAGE_MASON_MAX_PIXELS` and the memory check: markers that carry no length (TEM, RST) were read as if they had one. JPEG headers are now read the way libjpeg reads them, in one place instead of four. A PNG whose first chunk isn't its header is no longer measured from whatever is there.
- In browsers that can't display JPEG XL, the web interface said a `.jxl` file "may be damaged", and JPEG XL results showed as broken images. It now says the browser can't display JPEG XL, and a result it can't show carries a note to download it.
- The image's health check always used port 8080, so a container that moved the service with `ASPNETCORE_HTTP_PORTS` showed as unhealthy. It now checks the first port listed.
- The image's labels said `image-mason`, an old description and a version with a `v` (`v0.2.1`). They now give the title, description and version from the Dockerfile.
- A cancelled or timed-out job could leave its files in `/tmp` (which is memory in the compose files): the work folder was deleted before the killed tool had exited. The tool is now waited for first. Tool output kept for the log is capped at 64 KB.
- A stored image whose save failed left a partial file behind until cleanup, and a relative `IMAGE_MASON_STORE_PATH` made `GET /images/{name}` return 404. Saves no longer list the whole store each time to check its size.
- `IMAGE_MASON_TIMEOUT_SECONDS` above about 4.3 million made every request fail, and a very large `IMAGE_MASON_IMAGE_TTL_MINUTES` broke cleanup. They're now limited to a day (`86400`) and a year (`525600`), and larger values stop the service at startup with a message.
- A damaged JPEG, PNG or WebP that had to be converted, resized or cropped returned 500. It now gets 422 with a plain message, and the tool's output goes to the log only. Damaged AVIF and JPEG XL images get 422 too (they got 415, which means an unsupported format).
- An EXIF block with an out-of-range offset could make `info`, `upload` and `optimize` fail with 500.
- The check for PNG photos (`preferJpeg`, and `isPhoto` from `info`) ran before the pixel limit and ignored the request timeout. It now skips images over `IMAGE_MASON_MAX_PIXELS`, stops at the timeout, and runs once per request instead of up to twice.
- Requests without the API key waited for a job slot before getting 401, so they could fill the queue and make requests with the key get 503. The key is now checked first.
- The 503 sent when the job queue is full now has `Retry-After`, like the one sent when an image tool runs out of memory.
- The image was missing the license texts of its Alpine packages (musl, busybox, OpenSSL, zlib and others), the third-party notices of the .NET runtime and ASP.NET Core compiled into the binary, and the licenses of three small libraries the aom encoder includes. They're now in `/usr/share/licenses/image-mason/licenses/`, listed in `THIRD_PARTY_NOTICES.md`.

## [0.2.1] - 2026-10-25

### Added

- Images are published for `linux/arm64` as well as `linux/amd64`, built and smoke tested on native runners.

## [0.2.0] - 2026-10-25

### Added

- `optimize` accepts an optional `format` (`jpeg`, `png`, `webp` or `avif`) to convert while optimizing; it overrides `preferJpeg`.
- `optimize` accepts `resize`'s `width`, `height`, `fit`, `crop` and `upscale`, as an alternative to `maxWidth`.
- `POST /api/v1/crop`: keeps an area given as `rect=left,top,width,height` in display pixels, then optimizes it like `optimize`, optionally resizing or converting the cropped area. `optimize`, `convert` and `resize` accept `rect` too.

### Changed

- `GET /api/v1/limits` no longer requires the API key, so clients can read `apiKeyRequired` before asking for one.

## [0.1.0] - 2026-10-25

### Added

- `POST /api/v1/optimize`: optimizes a JPEG, PNG, WebP or AVIF sent as the request body or fetched from `url`, with `lossless`, `high`, `medium` and `low` quality presets held to an SSIMULACRA2 score, optional `minScore`, `maxWidth` resizing and PNG-to-JPEG conversion for photos.
- `POST /api/v1/convert`: converts between JPEG, PNG, WebP and AVIF, optimized with the same presets.
- `POST /api/v1/resize`: resizes to a box (`contain`, `cover` with center or smart cropping, or `fill`), optionally converting, optimized with the same presets.
- `POST /api/v1/upload`: stores an image unchanged.
- `POST /api/v1/info`: format, dimensions, orientation, bit depth, alpha, ICC profile and animation, read from the image's headers, plus an EXIF summary (camera, date taken, exposure, author, GPS location) for JPEG, PNG and WebP.
- `GET /api/v1/limits`: the server's formats, size limits, quality presets and timeouts.
- Temporary hosting of results at `/images/{name}`, with a time limit and a total size cap, and `DELETE /images/{name}` to remove one early.
- `GET /health` with build information, used by the image's `HEALTHCHECK`.
- Landing page with usage examples.
- Optional API key, and an allow-list of hosts and extensions for fetching by URL.
- Memory admission control that sizes each job before it starts, so large images wait or are rejected instead of running the container out of memory.
- Errors returned as RFC 9457 Problem Details.

[Unreleased]: https://github.com/dmhendricks/image-mason/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/dmhendricks/image-mason/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/dmhendricks/image-mason/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/dmhendricks/image-mason/releases/tag/v0.1.0
