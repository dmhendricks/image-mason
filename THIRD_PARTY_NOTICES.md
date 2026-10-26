# Third-Party Notices

Image Mason is licensed under the [Apache License 2.0](LICENSE). The Docker image also contains third-party software, distributed under the licenses listed below. Those licenses apply to those components only.

The image tools (`cjpegli`, `ssim2`, `cjxl`, `djxl`, `jpegtran`, `vips`, `vipsthumbnail`, `avifenc`, `avifdec`, `cwebp`, `oxipng`, `pngquant`) run as separate programs. None of them is linked into the Image Mason application. The application binary contains the .NET runtime and ASP.NET Core, compiled in ahead of time, and the web interface's files (Vue and the Barlow fonts), embedded as resources.

Full license texts are in the [`licenses/`](licenses/) directory. In the image, they are in `/usr/share/licenses/image-mason/`, together with the .NET runtime's and ASP.NET Core's third-party notices and the licenses of the Rust crates in `ssim2`, which the build copies from the packages it compiles in.

## Image Tools

| Component | Version | License | Included as | Source |
|---|---|---|---|---|
| libjxl (`cjxl`, `djxl`) | 0.12.0 | BSD-3-Clause, plus a patent grant | Built from source | <https://github.com/libjxl/libjxl/tree/v0.12.0> |
| fast-ssim2 (in `ssim2`) | 0.9.0 | BSD-2-Clause | Built from source | <https://github.com/imazen/fast-ssim2/tree/v0.9.0-release> |
| jpegli (`cjpegli`) | `031a0077` | BSD-3-Clause, plus a patent grant | Built from source | <https://github.com/google/jpegli/tree/031a0077f5799a6041004267fc12b956c1f52a20> |
| mozjpeg (`jpegtran`) | 4.1.5 | IJG AND BSD-3-Clause AND Zlib | Built from source | <https://github.com/mozilla/mozjpeg/tree/v4.1.5> |
| libvips (`vips`, `vipsthumbnail`) | 8.18.7 | LGPL-2.1-or-later | Built from source | <https://github.com/libvips/libvips/tree/v8.18.7> |
| libavif (`avifenc`, `avifdec`) | 1.4.2 | BSD-2-Clause | Built from source | <https://github.com/AOMediaCodec/libavif/tree/v1.4.2> |
| aom (AV1 encoder, in libavif) | 3.15.2 | BSD-2-Clause, plus the AOM Patent License 1.0 | Built from source | <https://aomedia.googlesource.com/aom/+/refs/tags/v3.15.2> |
| dav1d (AV1 decoder, in libavif) | 1.5.4 | BSD-2-Clause | Built from source | <https://code.videolan.org/videolan/dav1d/-/tree/1.5.4> |
| libyuv (in libavif) | 1924 (`644251f2`) | BSD-3-Clause, plus a patent grant | Built from source | <https://chromium.googlesource.com/libyuv/libyuv/+/644251f252a84bf8ce91ff0aca86a9b16b069ab8> |
| libargparse (in `avifenc`, `avifdec`) | `ee74d1b5` | MIT | Built from source | <https://github.com/kmurray/libargparse/tree/ee74d1b53bd680748af14e737378de57e2a0a954> |
| libwebp (`cwebp`, and WebP in libvips) | 1.6.0 | BSD-3-Clause, plus a patent grant | Alpine package | <https://github.com/webmproject/libwebp/tree/v1.6.0> |
| oxipng | 10.2.1 | MIT | Upstream release build | <https://github.com/oxipng/oxipng/tree/v10.2.1> |
| pngquant (with libimagequant) | 3.0.3 | GPL-3.0-or-later | Alpine package | <https://github.com/kornelski/pngquant/tree/3.0.3> |

### libjxl

Copyright (c) the JPEG XL Project Authors. All rights reserved. See [`licenses/libjxl-LICENSE.txt`](licenses/libjxl-LICENSE.txt) and [`licenses/libjxl-PATENTS.txt`](licenses/libjxl-PATENTS.txt).

### ssim2 and fast-ssim2

