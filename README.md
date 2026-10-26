# Image Mason

![Image Mason web interface](https://github.com/dmhendricks/image-mason/raw/main/assets/screenshot.webp)

A self-hosted HTTP API that makes JPEG, PNG, WebP, AVIF and JPEG XL images smaller, converts between those formats, and can host the results temporarily. A web UI is included (shown above) for testing and convenience.

Send an image and get a smaller one back, in the same format or another. Lossy results are held to a perceptual quality score ([SSIMULACRA2](https://github.com/cloudinary/ssimulacra2)), so a quality preset describes how the image looks rather than an encoder setting. The service tries several encoders and settings, and returns the smallest result that meets the score, or the original if nothing beats it.

- **JPEG:** lossless recompression with `jpegtran` (mozjpeg), and lossy encoding with Google's [jpegli](https://github.com/google/jpegli).
- **PNG:** lossless recompression with `oxipng`, and palette quantization with `pngquant`.
- **WebP:** lossy and lossless encoding with `cwebp` (libwebp).
- **AVIF:** lossy and lossless encoding with `avifenc` (libavif, with the aom AV1 encoder).
- **JPEG XL:** lossy and lossless encoding with `cjxl` (libjxl), and lossless recompression of JPEGs.
- **Conversion:** any of the five formats to any other, with the same quality presets.
- **Resizing:** fit into a box, fill it and crop (centered or on the most interesting area), or stretch, with libvips.
- **Cropping:** keep an exact area, then resize or convert it in the same call.
- **Hosting:** results can be stored and served from a URL for a limited time.
- **Web interface:** open an image in the browser, try the options, compare the result with the original and download it.

The image is about 65 MB, runs as a non-root user with a read-only root filesystem, and is published for `linux/amd64` and `linux/arm64`.

## Contents

- [Quick Start](#quick-start)
- [Usage](#usage)
- [Endpoints](#endpoints)
- [Configuration](#configuration)
- [Memory](#memory)
- [Security](#security)
- [Development](#development)
- [License](#license)

## Quick Start

```bash
docker run -d --name image-mason -p 8080:8080 \
  -e IMAGE_MASON_API_KEY=change-me \
  -v image-mason-data:/data \
  --memory 4g \
  hendridm/image-mason
```

Open <http://localhost:8080> to use the web interface (it asks for the key), or <http://localhost:8080/docs> for usage examples. Or optimize an image from the command line:

```bash
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg -o photo.min.jpg \
  http://localhost:8080/api/v1/optimize
```

### Docker Compose

Download [`docker-compose.yml`](https://github.com/dmhendricks/image-mason/blob/main/docker-compose.yml) and [`.env.sample`](https://github.com/dmhendricks/image-mason/blob/main/.env.sample), then:

```bash
cp .env.sample .env    # set IMAGE_MASON_API_KEY
docker compose up -d
```

The sample Compose file runs the container read-only with a 4 GB memory limit and a named volume for stored images.

### Tags

| Tag | Meaning |
|---|---|
| `latest` | The newest release |
| `0.1` | The newest `0.1.x` release |
| `0.1.0` | An exact release |

## Usage

### Optimize an Image

Send the image as the raw request body:

```bash
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg -o photo.min.jpg \
  "http://localhost:8080/api/v1/optimize?quality=medium"
```

Or pass a URL for the service to download:

```bash
curl -sS -X POST -H "X-Api-Key: change-me" -o photo.min.jpg \
  "http://localhost:8080/api/v1/optimize?url=https://example.com/photo.jpg"
```

The response body is the optimized image. Its statistics are in the response headers:

```text
X-Image-Mason-Quality: high
X-Image-Mason-Original-Size: 93694
X-Image-Mason-Optimized-Size: 47521
X-Image-Mason-Size-Change-Percent: -49.28
X-Image-Mason-Original-Dimensions: 640x427
X-Image-Mason-Dimensions: 640x427
X-Image-Mason-Method: jpegli-d1.15
X-Image-Mason-Score: 86.19
X-Image-Mason-Min-Score: 85
X-Image-Mason-Duration-Ms: 209
```

### Convert an Image

`convert` takes the same parameters as `optimize`, plus the output `format`: `jpeg`, `png`, `webp`, `avif` or `jxl`.

```bash
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg -o photo.webp \
  "http://localhost:8080/api/v1/convert?format=webp"
```

The result is optimized the same way: the smallest encoding that meets the quality preset.

### Resize an Image

`resize` fits the image into a box, then optimizes it like `optimize` (or converts it, with `format`):

```bash
# Fit inside 1200x800, keeping the aspect ratio
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg -o photo-1200.jpg \
  "http://localhost:8080/api/v1/resize?width=1200&height=800"

# A 400x400 square thumbnail as WebP, cropped to the most interesting area
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg -o thumb.webp \
  "http://localhost:8080/api/v1/resize?width=400&height=400&fit=cover&crop=attention&format=webp"
```

### Crop an Image

`crop` keeps the area in `rect`, given as `left,top,width,height` in pixels, then optimizes it like `optimize`. Add `width`, `height` or `maxWidth` to resize the cropped area, and `format` to convert it:

```bash
# The 480x480 square starting 80 pixels from the left
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg -o square.jpg \
  "http://localhost:8080/api/v1/crop?rect=80,0,480,480"

# The same square as a 200x200 AVIF
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg -o square.avif \
  "http://localhost:8080/api/v1/crop?rect=80,0,480,480&width=200&format=avif"
```

### Quality Presets

| Preset | Minimum score | Result |
|---|---|---|
| `lossless` | | Identical pixels; only the encoding is optimized (see [JPEG output](#how-each-format-is-handled)) |
| `high` | 85 | No visible difference in place at 1:1 |
| `medium` (default) | 80 | No visible difference side by side at 1:1 |
| `low` | 70 | Barely visible difference side by side |

Scores are SSIMULACRA2, from 0 to 100, where 100 is identical. Use `minScore` for a custom floor, or change the default preset and the preset floors with [environment variables](#configuration).

### Store and Share an Image

Add `output=json` to store the result and get its URL instead of the bytes:

```bash
curl -sS -H "X-Api-Key: change-me" --data-binary @graphic.png \
  "http://localhost:8080/api/v1/optimize?output=json"
```

```json
{
  "output": "http://localhost:8080/images/f100579fc856433e9a5b56c49ee9e420.png",
  "mimeType": "image/png",
  "originalSize": 9810,
  "compressedSize": 894,
  "sizeChangePercent": -90.89,
  "originalWidth": 480,
  "originalHeight": 320,
  "width": 480,
  "height": 320,
  "quality": "medium",
  "method": "pngquant+oxipng",
  "score": 90.31,
  "minScore": 80,
  "isPhoto": false,
  "expiresAt": "2026-10-24T16:32:24-04:00",
  "durationMs": 668
}
```

Stored images are served at `/images/{name}` until `expiresAt` (60 minutes by default), then deleted. To store an image without changing it, use [`POST /api/v1/upload`](#post-apiv1upload).

### Errors

Errors are returned as [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) with the `application/problem+json` content type:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.16",
  "title": "Unsupported Media Type",
  "status": 415,
  "detail": "Unsupported image format; expected JPEG, PNG, WebP, AVIF or JPEG XL.",
  "traceId": "00-2505c8a26cb460e47d2be04e45c74d8c-9d260fe1868f535e-00"
}
```

| Status | Cause |
|---|---|
| 400 | Invalid parameter, empty body, both a body and `url`, or a `url` that isn't allowed |
| 401 | Missing or wrong `X-Api-Key` |
| 413 | Image larger than `IMAGE_MASON_MAX_INPUT_SIZE` |
| 415 | Not a JPEG, PNG, WebP, AVIF or JPEG XL, or an animated image (only `upload` accepts those) |
| 422 | Too many pixels, or more memory needed than the service has |
| 502 | The `url` download failed or redirected too many times |
| 503 | The queue is full, or an image tool ran out of memory (sent with `Retry-After`) |
| 504 | The request took longer than `IMAGE_MASON_TIMEOUT_SECONDS` |

## Endpoints

When `IMAGE_MASON_API_KEY` is set, the `/api` endpoints require it in the `X-Api-Key` header, except `GET /api/v1/limits`.

| Endpoint | Description |
|---|---|
| `POST /api/v1/optimize` | Optimize an image, keeping its format unless `format` is sent |
| `POST /api/v1/convert` | Convert an image to another format, optimized |
| `POST /api/v1/resize` | Resize an image to a box, optimized |
| `POST /api/v1/crop` | Crop an image to an area, optimized |
| `POST /api/v1/upload` | Store an image unchanged |
| `POST /api/v1/info` | Describe an image: format, dimensions, bit depth, alpha and more |
| `GET /api/v1/limits` | The server's formats, size limits, presets and timeouts |
| `GET /images/{name}` | Fetch a stored image |
| `DELETE /images/{name}` | Delete a stored image before it expires |
| `GET /health` | Service status and build information |
| `GET /` | Web interface (turn off with `IMAGE_MASON_UI_ENABLED=false`) |
| `GET /docs` | Usage examples |

### `POST /api/v1/optimize`

The image is the request body, or the file at `url`.

| Parameter | Default | Description |
|---|---|---|
| `quality` | `medium` (`IMAGE_MASON_DEFAULT_QUALITY`) | `lossless`, `high`, `medium` or `low` |
| `minScore` | | Custom minimum SSIMULACRA2 score, 0 to 100. Overrides the preset's floor. |
| `rect` | | Keep only this area: `left,top,width,height` in display pixels (after the orientation is applied), such as `80,0,480,480`. Cropped before any resize, so the sizes below apply to the area. |
| `maxWidth` | `0` | Downscale images wider than this, keeping the aspect ratio. `0` keeps the original size. Can't be combined with `width` or `height`. |
| `width` | | Resize to a box this wide, 1 to 65535, in display pixels (after the orientation is applied) |
| `height` | | Resize to a box this high, 1 to 65535. Send `width`, `height` or both; a missing side follows the aspect ratio. |
| `fit` | `contain` | `contain`: fit inside the box, keeping the aspect ratio (either side may be left out). `cover`: fill the box and crop what overflows. `fill`: stretch to exactly the box. `cover` and `fill` need both sides. |
| `crop` | `center` | For `fit=cover`: which part to keep. `center`, `attention` (the most interesting area: faces, saturated colors and edges) or `entropy` (the most detail). |
| `upscale` | `false` | Enlarge images smaller than the box. Without it, `contain` leaves them as they are and `cover` only crops. `fill` always reaches the exact size. |
| `format` | the input's | `jpeg` (or `jpg`), `png`, `webp`, `avif` or `jxl`, to convert while optimizing. Overrides `preferJpeg`. |
| `preferJpeg` | `false` | Return a JPEG when the input is a PNG photo with no transparency. Ignored when `format` is sent. |
| `url` | | Download the image from this URL instead of reading the body |
| `output` | | `json` stores the result and returns its URL and statistics |
| `outputFilename` | random | Stored file name, such as `banner.jpg`: letters, digits, `_` or `-`, then the output format's extension (`.jpg`, `.png`, `.webp`, `.avif` or `.jxl`). Replaces an existing file with the same name. Requires `output=json`. |
| `keepOriginal` | `false` | Also store the input, returned as `input`. Requires `output=json`. |

Response headers:

| Header | Description |
|---|---|
| `X-Image-Mason-Quality` | The preset used |
| `X-Image-Mason-Original-Size` | Input size in bytes |
| `X-Image-Mason-Optimized-Size` | Output size in bytes |
| `X-Image-Mason-Size-Change-Percent` | Percent change from the input size: negative if smaller, positive if larger |
| `X-Image-Mason-Original-Dimensions` | Input width x height |
| `X-Image-Mason-Dimensions` | Output width x height |
| `X-Image-Mason-Method` | How the result was produced, such as `jpegtran`, `jpegli-d1.15`, `oxipng`, `pngquant+oxipng`, `cwebp-q93`, `cwebp-lossless`, `avifenc-q80`, `cjxl-d1.2`, `cjxl-jpeg` or `original`. Cropped results start with `crop+` and resized ones with `resize+` (`crop+resize+` when both). |
| `X-Image-Mason-Score` | SSIMULACRA2 score of a lossy result |
| `X-Image-Mason-Min-Score` | The floor the result had to meet |
| `X-Image-Mason-Is-Photo` | For PNG inputs: whether it looks like a photo with no transparency |
| `X-Image-Mason-Duration-Ms` | Processing time |

#### How Each Format Is Handled

Lossy encoders search their quality setting for the smallest result that meets the floor, scoring each attempt against the input.

- **JPEG:** `jpegtran` recompresses a JPEG without loss, keeping the ICC color profile (and the EXIF data, when the image relies on its orientation tag). Unless the preset is `lossless`, jpegli encodes it too. JPEG has no lossless mode, so a JPEG produced by cropping, resizing or converting under `lossless` is held to a score of 90 instead. Transparent images are placed on white.
- **PNG:** `oxipng` recompresses without loss. Unless the preset is `lossless`, `pngquant` reduces the palette, and the result is kept only if it meets the floor.
- **WebP:** `cwebp` encodes lossy, and also lossless when the input is a PNG graphic or the preset is `lossless`. Lossy WebP can't reach every floor on detailed photos; lossless is then the fallback.
- **AVIF:** `avifenc` encodes lossy, or lossless under the `lossless` preset.
- **JPEG XL:** `cjxl` encodes lossy, and also lossless when the input is a PNG graphic or the preset is `lossless`. A JPEG at its original size is also recompressed without loss (`cjxl-jpeg`), typically 20% smaller.
- **Orientation:** every re-encoded result is rotated upright, so it displays correctly without an orientation tag. Metadata other than the ICC color profile is removed.
- **Cropped and resized images** start from the cut-out or downscaled pixels, and are scored against them.

When the output format matches the input, the original is always a candidate, so the response is never larger than the input unless a crop or resize asked for a different image. Animated WebP, AVIF and JPEG XL are rejected.

### `POST /api/v1/convert`

The same parameters and response as [`optimize`](#post-apiv1optimize), except that `format` is required: `jpeg` (or `jpg`), `png`, `webp`, `avif` or `jxl`. `preferJpeg` doesn't apply. Converting to JPEG or WebP reduces 16-bit images to 8 bits per channel.

### `POST /api/v1/resize`

The same parameters and response as [`optimize`](#post-apiv1optimize), except that `width`, `height` or both are required, and `maxWidth` and `preferJpeg` don't apply.

The result is optimized against the resized pixels, so its score compares it to the resized image, not the original. Results over `IMAGE_MASON_MAX_PIXELS`, and WebP results over 16383 pixels on a side, get 422.

### `POST /api/v1/crop`

The same parameters and response as [`optimize`](#post-apiv1optimize), except that `rect` is required. The area is in display pixels, so for a photo with an orientation tag it matches what a viewer shows, not the stored pixels. A `rect` that reaches past the image's edges gets 400, with the image's displayed size in the message.

`width`, `height` and `maxWidth` resize the cropped area, and `X-Image-Mason-Original-Dimensions` still reports the whole input. The score compares the result with the cropped area.

### `POST /api/v1/upload`

Stores a JPEG, PNG, WebP, AVIF or JPEG XL unchanged, animated ones included, and returns the same JSON as `optimize?output=json`. Accepts the image as the body or `url`, and `outputFilename`.

### `POST /api/v1/info`

Reads the image's headers without decoding it, so it's fast and doesn't wait for a job slot. Accepts the image as the body or `url`.

```bash
curl -sS -H "X-Api-Key: change-me" --data-binary @photo.jpg http://localhost:8080/api/v1/info
```

```json
{
  "format": "jpeg",
  "mimeType": "image/jpeg",
  "size": 93794,
  "width": 640,
  "height": 427,
  "orientation": 6,
  "displayWidth": 427,
  "displayHeight": 640,
  "megapixels": 0.27,
  "bitDepth": 8,
  "hasAlpha": false,
  "hasIccProfile": false,
  "animated": false,
  "withinPixelLimit": true
}
```

`orientation` is the EXIF orientation (1 is upright), and `displayWidth` and `displayHeight` apply it. `hasAlpha` means the image has an alpha channel, even if every pixel is opaque. PNGs also report `isPhoto`. `withinPixelLimit` says whether `optimize` and `convert` accept the image under `IMAGE_MASON_MAX_PIXELS`.

When a JPEG, PNG or WebP carries EXIF, `exif` summarizes it. Fields the image doesn't have are left out:

```json
"exif": {
  "make": "Canon",
  "model": "Canon EOS R5",
  "lensModel": "RF24-70mm F2.8 L IS USM",
  "software": "Lightroom 13",
  "dateTaken": "2026-05-01T14:30:00+02:00",
  "artist": "Jane Doe",
  "copyright": "(c) 2026 Jane Doe",
  "iso": 800,
  "fNumber": 4,
  "exposureTime": "1/125",
  "focalLength": 35,
  "gps": { "latitude": 48.85837, "longitude": 2.294481, "altitude": 35 }
}
```

`gps` is where the photo was taken. `optimize` and `convert` remove it, along with the rest of the EXIF data.

### `GET /api/v1/limits`

What this server accepts, so a client can check an image before sending it. The values come from the [configuration](#configuration). It needs no API key, so a client can read `apiKeyRequired` before asking for one.

```json
{
  "formats": [
    { "name": "jpeg", "mimeType": "image/jpeg", "extension": ".jpg", "maxSide": 65535 },
    { "name": "png", "mimeType": "image/png", "extension": ".png" },
    { "name": "webp", "mimeType": "image/webp", "extension": ".webp", "maxSide": 16383 },
    { "name": "avif", "mimeType": "image/avif", "extension": ".avif" },
    { "name": "jxl", "mimeType": "image/jxl", "extension": ".jxl" }
  ],
  "maxInputSize": 26214400,
  "maxPixels": 50000000,
  "presets": { "lossless": null, "high": 85, "medium": 80, "low": 70 },
  "defaultQuality": "medium",
  "defaultMaxWidth": 0,
  "imageTtlMinutes": 60,
  "timeoutSeconds": 120,
  "urlFetch": true,
  "apiKeyRequired": true
}
```

`maxSide` is the largest width or height a format can store; without it, only `maxPixels` applies. `presets` gives each preset's minimum score.

### `GET /images/{name}`

Serves a stored image until it expires. These URLs need no API key: anyone who has one can fetch the image.

### `DELETE /images/{name}`

Deletes a stored image before it expires. Unlike fetching, this needs the API key. Returns 204, or 404 when there's no image by that name (including one that already expired). An image stored with `keepOriginal` has two names, `output` and `input`; delete each separately.

```bash
curl -sS -X DELETE -H "X-Api-Key: change-me" http://localhost:8080/images/banner.webp
```

### `GET /health`

Returns the service status, uptime and build information (version, build date, .NET runtime and architecture). The image's `HEALTHCHECK` uses this endpoint.

## Configuration

All settings are environment variables, and all are optional. Sizes are in bytes or use a suffix: `25m`, `10g`. [`.env.sample`](https://github.com/dmhendricks/image-mason/blob/main/.env.sample) lists them with comments. An invalid value stops the container at startup with a message naming the variable.

| Variable | Default | Description |
|---|---|---|
| `IMAGE_MASON_API_KEY` | | Key callers send in `X-Api-Key`. Empty turns authentication off. |
| `IMAGE_MASON_PUBLIC_BASE_URL` | | Origin for stored image URLs, such as `https://images.example.com`, when behind a proxy. Empty uses each request's scheme and host. |
| `IMAGE_MASON_MAX_INPUT_SIZE` | `25m` | Largest accepted image file |
| `IMAGE_MASON_MAX_PIXELS` | `50000000` | Largest accepted image in pixels, checked before decoding |
| `IMAGE_MASON_MAX_CONCURRENT_JOBS` | `2` | Requests processed at once |
| `IMAGE_MASON_MAX_QUEUED_JOBS` | `16` | Requests that wait for a slot before getting 503 |
| `IMAGE_MASON_TIMEOUT_SECONDS` | `120` | Time before a request stops with 504 |
| `IMAGE_MASON_MEMORY_LIMIT` | container limit | Memory the service may use. See [Memory](#memory). |
| `IMAGE_MASON_DEFAULT_MAX_WIDTH` | `0` | `maxWidth` for requests that don't send one. `0` keeps the original size. |
| `IMAGE_MASON_DEFAULT_QUALITY` | `medium` | `quality` for requests that don't send one, and the web interface's starting choice: `high`, `medium` or `low` |
| `IMAGE_MASON_HIGH_MIN_SCORE` | `85` | Floor for `high` |
| `IMAGE_MASON_MEDIUM_MIN_SCORE` | `80` | Floor for `medium` |
| `IMAGE_MASON_LOW_MIN_SCORE` | `70` | Floor for `low` |
| `IMAGE_MASON_IMAGE_TTL_MINUTES` | `60` | How long stored images are served |
| `IMAGE_MASON_MAX_STORE_SIZE` | `10g` | Total stored size; past it, the oldest images are deleted. `0` turns the cap off. |
| `IMAGE_MASON_URL_FETCH_ENABLED` | `true` | `false` accepts uploaded images only |
| `IMAGE_MASON_URL_ALLOWED_HOSTS` | | Comma-separated hosts `url` may point to. `*.example.com` matches subdomains. Empty allows any host. |
| `IMAGE_MASON_URL_ALLOWED_EXTENSIONS` | | Comma-separated extensions the `url` path must end in, such as `png,jpg,jpeg,webp,avif,jxl`. Empty allows any. |
| `IMAGE_MASON_UI_ENABLED` | `true` | `false` turns the web interface off: `/` redirects to the usage examples at `/docs`. The API is unaffected. |
| `IMAGE_MASON_LOG_LEVEL` | `Information` | `Trace`, `Debug`, `Information`, `Warning` or `Error` |
| `TZ` | `UTC` | Timezone for logs and timestamps, such as `America/New_York` |

Stored images are kept in `/data/images`. Mount a volume at `/data` to keep them across container restarts; they still expire after `IMAGE_MASON_IMAGE_TTL_MINUTES`.

## Memory

Scoring a lossy result takes far more memory than encoding it: about **150 MB per megapixel**. A 12 MP phone photo needs about 2 GB at `high`, `medium` or `low`, whatever the output format. Lossless jobs need much less: up to about 16 MB per megapixel for JPEG, 32 MB for WebP and JPEG XL, 40 MB for AVIF and 48 MB for PNG.

Time grows with size the same way. With 4 CPUs, a 15 MP photo at `high` takes about 10 seconds as JPEG and 10 to 20 seconds as AVIF or WebP, which need more encoding attempts. Raise `IMAGE_MASON_TIMEOUT_SECONDS` if you send large images with a short timeout.

The service guards against running out of memory:

- Each job reserves its estimated peak memory before it starts. Jobs wait in order while memory is in use.
- A job that could never fit gets 422 instead of waiting.
- The budget is the container's memory limit minus 256 MB for the service itself. Set `IMAGE_MASON_MEMORY_LIMIT` when there is no container limit, or to share the host with other services.
- If the kernel still kills an image tool, the request gets 503 with `Retry-After`, and the service keeps running.

Set a container memory limit (`--memory` or the Compose `memory` limit) that fits your largest images, and use `maxWidth` or `IMAGE_MASON_DEFAULT_MAX_WIDTH` when you don't need full-resolution output: memory for scoring depends on the output size.

## Security

Image Mason is built for private networks and trusted callers.

- **Set an API key** unless only trusted clients can reach the port. Without one, anyone who can reach the service can use it, and the container logs a warning at startup.
- **Use TLS.** The service speaks plain HTTP. Put it behind a reverse proxy for HTTPS, and set `IMAGE_MASON_PUBLIC_BASE_URL` so stored image URLs use the public address.
- **Restrict URL fetching.** The service fetches any URL a caller sends, including addresses on your private network; it does not block private or internal addresses. Set `IMAGE_MASON_URL_ALLOWED_HOSTS`, or turn fetching off with `IMAGE_MASON_URL_FETCH_ENABLED=false`. The host list is checked again on every redirect.
- **Stored images are public** to anyone with the URL until they expire or are deleted with `DELETE /images/{name}`. The names are random unless `outputFilename` sets one.

To report a vulnerability, see [SECURITY.md](https://github.com/dmhendricks/image-mason/blob/main/SECURITY.md).

## Development

Requires Docker. Running the unit tests outside Docker requires the [.NET 10 SDK](https://dotnet.microsoft.com/download), and working on the web interface requires [Node.js 26](https://nodejs.org/).

```bash
git clone https://github.com/dmhendricks/image-mason.git
cd image-mason
cp .env.sample .env
```

Uncomment `COMPOSE_FILE` in `.env` to build from the local Dockerfile instead of pulling the published image, then:

```bash
docker compose up -d --build                         # http://localhost:8080
dotnet test --solution ImageMason.slnx               # unit tests
scripts/smoke-test.sh image-mason:dev                # checks every endpoint against a fresh container
scripts/optimize.sh photo.jpg medium                 # optimize one image and print the stats
```

The first build compiles libjxl, mozjpeg, libvips, aom, dav1d and libavif from source and takes 10 minutes or more; later builds reuse the cached layers.

The web interface is a Vue 3 app in `src/web`, built with Vite into static files that are embedded in the binary. To work on it with instant reloads, run the container as above, then:

```bash
cd src/web
npm ci
npm run dev        # http://localhost:5173; forwards /api and the rest to the container (PORT in .env)
npm test           # unit tests
npm run lint
npm run build      # type check, then build into src/web/dist
```

See [CONTRIBUTING.md](https://github.com/dmhendricks/image-mason/blob/main/CONTRIBUTING.md) for how to submit changes.

## License

Image Mason is licensed under the [Apache License 2.0](https://github.com/dmhendricks/image-mason/blob/main/LICENSE).

The Docker image also contains third-party software under its own licenses, including pngquant (GPL-3.0) and libvips (LGPL-2.1), which run as separate programs, and the aom AV1 encoder, which comes with the Alliance for Open Media Patent License. See [THIRD_PARTY_NOTICES.md](https://github.com/dmhendricks/image-mason/blob/main/THIRD_PARTY_NOTICES.md); the license texts are also in the image at `/usr/share/licenses/image-mason/`.
