# Repository Guidelines

## Project Structure & Module Organization

The .NET 11 solution is rooted at `IndustrialEnergyTelemetry.slnx`. Runtime services live under `src/`: OPC UA server/client simulators, Kafka processor and inserter workers, the ASP.NET Core API, and shared `Telemetry.Contracts`, `Telemetry.Domain`, and `BuildingBlocks` projects. The React 19/Vite dashboard is in `frontend/src`. Tests are split into `tests/Telemetry.UnitTests`, `Telemetry.IntegrationTests`, and `Telemetry.EndToEndTests`. Deployment assets live in `infrastructure/` (Docker, PostgreSQL, observability, and Helm); architecture and operations notes live in `docs/`.

## Build, Test, and Development Commands

- `dotnet restore IndustrialEnergyTelemetry.slnx` restores .NET packages.
- `dotnet build IndustrialEnergyTelemetry.slnx --no-restore` compiles all projects; warnings fail the build.
- `dotnet test IndustrialEnergyTelemetry.slnx --no-build --filter "Category!=EndToEnd"` runs unit and integration tests. Integration tests require a Docker-compatible daemon.
- `cd frontend && npm ci && npm run typecheck && npm run lint && npm run build` installs locked dependencies and validates the dashboard.
- `cd frontend && npm run dev` starts the Vite development server.
- `docker compose up --build` runs the default local stack; add `--profile full-demo` for all simulated sites.

## Coding Style & Naming Conventions

Follow existing C# conventions: four-space indentation, file-scoped namespaces, nullable reference types, `PascalCase` for types/public members, and `camelCase` for locals and parameters. Keep shared domain rules in `Telemetry.Domain` and transport shapes in `Telemetry.Contracts`. TypeScript/TSX uses two-space indentation, semicolons, `PascalCase` components, and `camelCase` functions. Run ESLint before submitting frontend changes. Do not commit generated `bin/`, `obj/`, `dist/`, or `node_modules/` content.

## Testing Guidelines

Tests use xUnit; integration tests use Testcontainers with real Kafka and PostgreSQL. Name test classes after the subject (for example, `TelemetryValidatorTests`) and test methods as readable behavioral statements. Add focused unit tests for domain logic and integration tests for storage or messaging boundaries. E2E tests require the full Compose stack; follow the command in `README.md`. No coverage threshold is enforced, but changed behavior should be exercised.

## Commit & Pull Request Guidelines

Recent history favors short imperative subjects and Conventional Commit prefixes such as `feat:`, `fix:`, and `perf:`. Keep commits scoped to one coherent change. Pull requests should explain the user-visible effect, list verification commands, link relevant issues, and call out configuration or migration changes. Include screenshots for dashboard changes and update architecture or deployment docs when behavior changes.

## Security & Configuration

Never commit credentials, certificates, or production connection strings. Treat Compose defaults as development-only; the API lacks end-user authentication, and production deployments must enable transport security and managed secrets.
