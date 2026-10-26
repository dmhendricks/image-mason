# Security Policy

## Supported Versions

Security fixes are made in the latest release only. Use the `latest` tag, or the newest `major.minor` tag, to receive them.

## Reporting a Vulnerability

Do not report security vulnerabilities in public issues.

Report them privately through [GitHub's vulnerability reporting](https://github.com/dmhendricks/image-mason/security/advisories/new). Include the affected version, steps to reproduce, and the impact you expect.

You should receive a reply within a week. Once a fix is released, the advisory is published with credit to the reporter, unless you prefer otherwise.

## Scope

Image Mason is designed for private networks and trusted callers. Behavior described in the README's [Security](README.md#security) section is by design. For example, URL fetching reaches private addresses unless `IMAGE_MASON_URL_ALLOWED_HOSTS` restricts it, and stored image URLs are readable by anyone who has them.

Vulnerabilities in the bundled image tools (libjxl, jpegli, mozjpeg, libvips, libavif, aom, dav1d, libwebp, oxipng, pngquant) should also be reported to their projects. Let us know as well, so the image can be updated.
