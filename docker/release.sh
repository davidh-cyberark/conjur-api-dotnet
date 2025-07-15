#!/usr/bin/env bash
set -euo pipefail

cd /artifacts

dotnet nuget add source "https://$WRITE_ARTIFACTORY_URL/artifactory/api/nuget/conjur-api-dotnet" --name "conjur-api-dotnet" \
     --username "$WRITE_ARTIFACTORY_USERNAME" --password "$WRITE_ARTIFACTORY_PASSWORD" --store-password-in-clear-text

dotnet nuget push conjur-api.*.nupkg --source "conjur-api-dotnet"

if [ "$NUGET_API_KEY" != "" ]; then
  dotnet nuget push conjur-api.*.nupkg --source "https://api.nuget.org/v3/index.json" -k "$NUGET_API_KEY"
fi
