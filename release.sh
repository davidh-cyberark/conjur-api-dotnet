#!/usr/bin/env bash
set -euo pipefail

docker run \
  -v "$PWD":/src:ro \
  -v "$ASSET_DIR":/artifacts:ro \
  -e WRITE_ARTIFACTORY_USERNAME \
  -e WRITE_ARTIFACTORY_PASSWORD \
  -e WRITE_ARTIFACTORY_URL \
  -e NUGET_API_KEY \
  --entrypoint "/release.sh" \
  dotnet-builder:dev
