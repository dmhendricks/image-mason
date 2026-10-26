#!/bin/sh
# Writes the licenses of the crates compiled into ssim2, for the image's license folder. Each crate's own license
# files come from the downloaded crates; licenses/<crate>.txt stands in for a crate that ships none. A crate with
# neither fails the build, unless it may be used under Apache-2.0 (whose text the image has) or the Unlicense.
#
#   notices.sh <output-file>    (run in src/ssim2 after cargo has downloaded the crates)
set -eu
# A failed cargo tree must fail the build, not leave the file empty.
set -o pipefail

out=$1
registry=$(ls -d "${CARGO_HOME:-$HOME/.cargo}"/registry/src/*/ | head -1)

{
    echo "Rust crates compiled into ssim2, Image Mason's SSIMULACRA2 scorer (src/ssim2), and their licenses."
    echo "The Rust standard library's notices are in rust-std-COPYRIGHT.html."

    cargo tree --locked --edges normal --prefix none --format '{p}|{l}' \
        | sed 's/ ([^|]*)|/|/; s/ (\*)$//' | sort -u \
        | while IFS='|' read -r crate license; do
            name=${crate% v*}
            version=${crate##* v}
            [ "$name" = ssim2 ] && continue

            printf '\n\n==== %s %s (%s) ====\n' "$name" "$version" "$license"
            files=$(find "$registry$name-$version" -maxdepth 1 -type f \
                \( -iname 'licen[cs]e*' -o -iname 'copying*' -o -iname 'notice*' \) | sort)
            if [ -n "$files" ]; then
                for file in $files; do
                    printf '\n---- %s\n\n' "$(basename "$file")"
                    cat "$file"
                done
            elif [ -f "licenses/$name.txt" ]; then
                printf '\n'
                cat "licenses/$name.txt"
            else
                case "$license" in
                    *Apache-2.0*) printf '\nUsed under Apache-2.0; the crate includes no license file. See Apache-2.0.txt.\n' ;;
                    *Unlicense*) printf '\nUsed under the Unlicense, which has no conditions.\n' ;;
                    *) echo "notices.sh: $name $version ($license) has no license file; add licenses/$name.txt" >&2; exit 1 ;;
                esac
            fi
        done
} > "$out"
