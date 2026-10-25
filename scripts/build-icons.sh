#!/usr/bin/env bash
# Generate the favicons in src/ImageMason/wwwroot/, and the README logo in assets/, from a square SVG logo.
#
#   scripts/build-icons.sh <logo.svg> [logo-16.svg]
#
# logo-16.svg is an optional pixel-aligned variant for the 16x16 favicon.ico layer.
# Env: ICON_BACKGROUND (default #ffffff) fills the Apple touch icon, which iOS shows without transparency.
#
# Requires rsvg-convert (librsvg) and ImageMagick 7 (magick).
set -euo pipefail

logo="${1:?usage: build-icons.sh <logo.svg> [logo-16.svg]}"
logo16="${2:-$logo}"
background="${ICON_BACKGROUND:-#ffffff}"

root="$(cd "$(dirname "$0")/.." && pwd)"
out="${root}/src/ImageMason/wwwroot"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

mkdir -p "$out"
cp "$logo" "${out}/favicon.svg"

rsvg-convert -w 16 -h 16 "$logo16" -o "${work}/16.png"
for size in 32 48; do
    rsvg-convert -w "$size" -h "$size" "$logo" -o "${work}/${size}.png"
done
magick "${work}/16.png" "${work}/32.png" "${work}/48.png" "${out}/favicon.ico"

# 180x180 with the logo at 140px, centered on the background.
rsvg-convert -w 140 -h 140 "$logo" -o "${work}/touch.png"
magick "${work}/touch.png" -background "$background" -gravity center -extent 180x180 "${out}/apple-touch-icon.png"

# The README is also the Docker Hub overview, which needs an absolute URL and may not display SVGs.
rsvg-convert -w 128 -h 128 "$logo" -o "${root}/assets/logo.png"

echo "Wrote favicon.svg, favicon.ico, apple-touch-icon.png to ${out#"$root"/}, and assets/logo.png"
