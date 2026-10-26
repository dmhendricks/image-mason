# syntax=docker/dockerfile:1

# Image Mason: JPEG, PNG, WebP, AVIF and JPEG XL optimization API (oxipng, pngquant, mozjpeg, jpegli, cwebp,
# avifenc, cjxl, SSIMULACRA2).
#
# Four stages:
#   1. tools   - compiles libvips (vips, vipsthumbnail), mozjpeg (jpegtran), libjxl (ssimulacra2, cjxl, djxl), jpegli
#                (cjpegli) and libavif (avifenc, avifdec), which Alpine doesn't package or packages at older versions,
#                and fetches oxipng's checksummed release build. Uses the same Alpine release as the runtime (musl ABI).
#   2. web     - builds the web interface (src/web, Vue + Vite) into static files. Node exists only here.
#   3. app     - Native AOT publish of the .NET minimal API, with the web interface embedded, so the runtime
#                needs no .NET installation and no web root.
#   4. runtime - runtime-deps:alpine + pngquant and cwebp from Alpine + the tools from stage 1.

ARG ALPINE_VERSION=3.24
ARG DOTNET_VERSION=10.0
# Node LTS, for building the web interface only.
ARG NODE_VERSION=26

# ---------------------------------------------------------------------------
# 1. Native tools
# ---------------------------------------------------------------------------
FROM alpine:${ALPINE_VERSION} AS tools

ARG LIBJXL_VERSION=v0.12.0
# jpegli left libjxl in 0.12 for github.com/google/jpegli, which has no tagged releases yet: pinned to a commit.
ARG JPEGLI_COMMIT=031a0077f5799a6041004267fc12b956c1f52a20
ARG MOZJPEG_VERSION=v4.1.5
ARG LIBVIPS_VERSION=v8.18.7
ARG LIBAVIF_VERSION=v1.4.2
ARG AOM_VERSION=v3.15.2
ARG DAV1D_VERSION=1.5.4
# Alpine packages oxipng 9; 10 comes from the project's static musl builds, checked against these SHA-256 sums.
ARG OXIPNG_VERSION=10.2.1
ARG OXIPNG_SHA256_AMD64=1813750ef592c5350ca79c88f98b2c0876d05c826dc7156245256d4255c1ad17
ARG OXIPNG_SHA256_ARM64=ff21aa2a005b324c8dfbdf13cd2f469fb6728b27dacded82635824ecd12e3b35
ARG TARGETARCH

RUN apk add --no-cache build-base cmake ninja meson git nasm perl pkgconf \
    zlib-dev libpng-dev giflib-dev libjpeg-turbo-dev lcms2-dev brotli-dev highway-dev \
    glib-dev expat-dev libexif-dev libwebp-dev

# libvips: vipsthumbnail downscales and applies EXIF orientation; the vips CLI flattens alpha and decodes WebP.
# Alpine's vips package pulls in cairo, pango, librsvg, OpenEXR and more (+55 MB), so build it with JPEG,
# PNG, WebP, ICC, EXIF and SIMD only, linked statically.
RUN git clone --depth 1 --branch ${LIBVIPS_VERSION} https://github.com/libvips/libvips.git /src/libvips \
    && meson setup /build/libvips /src/libvips \
        --buildtype=release \
        --default-library=static \
        -Dauto_features=disabled \
        -Djpeg=enabled \
        -Dpng=enabled \
        -Dwebp=enabled \
        -Dlcms=enabled \
        -Dexif=enabled \
        -Dhighway=enabled \
        -Dzlib=enabled \
        -Dmodules=disabled \
        -Dintrospection=disabled \
        -Ddeprecated=false \
        -Dexamples=false \
        -Dcplusplus=false \
        -Dnsgif=false \
        -Dppm=false \
        -Danalyze=false \
        -Dradiance=false \
    && meson compile -C /build/libvips tools/vipsthumbnail tools/vips \
    && install -D -s /build/libvips/tools/vipsthumbnail /out/bin/vipsthumbnail \
    && install -D -s /build/libvips/tools/vips /out/bin/vips

