#!/usr/bin/env bash
set -euo pipefail
dotnet run --project ./src/IreiMvp.Web/IreiMvp.Web.csproj --urls http://localhost:5078
