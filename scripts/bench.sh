#!/usr/bin/env bash
# Run every JPEG, PNG, WebP, AVIF and JPEG XL in a folder through the service at every quality preset.
# Writes optimized files to <out>/<quality>/ for side-by-side review and prints a CSV + totals.
#
#   scripts/bench.sh <image-dir> [out-dir]
#
# Env: IMAGE_MASON_URL (default http://localhost:8080), IMAGE_MASON_API_KEY (optional)
set -euo pipefail

dir="${1:?usage: bench.sh <image-dir> [out-dir]}"
out="${2:-out}"
url="${IMAGE_MASON_URL:-http://localhost:8080}"
csv="${out}/results.csv"

mkdir -p "${out}/lossless" "${out}/high" "${out}/medium" "${out}/low"
echo "file,quality,original,optimized,saved_pct,method,score,ms" > "$csv"

headers=$(mktemp)
trap 'rm -f "$headers"' EXIT

header() { { grep -i "^x-image-mason-$1:" "$headers" || true; } | cut -d' ' -f2 | tr -d '\r'; }

find "$dir" -maxdepth 1 -type f \( -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.webp' -o -iname '*.avif' -o -iname '*.jxl' \) | sort | while read -r file; do
    name=$(basename "$file")
    for mode in lossless high medium low; do
        status=$(curl -sS -H "Expect:" -o "${out}/${mode}/${name}" -D "$headers" -w '%{http_code}' \
            -H "X-Api-Key: ${IMAGE_MASON_API_KEY:-}" \
            --data-binary "@${file}" \
            "${url}/api/v1/transform?quality=${mode}")

        if [[ "$status" != "200" ]]; then
            echo "${name},${mode},HTTP ${status}" >> "$csv"
            continue
        fi

        original=$(header original-size)
        optimized=$(header size)
        saved=$(awk -v a="$original" -v b="$optimized" 'BEGIN { printf "%.1f", (a - b) * 100 / a }')
        echo "${name},${mode},${original},${optimized},${saved},$(header method),$(header score),$(header duration-ms)" >> "$csv"
    done
done

column -s, -t < "$csv"
echo
awk -F, 'NR > 1 && $3 ~ /^[0-9]+$/ { o[$2] += $3; n[$2] += $4; t[$2] += $8 }
    END { for (m in o) printf "%-9s %10d -> %10d bytes  (%.1f%% saved, %d ms total)\n", m, o[m], n[m], (o[m] - n[m]) * 100 / o[m], t[m] }' "$csv"
