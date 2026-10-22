#!/usr/bin/env bash
# Local verification: build, unit tests, integration tests. Functional tests need a
# reachable SQL Server and are run by .github/workflows/functional-tests.yml.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== Restauration =="
dotnet restore Mondje.slnx

echo "== Compilation (Release) =="
dotnet build Mondje.slnx --configuration Release --no-restore

echo "== Tests du domaine =="
dotnet test tests/Domain.UnitTests/Domain.UnitTests.csproj \
  --configuration Release --no-build

echo "== Tests de la couche Application =="
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj \
  --configuration Release --no-build

echo "== Tests d'integration Infrastructure =="
dotnet test tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj \
  --configuration Release --no-build

echo
echo "Verification locale terminee."
echo "Pour les tests fonctionnels : docker compose up -d sqlserver && \\"
echo "  ConnectionStrings__MondjeDb='Server=localhost,1433;Database=MondjeDb_Test;User Id=sa;Password=Mondje2026!;TrustServerCertificate=True' \\"
echo "  dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj"