`ssim2` is Image Mason's SSIMULACRA2 scorer, in [`src/ssim2/`](src/ssim2/) under the Apache License 2.0. It's built from source with [fast-ssim2](https://github.com/imazen/fast-ssim2) (Copyright (c) 2022-2022, the rav1e contributors; BSD-2-Clause) and other Rust crates, and links libjpeg-turbo and Little CMS (listed under [Base Image and Shared Libraries](#base-image-and-shared-libraries)) statically. Each crate's license is in `ssim2-THIRD-PARTY-NOTICES.txt`, and the Rust standard library's in `rust-std-COPYRIGHT.html`, both in `/usr/share/licenses/image-mason/licenses/` in the image; the build writes them from the crates it compiles.

### jpegli

Copyright (c) the JPEG XL Project Authors. All rights reserved. jpegli's license and patent grant are identical to libjxl's: see [`licenses/libjxl-LICENSE.txt`](licenses/libjxl-LICENSE.txt) and [`licenses/libjxl-PATENTS.txt`](licenses/libjxl-PATENTS.txt).

### mozjpeg

This software is based in part on the work of the Independent JPEG Group.

Copyright (C) 1991-2020, Thomas G. Lane, Guido Vollbeding. Copyright (C) 2009-2023 D. R. Commander. Copyright (C) 2015 Viktor Szathmáry. See [`licenses/mozjpeg-LICENSE.md`](licenses/mozjpeg-LICENSE.md) and [`licenses/mozjpeg-README.ijg.txt`](licenses/mozjpeg-README.ijg.txt).

### libvips

Licensed under the GNU Lesser General Public License, version 2.1 or later. See [`licenses/libvips-LICENSE.txt`](licenses/libvips-LICENSE.txt).

### libavif

Copyright 2019 Joe Drago. All rights reserved. See [`licenses/libavif-LICENSE.txt`](licenses/libavif-LICENSE.txt), which also covers code libavif includes from other projects.

### aom

Copyright (c) 2016, Alliance for Open Media. All rights reserved. See [`licenses/aom-LICENSE.txt`](licenses/aom-LICENSE.txt) and [`licenses/aom-PATENTS.txt`](licenses/aom-PATENTS.txt) (Alliance for Open Media Patent License 1.0).

The encoder includes code from three projects aom bundles, each under its own license:

- FAST corner detection, Copyright (c) 2006, 2008 Edward Rosten (BSD-3-Clause). See [`licenses/aom-fastfeat-LICENSE.txt`](licenses/aom-fastfeat-LICENSE.txt).
- vector, Copyright (c) 2016 Peter Goldsborough (MIT). See [`licenses/aom-vector-LICENSE.txt`](licenses/aom-vector-LICENSE.txt).
- x86inc, Copyright (C) 2005-2012 x264 project (ISC), in the amd64 image only. See [`licenses/aom-x86inc-LICENSE.txt`](licenses/aom-x86inc-LICENSE.txt).

### dav1d

Copyright © 2018-2025, VideoLAN and dav1d authors. All rights reserved. See [`licenses/dav1d-COPYING.txt`](licenses/dav1d-COPYING.txt).

### libyuv

Copyright 2011 The LibYuv Project Authors. All rights reserved. See [`licenses/libyuv-LICENSE.txt`](licenses/libyuv-LICENSE.txt) and [`licenses/libyuv-PATENTS.txt`](licenses/libyuv-PATENTS.txt).

### libargparse

Copyright 2017 K. Murray. Licensed under the MIT License. See [`licenses/libargparse-LICENSE.md`](licenses/libargparse-LICENSE.md).

### libwebp

Copyright (c) 2010, Google Inc. All rights reserved. See [`licenses/libwebp-COPYING.txt`](licenses/libwebp-COPYING.txt) and [`licenses/libwebp-PATENTS.txt`](licenses/libwebp-PATENTS.txt).

### oxipng

Copyright (c) 2016 Joshua Holmer. Licensed under the MIT License. See [`licenses/oxipng-LICENSE.txt`](licenses/oxipng-LICENSE.txt).

### pngquant and libimagequant

pngquant © 2009-2018 by Kornel Lesiński. Licensed under the GNU General Public License, version 3 or later. Derived from code by Jef Poskanzer and Greg Roelofs under their original licenses. See [`licenses/pngquant-COPYRIGHT.txt`](licenses/pngquant-COPYRIGHT.txt) and [`licenses/libimagequant-COPYRIGHT.txt`](licenses/libimagequant-COPYRIGHT.txt).

## .NET

| Component | License | Source |
|---|---|---|
| .NET runtime | MIT | <https://github.com/dotnet/runtime> |
| ASP.NET Core | MIT | <https://github.com/dotnet/aspnetcore> |
| Asp.Versioning.Http | MIT | <https://github.com/dotnet/aspnet-api-versioning> |

Copyright (c) .NET Foundation and Contributors. See [`licenses/dotnet-LICENSE.txt`](licenses/dotnet-LICENSE.txt) and [`licenses/asp-versioning-LICENSE.txt`](licenses/asp-versioning-LICENSE.txt). The .NET runtime and ASP.NET Core include further third-party components, listed in their own third-party notices. The image has the notices for the exact version it contains, as `dotnet-runtime-THIRD-PARTY-NOTICES.txt` and `aspnetcore-THIRD-PARTY-NOTICES.txt` in `/usr/share/licenses/image-mason/licenses/`. The current versions are also online:

- <https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>
- <https://github.com/dotnet/aspnetcore/blob/main/THIRD-PARTY-NOTICES.txt>

## Web Interface

Bundled into the web interface's JavaScript, CSS and font files, which are embedded in the application binary.

| Component | Version | License | Source |
|---|---|---|---|
| Vue (`@vue/runtime-dom`, `@vue/runtime-core`, `@vue/reactivity`, `@vue/shared`) | 3.5.44 | MIT | <https://github.com/vuejs/core> |
| Barlow (font, from `@fontsource/barlow`) | 5.3.0 | OFL-1.1 | <https://github.com/jpt/barlow> |
| Barlow Semi Condensed (font, from `@fontsource/barlow-semi-condensed`) | 5.3.0 | OFL-1.1 | <https://github.com/jpt/barlow> |

Vue: Copyright (c) 2018-present, Yuxi (Evan) You. See [`licenses/vue-LICENSE.txt`](licenses/vue-LICENSE.txt).

Barlow and Barlow Semi Condensed: Copyright 2017 The Barlow Project Authors. See [`licenses/barlow-OFL.txt`](licenses/barlow-OFL.txt) and [`licenses/barlow-semi-condensed-OFL.txt`](licenses/barlow-semi-condensed-OFL.txt).

## Base Image and Shared Libraries

The image is built on `mcr.microsoft.com/dotnet/runtime-deps` (Alpine Linux 3.24). These Alpine packages are installed, each under its own license. Versions change as Alpine publishes updates. To list the packages in a specific image:

```bash
docker run --rm --entrypoint apk hendridm/image-mason:<tag> list --installed
```

The license texts are in [`licenses/`](licenses/), taken from each project at the version Alpine 3.24 packages. Licenses shared by several packages (GPL, LGPL, Apache, MPL) are included once, named by license.

| Package | License | License text |
|---|---|---|
| alpine-baselayout, apk-tools, busybox, scanelf, ssl_client | GPL-2.0-only | [`GPL-2.0.txt`](licenses/GPL-2.0.txt) |
| musl, musl-utils | MIT (musl-utils also BSD-2-Clause and GPL-2.0-or-later) | [`musl-COPYRIGHT.txt`](licenses/musl-COPYRIGHT.txt), [`GPL-2.0.txt`](licenses/GPL-2.0.txt) |
| brotli-libs | MIT | [`brotli-LICENSE.txt`](licenses/brotli-LICENSE.txt) |
| expat, libexpat | MIT | [`expat-COPYING.txt`](licenses/expat-COPYING.txt) |
| giflib | MIT | [`giflib-COPYING.txt`](licenses/giflib-COPYING.txt) |
| lcms2 | MIT | [`lcms2-LICENSE.txt`](licenses/lcms2-LICENSE.txt) |
| libeconf | MIT | [`libeconf-LICENSE.txt`](licenses/libeconf-LICENSE.txt) |
| libffi | MIT | [`libffi-LICENSE.txt`](licenses/libffi-LICENSE.txt) |
| alpine-keys, alpine-release | MIT | Alpine's signing keys and release files; the Alpine project publishes no license text for them |
| ca-certificates-bundle | MPL-2.0 AND MIT | [`MPL-2.0.txt`](licenses/MPL-2.0.txt) (the certificates come from Mozilla's list; the MIT part is Alpine's update script, which isn't installed) |
| glib, libblkid, libmount (util-linux), libintl (gettext) | LGPL-2.1-or-later | [`LGPL-2.1.txt`](licenses/LGPL-2.1.txt) |
| libexif | LGPL-2.0-or-later | [`LGPL-2.1.txt`](licenses/LGPL-2.1.txt) |
| libgcc, libstdc++ | GPL-3.0-or-later with the GCC Runtime Library Exception 3.1 | [`GPL-3.0.txt`](licenses/GPL-3.0.txt), [`GCC-exception-3.1.txt`](licenses/GCC-exception-3.1.txt) |
| libcrypto3, libssl3 (OpenSSL), libhwy (Highway) | Apache-2.0 | [`Apache-2.0.txt`](licenses/Apache-2.0.txt) |
| libjpeg-turbo | BSD-3-Clause AND IJG AND Zlib | [`libjpeg-turbo-LICENSE.md`](licenses/libjpeg-turbo-LICENSE.md), [`libjpeg-turbo-README.ijg.txt`](licenses/libjpeg-turbo-README.ijg.txt) |
| libpng | Libpng | [`libpng-LICENSE.txt`](licenses/libpng-LICENSE.txt) |
| pcre2 | BSD-3-Clause | [`pcre2-LICENCE.md`](licenses/pcre2-LICENCE.md) |
| libwebp, libwebp-tools, libwebpmux, libwebpdemux, libsharpyuv | BSD-3-Clause | [`libwebp-COPYING.txt`](licenses/libwebp-COPYING.txt), [`libwebp-PATENTS.txt`](licenses/libwebp-PATENTS.txt) |
| tzdata | Public domain | [`tzdata-LICENSE.txt`](licenses/tzdata-LICENSE.txt) |
| zlib | Zlib | [`zlib-LICENSE.txt`](licenses/zlib-LICENSE.txt) |

## Source Code for GPL and LGPL Components

The corresponding source for every GPL- and LGPL-licensed component in the image is available at no charge:

- **Built from source** (libvips): the exact upstream tag linked above. The build steps are in this repository's `Dockerfile`.
- **Alpine packages** (pngquant, busybox, glib and others): the package build recipe is in [Alpine aports](https://gitlab.alpinelinux.org/alpine/aports), on the branch for the Alpine release in the image (for example `3.24-stable`), under `main/<package>/` or `community/<package>/`. Each `APKBUILD` names the upstream source archive and lists any patches Alpine applies.

If a source link stops working, open an issue at <https://github.com/dmhendricks/image-mason/issues> and the source will be provided.