# mozjpeg: only jpegtran is used (the lossless JPEG step). Static libjpeg, so it never collides with
# the system libjpeg-turbo that libjxl links against.
# CMAKE_POLICY_VERSION_MINIMUM: mozjpeg declares cmake 2.8.12, which CMake 4 rejects.
RUN git clone --depth 1 --branch ${MOZJPEG_VERSION} https://github.com/mozilla/mozjpeg.git /src/mozjpeg \
    && cmake -S /src/mozjpeg -B /build/mozjpeg -G Ninja \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -DENABLE_SHARED=OFF \
        -DWITH_TURBOJPEG=OFF \
        -DPNG_SUPPORTED=OFF \
    && cmake --build /build/mozjpeg --target jpegtran-static \
    && install -D -s /build/mozjpeg/jpegtran-static /out/bin/jpegtran

# libjxl: builds only ssimulacra2, cjxl (JPEG XL encoder) and djxl (decoder). libjxl is shared, so the three
# tools share one copy of the codec. Third-party libraries come from Alpine packages.
RUN git clone --depth 1 --branch ${LIBJXL_VERSION} https://github.com/libjxl/libjxl.git /src/libjxl \
    && cmake -S /src/libjxl -B /build/libjxl -G Ninja \
        -DCMAKE_BUILD_TYPE=Release \
        -DBUILD_SHARED_LIBS=ON \
        -DBUILD_TESTING=OFF \
        -DJPEGXL_ENABLE_TOOLS=ON \
        -DJPEGXL_ENABLE_DEVTOOLS=ON \
        -DJPEGXL_ENABLE_SKCMS=OFF \
        -DJPEGXL_FORCE_SYSTEM_LCMS2=ON \
        -DJPEGXL_FORCE_SYSTEM_BROTLI=ON \
        -DJPEGXL_FORCE_SYSTEM_HWY=ON \
        -DJPEGXL_ENABLE_BENCHMARK=OFF \
        -DJPEGXL_ENABLE_EXAMPLES=OFF \
        -DJPEGXL_ENABLE_MANPAGES=OFF \
        -DJPEGXL_ENABLE_JNI=OFF \
        -DJPEGXL_ENABLE_SJPEG=OFF \
        -DJPEGXL_ENABLE_OPENEXR=OFF \
        -DJPEGXL_ENABLE_DOXYGEN=OFF \
        -DJPEGXL_ENABLE_PLUGINS=OFF \
        -DJPEGXL_ENABLE_VIEWERS=OFF \
    && cmake --build /build/libjxl --target ssimulacra2 cjxl djxl \
    && for tool in ssimulacra2 cjxl djxl; do install -D -s /build/libjxl/tools/$tool /out/bin/$tool; done \
    && mkdir -p /out/lib && cp -P /build/libjxl/lib/libjxl.so* /build/libjxl/lib/libjxl_cms.so* \
        /build/libjxl/lib/libjxl_threads.so* /out/lib/ \
    && strip /out/lib/libjxl*.so.*.*

# jpegli: cjpegli, with jpegli linked in statically. Third-party libraries come from Alpine packages; the
# libjpeg-turbo submodule is needed only for jpegli's API headers. Fetched by commit, since there are no tags.
RUN git init -q /src/jpegli \
    && git -C /src/jpegli fetch -q --depth 1 https://github.com/google/jpegli.git ${JPEGLI_COMMIT} \
    && git -C /src/jpegli checkout -q FETCH_HEAD \
    && git -C /src/jpegli submodule update --init --depth 1 third_party/libjpeg-turbo \
    && cmake -S /src/jpegli -B /build/jpegli -G Ninja \
        -DCMAKE_BUILD_TYPE=Release \
        -DBUILD_SHARED_LIBS=OFF \
        -DBUILD_TESTING=OFF \
        -DJPEGLI_ENABLE_TOOLS=ON \
        -DJPEGLI_ENABLE_JPEGLI_LIBJPEG=OFF \
        -DJPEGLI_ENABLE_SKCMS=OFF \
        -DJPEGLI_FORCE_SYSTEM_LCMS2=ON \
        -DJPEGLI_FORCE_SYSTEM_HWY=ON \
        -DJPEGLI_ENABLE_BENCHMARK=OFF \
        -DJPEGLI_ENABLE_MANPAGES=OFF \
        -DJPEGLI_ENABLE_JNI=OFF \
        -DJPEGLI_ENABLE_SJPEG=OFF \
        -DJPEGLI_ENABLE_OPENEXR=OFF \
        -DJPEGLI_ENABLE_DOXYGEN=OFF \
    && cmake --build /build/jpegli --target cjpegli \
    && install -D -s /build/jpegli/tools/cjpegli /out/bin/cjpegli

