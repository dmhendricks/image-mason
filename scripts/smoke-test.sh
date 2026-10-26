#!/usr/bin/env bash
# Start an Image Mason image and check every endpoint against the fixtures in tests/fixtures/.
# Used by CI and the release workflow; also runs locally.
#
#   scripts/smoke-test.sh <image> [expected-version]
#
# Env: SMOKE_PORT (default 18080). Requires docker, curl and jq.
set -euo pipefail

image="${1:?usage: smoke-test.sh <image> [expected-version]}"
expected_version="${2:-}"
port="${SMOKE_PORT:-18080}"
name="image-mason-smoke-$$"
base="http://127.0.0.1:${port}"
api="${base}/api/v1"
key="smoke-test-key"
fixtures="$(cd "$(dirname "$0")/../tests/fixtures" && pwd)"

work=$(mktemp -d)
failures=0

cleanup() {
    if [[ $failures -gt 0 ]]; then
        echo "--- container logs"
        docker logs "$name" 2>&1 | tail -50 || true
    fi
    docker rm -f "$name" >/dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT

# check <description> <command...>: runs the command and records whether it succeeded.
check() {
    local description=$1
    shift
    if "$@"; then
        printf '  ok    %s\n' "$description"
    else
        printf '  FAIL  %s\n' "$description"
        failures=$((failures + 1))
    fi
}

# request <name> <curl args...>: saves the response to $work/<name>.body and .headers; prints the status code.
request() {
    local name=$1
    shift
    curl -s -o "$work/$name.body" -D "$work/$name.headers" -w '%{http_code}' "$@"
}

header() { grep -i "^$2:" "$work/$1.headers" | head -1 | cut -d' ' -f2- | tr -d '\r'; }
json() { jq -r "$2" "$work/$1.body"; }
size() { wc -c < "$1" | tr -d ' '; }
starts_with() { [[ $1 == "$2"* ]]; }
ends_with() { [[ $1 == *"$2" ]]; }
smaller() { [[ $(size "$1") -lt $(size "$2") ]]; }
at_least() { awk -v value="$1" -v floor="$2" 'BEGIN { exit !(floor > 0 && value >= floor) }'; }

docker run -d --name "$name" -p "127.0.0.1:${port}:8080" \
    --read-only --tmpfs /tmp:size=256m \
    -e IMAGE_MASON_API_KEY="$key" \
    -e IMAGE_MASON_URL_ALLOWED_HOSTS=images.example.com \
    "$image" >/dev/null

for _ in $(seq 1 60); do
    curl -sf -o /dev/null "$base/health" && break
    sleep 0.5
done

echo "Smoke testing $image"
auth=(-H "X-Api-Key: $key")

code=$(request health "$base/health")
check "GET /health is 200" [ "$code" = 200 ]
check "  status is healthy" [ "$(json health .status)" = healthy ]
if [[ -n "$expected_version" ]]; then
    check "  version is $expected_version" [ "$(json health .build.version)" = "$expected_version" ]
fi

code=$(request webui "$base/")
check "GET / is 200" [ "$code" = 200 ]
check "  serves the web interface" grep -q '<div id="app">' "$work/webui.body"
check "  with a Content-Security-Policy" starts_with "$(header webui content-security-policy)" "default-src 'self'"
script=$(grep -o '/assets/[^"]*\.js' "$work/webui.body" | head -1 || true)
check "  references its script" [ -n "$script" ]
code=$(request webuiscript "$base$script")
check "  the script loads" [ "$code" = 200 ]
check "  and is cached for good" [ "$(header webuiscript cache-control)" = "public, max-age=31536000, immutable" ]

code=$(request docs "$base/docs")
check "GET /docs is 200" [ "$code" = 200 ]
check "  serves the usage examples" grep -q "api/v1/crop?rect=" "$work/docs.body"

code=$(request favicon "$base/favicon.svg")
check "GET /favicon.svg is 200" [ "$code" = 200 ]
check "  is an SVG" [ "$(header favicon content-type)" = image/svg+xml ]

code=$(request nokey --data-binary @"$fixtures/photo.jpg" "$api/optimize")
check "optimize without the API key is 401" [ "$code" = 401 ]
check "  as Problem Details" starts_with "$(header nokey content-type)" application/problem+json

code=$(request jpeg "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?quality=high")
check "optimize JPEG at high is 200" [ "$code" = 200 ]
check "  returns a JPEG" [ "$(header jpeg content-type)" = image/jpeg ]
check "  smaller than the input" smaller "$work/jpeg.body" "$fixtures/photo.jpg"
check "  scored at or above the floor" at_least "$(header jpeg x-image-mason-score)" "$(header jpeg x-image-mason-min-score)"

code=$(request lossless "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?quality=lossless")
check "optimize JPEG lossless is 200" [ "$code" = 200 ]
check "  uses jpegtran" [ "$(header lossless x-image-mason-method)" = jpegtran ]

code=$(request png "${auth[@]}" --data-binary @"$fixtures/graphic.png" "$api/optimize")
check "optimize PNG is 200" [ "$code" = 200 ]
check "  returns a PNG" [ "$(header png content-type)" = image/png ]
check "  smaller than the input" smaller "$work/png.body" "$fixtures/graphic.png"
check "  uses the default quality, medium" [ "$(header png x-image-mason-quality)" = medium ]

code=$(request resize "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?maxWidth=320")
check "optimize with maxWidth=320 is 200" [ "$code" = 200 ]
check "  resizes to 320 wide" starts_with "$(header resize x-image-mason-dimensions)" 320x

code=$(request json "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?output=json&outputFilename=smoke.jpg")
check "optimize with output=json is 200" [ "$code" = 200 ]
check "  returns the stored URL" ends_with "$(json json .output)" /images/smoke.jpg
code=$(request stored "$base/images/smoke.jpg")
check "  the stored image is served" [ "$code" = 200 ]

code=$(request webp "${auth[@]}" --data-binary @"$fixtures/photo.webp" "$api/optimize?maxWidth=320")
check "optimize WebP is 200" [ "$code" = 200 ]
check "  returns a WebP" [ "$(header webp content-type)" = image/webp ]
check "  resizes to 320 wide" starts_with "$(header webp x-image-mason-dimensions)" 320x

code=$(request optwebp "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?format=webp&maxWidth=320&preferJpeg=true")
check "optimize with format=webp is 200" [ "$code" = 200 ]
check "  returns a WebP, ignoring preferJpeg" [ "$(header optwebp content-type)" = image/webp ]
check "  resizes to 320 wide" starts_with "$(header optwebp x-image-mason-dimensions)" 320x

code=$(request optheight "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?height=100")
check "optimize with height=100 is 200" [ "$code" = 200 ]
check "  scales to 100 high" ends_with "$(header optheight x-image-mason-dimensions)" x100

code=$(request optcover "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?width=200&height=150&fit=cover&format=avif")
check "optimize with fit=cover, as AVIF, is 200" [ "$code" = 200 ]
check "  crops to 200x150" [ "$(header optcover x-image-mason-dimensions)" = 200x150 ]
check "  returns an AVIF" [ "$(header optcover content-type)" = image/avif ]

code=$(request optboth "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?maxWidth=320&width=200")
check "optimize with maxWidth and width is 400" [ "$code" = 400 ]

code=$(request optbadformat "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?format=gif")
check "optimize to an unknown format is 400" [ "$code" = 400 ]

code=$(request towebp "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/convert?format=webp")
check "convert JPEG to WebP is 200" [ "$code" = 200 ]
check "  returns a WebP" [ "$(header towebp content-type)" = image/webp ]
check "  scored at or above the floor" at_least "$(header towebp x-image-mason-score)" "$(header towebp x-image-mason-min-score)"

code=$(request toavif "${auth[@]}" --data-binary @"$fixtures/graphic.png" "$api/convert?format=avif")
check "convert PNG to AVIF is 200" [ "$code" = 200 ]
check "  returns an AVIF" [ "$(header toavif content-type)" = image/avif ]
check "  smaller than the input" smaller "$work/toavif.body" "$fixtures/graphic.png"

code=$(request fromavif "${auth[@]}" --data-binary @"$fixtures/photo.avif" "$api/convert?format=jpeg&quality=medium")
check "convert AVIF to JPEG is 200" [ "$code" = 200 ]
check "  returns a JPEG" [ "$(header fromavif content-type)" = image/jpeg ]

code=$(request tojxl "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/convert?format=jxl")
check "convert JPEG to JPEG XL is 200" [ "$code" = 200 ]
check "  returns a JPEG XL" [ "$(header tojxl content-type)" = image/jxl ]
check "  encodes lossy with cjxl" starts_with "$(header tojxl x-image-mason-method)" cjxl-d
check "  scored at or above the floor" at_least "$(header tojxl x-image-mason-score)" "$(header tojxl x-image-mason-min-score)"
check "  smaller than the input" smaller "$work/tojxl.body" "$fixtures/photo.jpg"

code=$(request jxlrecompress "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/convert?format=jxl&quality=lossless")
check "convert JPEG to JPEG XL losslessly is 200" [ "$code" = 200 ]
check "  recompresses the JPEG data" [ "$(header jxlrecompress x-image-mason-method)" = cjxl-jpeg ]
check "  smaller than the input" smaller "$work/jxlrecompress.body" "$fixtures/photo.jpg"

code=$(request jxlgraphic "${auth[@]}" --data-binary @"$fixtures/graphic.png" "$api/convert?format=jxl")
check "convert PNG to JPEG XL is 200" [ "$code" = 200 ]
check "  smaller than the input" smaller "$work/jxlgraphic.body" "$fixtures/graphic.png"

code=$(request fromjxl "${auth[@]}" --data-binary @"$fixtures/photo.jxl" "$api/convert?format=webp&quality=medium")
check "convert JPEG XL to WebP is 200" [ "$code" = 200 ]
check "  returns a WebP" [ "$(header fromjxl content-type)" = image/webp ]
check "  keeps the 640x427 size" [ "$(header fromjxl x-image-mason-dimensions)" = 640x427 ]

code=$(request topng "${auth[@]}" --data-binary @"$fixtures/photo.webp" "$api/convert?format=png&quality=lossless")
check "convert WebP to PNG is 200" [ "$code" = 200 ]
check "  returns a PNG" [ "$(header topng content-type)" = image/png ]

code=$(request convertjson "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/convert?format=avif&output=json&outputFilename=smoke.avif")
check "convert with output=json is 200" [ "$code" = 200 ]
check "  returns the stored URL" ends_with "$(json convertjson .output)" /images/smoke.avif
code=$(request storedavif "$base/images/smoke.avif")
check "  the stored image is served as AVIF" [ "$(header storedavif content-type)" = image/avif ]

code=$(request deletenokey -X DELETE "$base/images/smoke.avif")
check "DELETE a stored image without the API key is 401" [ "$code" = 401 ]
code=$(request delete "${auth[@]}" -X DELETE "$base/images/smoke.avif")
check "DELETE a stored image is 204" [ "$code" = 204 ]
code=$(request deleted "$base/images/smoke.avif")
check "  it is no longer served" [ "$code" = 404 ]
code=$(request deleteagain "${auth[@]}" -X DELETE "$base/images/smoke.avif")
check "  deleting it again is 404" [ "$code" = 404 ]

code=$(request badformat2 "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/convert?format=gif")
check "convert to an unknown format is 400" [ "$code" = 400 ]

code=$(request upload "${auth[@]}" --data-binary @"$fixtures/graphic.png" "$api/upload")
check "upload is 200" [ "$code" = 200 ]
check "  stores the image unchanged" [ "$(json upload .method)" = upload ]

code=$(request uploadwebp "${auth[@]}" --data-binary @"$fixtures/photo.webp" "$api/upload")
check "upload WebP is 200" [ "$code" = 200 ]
check "  reports WebP" [ "$(json uploadwebp .mimeType)" = image/webp ]

code=$(request resize "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/resize?width=200&height=200&fit=cover")
check "resize with fit=cover is 200" [ "$code" = 200 ]
check "  crops to 200x200" [ "$(header resize x-image-mason-dimensions)" = 200x200 ]

code=$(request resizeconvert "${auth[@]}" --data-binary @"$fixtures/photo.webp" "$api/resize?height=100&format=avif")
check "resize to a height, as AVIF, is 200" [ "$code" = 200 ]
check "  returns an AVIF" [ "$(header resizeconvert content-type)" = image/avif ]
check "  scales to 100 high" ends_with "$(header resizeconvert x-image-mason-dimensions)" x100

code=$(request resizenoupscale "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/resize?width=2000")
check "resize never enlarges unless upscale=true" [ "$(header resizenoupscale x-image-mason-dimensions)" = 640x427 ]

code=$(request resizebad "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/resize?width=200&fit=cover")
check "fit=cover without a height is 400" [ "$code" = 400 ]

code=$(request crop "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/crop?rect=100,50,300,200")
check "crop is 200" [ "$code" = 200 ]
check "  keeps the 300x200 area" [ "$(header crop x-image-mason-dimensions)" = 300x200 ]
check "  method starts with crop+" starts_with "$(header crop x-image-mason-method)" crop+
check "  scored at or above the floor" at_least "$(header crop x-image-mason-score)" "$(header crop x-image-mason-min-score)"

code=$(request cropresize "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?rect=0,0,400,400&width=200&format=webp")
check "optimize with rect and width, as WebP, is 200" [ "$code" = 200 ]
check "  crops, then resizes to 200x200" [ "$(header cropresize x-image-mason-dimensions)" = 200x200 ]
check "  method starts with crop+resize+" starts_with "$(header cropresize x-image-mason-method)" crop+resize+

code=$(request cropavif "${auth[@]}" --data-binary @"$fixtures/photo.avif" "$api/crop?rect=20,20,100,100")
check "crop an AVIF is 200" [ "$code" = 200 ]
check "  keeps the 100x100 area" [ "$(header cropavif x-image-mason-dimensions)" = 100x100 ]

code=$(request cropjxl "${auth[@]}" --data-binary @"$fixtures/photo.jxl" "$api/crop?rect=20,20,100,100")
check "crop a JPEG XL is 200" [ "$code" = 200 ]
check "  keeps the 100x100 area" [ "$(header cropjxl x-image-mason-dimensions)" = 100x100 ]
check "  returns a JPEG XL" [ "$(header cropjxl content-type)" = image/jxl ]

code=$(request croppng "${auth[@]}" --data-binary @"$fixtures/graphic.png" "$api/crop?rect=40,40,400,240&quality=lossless")
check "crop a PNG losslessly is 200" [ "$code" = 200 ]
check "  uses oxipng" [ "$(header croppng x-image-mason-method)" = crop+oxipng ]

code=$(request cropoutside "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/crop?rect=600,0,100,100")
check "crop past the image's edge is 400" [ "$code" = 400 ]
code=$(request cropnorect "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/crop")
check "crop without rect is 400" [ "$code" = 400 ]
code=$(request cropbadrect "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/crop?rect=1,2,3")
check "crop with a malformed rect is 400" [ "$code" = 400 ]

code=$(request limits "$api/limits")
check "GET limits without the API key is 200" [ "$code" = 200 ]
check "  lists five formats" [ "$(json limits '.formats | length')" = 5 ]
check "  reports the API key" [ "$(json limits .apiKeyRequired)" = true ]
check "  reports the default quality" [ "$(json limits .defaultQuality)" = medium ]

code=$(request info "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/info")
check "info is 200" [ "$code" = 200 ]
check "  reports the format" [ "$(json info .format)" = jpeg ]
check "  reports the dimensions" [ "$(json info '"\(.width)x\(.height)"')" = 640x427 ]

code=$(request infoavif "${auth[@]}" --data-binary @"$fixtures/photo.avif" "$api/info")
check "info on AVIF is 200" [ "$code" = 200 ]
check "  reports AVIF" [ "$(json infoavif .format)" = avif ]

code=$(request infojxl "${auth[@]}" --data-binary @"$fixtures/photo.jxl" "$api/info")
check "info on JPEG XL is 200" [ "$code" = 200 ]
check "  reports JPEG XL" [ "$(json infojxl .format)" = jxl ]
check "  reports the dimensions" [ "$(json infojxl '"\(.width)x\(.height)"')" = 640x427 ]

code=$(request infonokey --data-binary @"$fixtures/photo.jpg" "$api/info")
check "info without the API key is 401" [ "$code" = 401 ]

code=$(request badformat "${auth[@]}" --data-binary "not an image" "$api/optimize")
check "a non-image is 415" [ "$code" = 415 ]

# The headers are intact, so it's recognized as a JPEG, but there's too little left to encode.
head -c 2000 "$fixtures/photo.jpg" > "$work/damaged.jpg"
code=$(request damaged "${auth[@]}" --data-binary @"$work/damaged.jpg" "$api/optimize?format=webp")
check "a damaged image is 422" [ "$code" = 422 ]
check "  saying it may be damaged" grep -q "may be damaged" "$work/damaged.body"

code=$(request badquality "${auth[@]}" --data-binary @"$fixtures/photo.jpg" "$api/optimize?quality=ultra")
check "an unknown quality is 400" [ "$code" = 400 ]

code=$(request badhost "${auth[@]}" -X POST "$api/optimize?url=http://169.254.169.254/latest/meta-data")
check "a url outside IMAGE_MASON_URL_ALLOWED_HOSTS is 400" [ "$code" = 400 ]

code=$(request notfound "$base/nope")
check "an unknown route is 404" [ "$code" = 404 ]
check "  as Problem Details" starts_with "$(header notfound content-type)" application/problem+json

if [[ $failures -gt 0 ]]; then
    echo "$failures check(s) failed"
    exit 1
fi
echo "All checks passed"
