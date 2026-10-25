# Contributing

Bug reports, suggestions and pull requests are welcome.

## Reporting Issues

Open an [issue](https://github.com/dmhendricks/image-mason/issues) with:

- The image tag, or the `build.version` from `GET /health`.
- The request you sent (parameters and image size and dimensions), and the response status and body.
- The container logs around the request. Set `IMAGE_MASON_LOG_LEVEL=Debug` for more detail.
- A sample image, if the problem depends on one and you can share it.

Report security vulnerabilities privately instead; see [SECURITY.md](SECURITY.md).

## Pull Requests

For anything larger than a small fix, open an issue first so we can agree on the approach.

1. Fork the repository and create a branch from `main`.
2. Make your change, with tests where they make sense.
3. Run the checks below.
4. Add a line to the `Unreleased` section of [CHANGELOG.md](CHANGELOG.md) if users would notice the change.
5. Open a pull request that describes what changed and why.

### Checks

CI runs the same checks on every pull request:

```bash
dotnet test --solution ImageMason.slnx -c Release
shellcheck scripts/*.sh
(cd src/web && npm ci && npm run lint && npm test && npm run build && npm run size)
docker build -t image-mason:dev . && scripts/smoke-test.sh image-mason:dev
```

The build treats warnings as errors and enforces the code style in [.editorconfig](.editorconfig).

### Guidelines

- Keep the API backward compatible within a major version. Breaking changes go in a new API version.
- Settings are `IMAGE_MASON_*` environment variables. Document new ones in [.env.sample](.env.sample) and
  the README.
- Keep the image tools as separate programs, and add any new third-party component to
  [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) with its license in [licenses/](licenses/).
- Pin new dependencies to their latest stable version. Packages bundled into the web interface count as
  third-party components too.

## Sign-Off

Contributions are accepted under the [Apache License 2.0](LICENSE). Sign off each commit to certify that
you wrote it or otherwise have the right to submit it, as described in the
[Developer Certificate of Origin](https://developercertificate.org/):

```bash
git commit -s
```

This adds a `Signed-off-by` line with your name and email to the commit message.