# oxipng: the release's static musl binary for this architecture, verified before it's unpacked.
RUN case "$TARGETARCH" in \
        amd64) arch=x86_64 sha="$OXIPNG_SHA256_AMD64" ;; \
        arm64) arch=aarch64 sha="$OXIPNG_SHA256_ARM64" ;; \
        *) echo "No oxipng build for $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && name="oxipng-${OXIPNG_VERSION}-${arch}-unknown-linux-musl" \
    && wget -q -O /tmp/oxipng.tar.gz "https://github.com/oxipng/oxipng/releases/download/v${OXIPNG_VERSION}/${name}.tar.gz" \
    && echo "$sha  /tmp/oxipng.tar.gz" | sha256sum -c - \
    && tar -xzf /tmp/oxipng.tar.gz -C /tmp \
    && install -D "/tmp/${name}/oxipng" /out/bin/oxipng \
    && rm -rf /tmp/oxipng.tar.gz "/tmp/${name}"

# libavif: avifenc and avifdec share one libavif.so, with aom (encoder), dav1d (decoder) and libyuv linked into
# it, so the codecs are on disk once. libyuv is the version libavif pins (Alpine's has no linkable library);
# libsharpyuv, libpng and libjpeg come from Alpine.
RUN git clone --depth 1 --branch ${DAV1D_VERSION} https://code.videolan.org/videolan/dav1d.git /src/dav1d \
    && meson setup /build/dav1d /src/dav1d \
        --buildtype=release \
        --default-library=static \
        --prefix=/opt/av1 \
        --libdir=lib \
        -Denable_tools=false \
        -Denable_tests=false \
    && meson install -C /build/dav1d

# _GNU_SOURCE: aom's apps call fseeko/ftello, which musl declares only with it.
RUN git clone --depth 1 --branch ${AOM_VERSION} https://aomedia.googlesource.com/aom /src/aom \
    && cmake -S /src/aom -B /build/aom -G Ninja \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX=/opt/av1 \
        -DCMAKE_INSTALL_LIBDIR=lib \
        -DCMAKE_C_FLAGS=-D_GNU_SOURCE \
        -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
        -DBUILD_SHARED_LIBS=OFF \
        -DCONFIG_AV1_DECODER=0 \
        -DENABLE_DOCS=OFF \
        -DENABLE_EXAMPLES=OFF \
        -DENABLE_TESTS=OFF \
        -DENABLE_TOOLS=OFF \
    && cmake --build /build/aom \
    && cmake --install /build/aom

RUN git clone --depth 1 --branch ${LIBAVIF_VERSION} https://github.com/AOMediaCodec/libavif.git /src/libavif \
    && PKG_CONFIG_PATH=/opt/av1/lib/pkgconfig cmake -S /src/libavif -B /build/libavif -G Ninja \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_PREFIX_PATH=/opt/av1 \
        -DBUILD_SHARED_LIBS=ON \
        -DAVIF_BUILD_APPS=ON \
        -DAVIF_CODEC_AOM=SYSTEM \
        -DAVIF_CODEC_AOM_DECODE=OFF \
        -DAVIF_CODEC_DAV1D=SYSTEM \
        -DAVIF_LIBYUV=LOCAL \
        -DAVIF_LIBSHARPYUV=SYSTEM \
        -DAVIF_JPEG=SYSTEM \
        -DAVIF_ZLIBPNG=SYSTEM \
        -DAVIF_LIBXML2=OFF \
    && cmake --build /build/libavif --target avifenc avifdec \
    && install -D -s /build/libavif/avifenc /out/bin/avifenc \
    && install -D -s /build/libavif/avifdec /out/bin/avifdec \
    && mkdir -p /out/lib && cp -P /build/libavif/libavif.so* /out/lib/ \
    && strip /out/lib/libavif.so.*.*

