#!/usr/bin/env bash
# Optimize one image and print the X-Image-Mason-* stats.
#
#   scripts/optimize.sh <image> [lossless|high|medium|low] [output] [extra query, e.g. "&width=2000"]
#
# Env: IMAGE_MASON_URL (default http://localhost:8080), IMAGE_MASON_API_KEY (optional)
set -euo pipefail

input="${1:?usage: optimize.sh <image> [lossless|high|medium|low] [output] [extra query]}"
quality="${2:-high}"
ext="${input##*.}"
output="${3:-${input%.*}.opt.${ext}}"
url="${IMAGE_MASON_URL:-http://localhost:8080}"
extra="${4:-}"

headers=$(mktemp)
trap 'rm -f "$headers"' EXIT

status=$(curl -sS -o "$output" -D "$headers" -w '%{http_code}' \
    -H "X-Api-Key: ${IMAGE_MASON_API_KEY:-}" \
    --data-binary "@${input}" \
    "${url}/api/v1/transform?quality=${quality}${extra}")

if [[ "$status" != "200" ]]; then
    echo "HTTP $status: $(cat "$output")" >&2
    rm -f "$output"
    exit 1
fi

grep -i '^x-image-mason-' "$headers" | tr -d '\r'
echo "-> $output"