# ---------------------------------------------------------------------------
# 2. Web interface
# ---------------------------------------------------------------------------
# The output is the same on every architecture, so it builds once, on the build machine's platform.
FROM --platform=$BUILDPLATFORM node:${NODE_VERSION}-alpine AS web

WORKDIR /src/web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/web/ ./
RUN npm run build

# ---------------------------------------------------------------------------
# 3. .NET app (Native AOT)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine${ALPINE_VERSION}-aot AS app

ARG TARGETARCH
ARG VERSION=0.0.0-dev
WORKDIR /build

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ImageMason/ImageMason.csproj src/ImageMason/
RUN RID=linux-musl-$([ "$TARGETARCH" = "amd64" ] && echo x64 || echo "$TARGETARCH") \
    && dotnet restore src/ImageMason/ImageMason.csproj -r "$RID"

COPY src/ImageMason/ src/ImageMason/
# ImageMason.csproj embeds ../web/dist as the web interface.
COPY --from=web /src/web/dist/ src/web/dist/
RUN RID=linux-musl-$([ "$TARGETARCH" = "amd64" ] && echo x64 || echo "$TARGETARCH") \
    && dotnet publish src/ImageMason/ImageMason.csproj -c Release -r "$RID" --no-restore \
        -p:Version="$VERSION" -o /app \
    && rm -f /app/*.dbg

# ---------------------------------------------------------------------------
# 4. Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION}-alpine${ALPINE_VERSION}

ARG VERSION=0.0.0-dev

LABEL org.opencontainers.image.title="Image Mason" \
      org.opencontainers.image.description="Self-hosted API to optimize, convert and temporarily host JPEG, PNG, WebP, AVIF and JPEG XL images" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.authors="Daniel Hendricks" \
      org.opencontainers.image.source="https://github.com/dmhendricks/image-mason" \
      org.opencontainers.image.licenses="Apache-2.0"

# pngquant and cwebp from Alpine; the rest are shared libraries the compiled tools link against.
# tzdata supplies IANA zones so TZ can move the clock off UTC.
RUN apk add --no-cache pngquant libwebp-tools tzdata \
    libpng libjpeg-turbo giflib lcms2 brotli-libs libhwy libstdc++ libgcc \
    glib expat libexif libwebp libwebpmux libwebpdemux libsharpyuv

COPY --from=tools /out/bin/ /usr/local/bin/
COPY --from=tools /out/lib/ /usr/local/lib/
COPY --from=app /app/ /app/
COPY LICENSE NOTICE THIRD_PARTY_NOTICES.md /usr/share/licenses/image-mason/
COPY licenses/ /usr/share/licenses/image-mason/licenses/

# Fail the build if any tool is missing a shared library.
RUN cjpegli --version >/dev/null 2>&1 || cjpegli --help >/dev/null \
    && ssimulacra2 2>&1 | grep -qi usage \
    && cjxl --version | grep -qi cjxl \
    && djxl --version | grep -qi djxl \
    && jpegtran -version 2>&1 | grep -qi mozjpeg \
    && vipsthumbnail --vips-version \
    && vips -l foreign | grep -q webpload \
    && avifenc --version | grep -qi 'aom' \
    && avifdec --version | grep -qi 'dav1d' \
    && cwebp -version \
    && oxipng --version && pngquant --version

# Stored images live under /data. Created here so a new named volume starts out owned by the app user.
RUN mkdir -p /data/images && chown -R "$APP_UID:$APP_UID" /data
VOLUME /data

# UTC instant of this image build. A cached layer keeps the previous stamp, which is the last time the image contents changed.
RUN date -u +%Y-%m-%dT%H:%M:%SZ > /app/build-date

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s \
    CMD wget -qO- http://127.0.0.1:8080/health || exit 1

USER $APP_UID
WORKDIR /app
ENTRYPOINT ["/app/image-mason"]
