# BUILDING.md — Development Environment & Build Guide

This document explains how to set up a development environment for **EmuSync** on Windows and Linux, build every component from source, run the full test suite, and produce the packaged installers.

If anything here is out of date with the actual build behaviour, the GitHub Actions workflows under `.github/workflows/` are the source of truth:

- `.github/workflows/build-and-release.yml` — Windows + Linux release pipeline (tag-triggered).
- `.github/workflows/test-linux.yml` — Linux smoke-build pipeline (manual `workflow_dispatch`).

When you change the build process, update this file as part of the same change.

---

## 1. Repository layout

```
EmuSync/
├── src/
│   ├── EmuSync.sln                       # .NET solution (5 production projects + 4 test projects)
│   ├── Directory.Build.Props             # Shared MSBuild props (suppresses CS8618 across projects)
│   ├── EmuSync.Domain/                   # Core domain entities, helpers, services
│   ├── EmuSync.Services.Managers/        # Business-logic managers (Game, GameSync, SyncSource)
│   ├── EmuSync.Services.Storage/         # Cloud/local storage providers (Google Drive, Dropbox, OneDrive, shared folder)
│   ├── EmuSync.Services.LudusaviImporter/ # Ludusavi manifest import/scan
│   ├── EmuSync.Agent/                    # ASP.NET Core Web API (HTTP API the Electron UI talks to)
│   ├── EmuSync.Domain.Tests/             # xUnit tests for EmuSync.Domain
│   ├── EmuSync.Services.Managers.Tests/  # xUnit tests for EmuSync.Services.Managers
│   ├── EmuSync.Services.Storage.Tests/   # xUnit tests for EmuSync.Services.Storage
│   ├── EmuSync.Agent.Tests/              # xUnit tests for EmuSync.Agent
│   └── EmuSync.UI/                       # Electron + React + TypeScript UI
│       ├── package.json                  # UI build/test/lint scripts
│       ├── .erb/                         # electron-react-boilerplate scaffolding (webpack configs, etc.)
│       ├── src/main/                     # Electron main-process code
│       ├── src/renderer/                 # React renderer code
│       └── release/                      # Build output (created on first build)
├── .github/workflows/                    # CI pipelines
└── README.md                             # End-user documentation
```

There are two distinct deliverables in this repo:

1. **EmuSync.Agent** — a self-contained .NET 10 single-file Web API binary.
2. **EmuSync.UI** — an Electron desktop app written in TypeScript + React.

For EmuSync to function, both must be built; the UI launches the Agent as a child process.

---

## 2. Prerequisites

### 2.1 Required on every platform

| Tool                  | Version           | Notes |
|-----------------------|-------------------|-------|
| **Git**               | any recent 2.x    | for cloning and submodule-like workflows |
| **.NET SDK**          | **10.0.x** (GA)   | release pipeline uses `10.0.x`; every project targets `net10.0`. `.NET 8` SDK is enough to *build* on Linux for smoke-testing (see `test-linux.yml`), but **use 10.0.x** for full local parity. |
| **Node.js**           | **20.x LTS**      | used by the Electron UI |
| **npm**               | bundled with Node 20 | yarn / pnpm are **not** supported by the lockfile |
| **C/C++ build tools** | platform default  | required by `electron-builder install-app-deps` (the `postinstall` step). |

### 2.2 Windows-specific

- **Windows 10/11 x64** (the Agent targets `win-x64`).
- **Visual Studio Build Tools 2022** with the *.NET desktop development* + *ASP.NET and web development* workloads **or** the full **Visual Studio 2022 17.10+** with those workloads. This brings MSVC, the Windows SDK, and the .NET SDK integration.
- **PowerShell 5.1** or **PowerShell 7+**. All examples below use PowerShell syntax for Windows commands.
- Approximately **3 GB** of free disk space for `.NET SDK`, Node modules, and build output.

### 2.3 Linux-specific

- **Ubuntu 22.04+**, **Debian 12+**, **Fedora 38+**, **Arch**, or **SteamOS 3.x** (the Steam Deck). Other modern distros should work but are untested.
- Standard build essentials: `build-essential` (Debian/Ubuntu) or `gcc make` + `python3` (others).
- `libsecret-1-dev`, `libnss3`, `libatk1.0-0`, `libgtk-3-0`, `libxss1`, `libasound2` — runtime libraries commonly required by Electron/Chromium; install via your package manager. (Debian/Ubuntu names shown; substitute per distro.)
- For `linux-x64` packaging the runtime must be x86_64. **ARM64 Linux is not currently supported** by the release pipeline.
- Bash ≥ 4 (the release pipeline uses Bash; the examples below assume Bash on Linux).

### 2.4 Optional tooling

| Tool              | Why you might want it |
|-------------------|-----------------------|
| **Visual Studio Code** | Existing `.vscode/launch.json` configures the Electron main + renderer debug sessions. |
| **Visual Studio 2022** | Opens `src/EmuSync.sln` for F5 debugging the .NET Agent. |
| **Rider / JetBrains Rider** | Works well with the .NET side. |
| **dotnet-format**    | Run with `dotnet format src/EmuSync.sln`. |
| **reportgenerator**  | Convert `coverlet` coverage output to HTML: `dotnet tool install -g dotnet-reportgenerator-globaltool`. |
| **xunit.runner.console** | Useful for running a single test project outside `dotnet test`. |

---

## 3. Getting the source

```bash
git clone https://github.com/emu-sync/EmuSync.git
cd EmuSync
```

The repo has **no submodules** and **no LFS-tracked binaries**. A regular clone is sufficient.

---

## 4. First-time setup (every platform)

The commands below work identically on Windows (PowerShell) and Linux (Bash) — `dotnet` and `npm` are cross-platform.

### 4.1 Restore .NET dependencies

```bash
# From the repo root
dotnet restore src/EmuSync.sln
```

This downloads all NuGet packages referenced by the five production projects and four test projects.

### 4.2 Install UI dependencies

```bash
cd src/EmuSync.UI
npm ci
```

`npm ci` is intentionally used instead of `npm install` — it installs the exact versions pinned in `package-lock.json` and is faster on CI. The `postinstall` script also runs:

- `ts-node .erb/scripts/check-native-dep.js` — fails fast if a native dep version mismatch is detected.
- `electron-builder install-app-deps` — rebuilds native modules against the current Electron ABI.
- `npm run build:dll` — pre-builds the DLL manifest for fast dev rebuilds.

> **Note (Linux only):** if `electron-builder install-app-deps` fails with `libsecret` errors, install `libsecret-1-dev` and rerun. See [§8 Troubleshooting](#8-troubleshooting).

### 4.3 Verify the toolchain

```bash
dotnet --version          # must report 10.x.x
node --version            # must report v20.x
npm --version             # must report 10.x or 11.x (bundled with Node 20)
```

If any of these report an older version, revisit §2 before continuing.

---

## 5. Building

All commands below assume the repo root as the working directory unless an explicit `cd` is shown.

### 5.1 Build everything (debug)

```bash
dotnet build src/EmuSync.sln -c Debug
```

This compiles all production and test projects. Output goes to `src/<Project>/bin/Debug/net10.0/`.

### 5.2 Build everything (release)

```bash
dotnet build src/EmuSync.sln -c Release
```

### 5.3 Publish the Agent as a single-file self-contained binary

This is what the release pipeline does. The result is the executable that the Electron UI launches.

**Windows x64:**

```bash
dotnet publish src/EmuSync.Agent/EmuSync.Agent.csproj \
  -c Release -r win-x64 --self-contained true \
  -o src/EmuSync.UI/release/build/agent/tmp \
  -p:PublishSingleFile=true
# Keep only the exe and appsettings.json
cp src/EmuSync.UI/release/build/agent/tmp/EmuSync.Agent.exe src/EmuSync.UI/release/build/agent/
cp src/EmuSync.UI/release/build/agent/tmp/appsettings.json    src/EmuSync.UI/release/build/agent/
rm -rf src/EmuSync.UI/release/build/agent/tmp
```

> On Windows PowerShell, use `Copy-Item` instead of `cp` if `cp` is not aliased, and `Remove-Item -Recurse -Force` instead of `rm -rf`.

**Linux x64:**

```bash
dotnet publish src/EmuSync.Agent/EmuSync.Agent.csproj \
  -c Release -r linux-x64 --self-contained true \
  -o src/EmuSync.UI/release/build/agent-linux/tmp \
  -p:PublishSingleFile=true
cp src/EmuSync.UI/release/build/agent-linux/tmp/EmuSync.Agent  src/EmuSync.UI/release/build/agent-linux/EmuSync.Agent
cp src/EmuSync.UI/release/build/agent-linux/tmp/appsettings.json src/EmuSync.UI/release/build/agent-linux/appsettings.json
rm -rf src/EmuSync.UI/release/build/agent-linux/tmp
```

After publishing, the produced binary must be **executable** on Linux:

```bash
chmod +x src/EmuSync.UI/release/build/agent-linux/EmuSync.Agent
```

(`-p:PublishSingleFile=true` already sets the executable bit, but chmod makes the intent explicit if the binary is copied around.)

### 5.4 Build the Electron UI (development bundle)

```bash
cd src/EmuSync.UI
npm run build
```

This concurrently runs `build:main` (Electron main process) and `build:renderer` (React app). Output goes to `src/EmuSync.UI/release/app/dist/`. **Do not edit files under `release/` — they are overwritten on every build.**

### 5.5 Package the Electron UI as an installer

These scripts invoke `electron-builder` after `npm run build`. The Agent must already be published into the matching `release/build/agent{,-linux}/` directory first (see §5.3).

**Windows installer (NSIS `.exe`):**

```bash
cd src/EmuSync.UI
$env:RELEASE_VERSION = "0.0.0-local"   # PowerShell; on bash: export RELEASE_VERSION=0.0.0-local
npm run build:win
```

Output: `src/EmuSync.UI/release/build/EmuSync-Win-x64.exe` (or similarly named — see `electron-builder` output for exact name; the CI renames the lone `.exe` to that).

**Linux AppImage:**

```bash
cd src/EmuSync.UI
RELEASE_VERSION=0.0.0-local npm run build:linux
```

Output: `src/EmuSync.UI/release/build/*.AppImage`.

The CI then zips the AppImage together with the published Agent binary and the icon into `EmuSync-Linux-x64.zip`. To replicate locally:

```bash
cd src/EmuSync.UI/release/build
ART="0.0.0-local"
mkdir -p "$ART/agent"
cp *.AppImage            "$ART/EmuSync.AppImage"
cp ../../assets/icon.png "$ART/emu-sync-icon.png"
cp ../agent-linux/EmuSync.Agent     "$ART/agent/"
cp ../agent-linux/appsettings.json  "$ART/agent/"
zip -r "EmuSync-Linux-x64.zip" "$ART"
```

### 5.6 Clean build

```bash
# .NET
dotnet clean src/EmuSync.sln -c Debug
dotnet clean src/EmuSync.sln -c Release

# UI (electron-react-boilerplate clean script)
cd src/EmuSync.UI && npm run package   # also wipes release/ via .erb/scripts/clean.js
```

Or, for a fully manual nuke:

```bash
find src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
rm -rf src/EmuSync.UI/release src/EmuSync.UI/dist
```

---

## 6. Running

### 6.1 Run the .NET Agent directly (standalone)

```bash
dotnet run --project src/EmuSync.Agent -c Debug
```

By default the Agent binds to the URLs configured in `src/EmuSync.Agent/Properties/launchSettings.json`. Open `http://localhost:<port>/swagger` (if Swagger is registered) or whatever the launch profile indicates.

### 6.2 Run the Electron UI in development mode (with hot reload)

```bash
cd src/EmuSync.UI
npm start
```

This:

1. Starts the webpack dev server for the renderer.
2. Compiles the main process.
3. Launches Electron with `electronmon` watching the main bundle.

Use VS Code's *Electron: All* compound (defined in `src/EmuSync.UI/.vscode/launch.json`) to attach the Chrome DevTools to the renderer.

### 6.3 Run a "production-like" UI locally

After completing §5.3, §5.4, and §5.5 for your platform, the produced installer can be run; the AppImage simply needs to be made executable:

```bash
chmod +x src/EmuSync.UI/release/build/*.AppImage
./src/EmuSync.UI/release/build/EmuSync.AppImage
```

On Windows, launch `src/EmuSync.UI/release/build/EmuSync-Win-x64.exe`.

---

## 7. Testing

### 7.1 .NET unit tests (xUnit)

The solution contains four test projects, all using xUnit + Moq + coverlet:

- `src/EmuSync.Domain.Tests`
- `src/EmuSync.Services.Managers.Tests`
- `src/EmuSync.Services.Storage.Tests`
- `src/EmuSync.Agent.Tests`

**Run all tests in the solution:**

```bash
dotnet test src/EmuSync.sln -c Debug --nologo
```

**Run a single test project:**

```bash
dotnet test src/EmuSync.Domain.Tests/EmuSync.Domain.Tests.csproj -c Debug --nologo
```

**Run a single test by name:**

```bash
dotnet test src/EmuSync.sln --filter "FullyQualifiedName~ZipHelper"
```

**Collect code coverage (coverlet):**

```bash
dotnet test src/EmuSync.sln -c Debug \
  /p:CollectCoverage=true \
  /p:CoverletOutput=./TestResults/coverage/ \
  /p:CoverletOutputFormat=cobertura
```

To turn `cobertura.xml` into a browsable HTML report:

```bash
reportgenerator \
  -reports:./TestResults/coverage/coverage.cobertura.xml \
  -targetdir:./TestResults/coverage/html \
  -reporttypes:Html
```

### 7.2 Electron UI tests (Jest)

```bash
cd src/EmuSync.UI
npm test
```

Jest is configured (see the `"jest"` block in `package.json`) to:

- Resolve modules from `node_modules`, `release/app/node_modules`, and `src`.
- Run in a `jsdom` test environment at `http://localhost/`.
- Transform TypeScript and JavaScript via `ts-jest`.
- Ignore the build output (`release/app/dist`, `.erb/dll`).
- Require `release/app/dist/main.prod.js` to exist (`.erb/scripts/check-build-exists.ts`). If you have not yet built the UI, Jest will refuse to start — run `npm run build` first.

**Useful variants:**

```bash
npm test -- --watch                       # re-run on file change
npm test -- --coverage                    # emit ./coverage
npm test -- SomeComponent.test.tsx        # run a single file
npm test -- -t "renders the header"       # run by test name pattern
```

### 7.3 Static analysis

**UI lint (ESLint):**

```bash
cd src/EmuSync.UI
npm run lint          # check
npm run lint:fix      # autofix
```

**UI type-check (TypeScript):**

```bash
cd src/EmuSync.UI
npm run type-check
```

**.NET format:**

```bash
dotnet format src/EmuSync.sln
```

The repository ships no `.editorconfig` enforcement beyond the one at `src/EmuSync.UI/.editorconfig`; treat `dotnet format` warnings as advisory unless the project grows a formatting gate.

---

## 8. Troubleshooting

### 8.1 `dotnet build` fails with "NETSDK1045: The current .NET SDK does not support targeting .NET 10"

You have an older SDK installed. Install the **.NET 10 SDK** (GA) from <https://dotnet.microsoft.com/download/dotnet/10.0> and ensure `dotnet --version` reports `10.x`. If multiple SDKs are installed, add a `global.json` to the repo root with `"sdk": { "version": "10.0.x" }`.

### 8.2 `npm ci` fails: `gyp ERR! find Python`

Install Python 3 and a working C/C++ toolchain. On Debian/Ubuntu:

```bash
sudo apt-get install -y python3 build-essential
```

On Windows, reinstall the **Node.js** workloads via Visual Studio Installer (or install `windows-build-tools` if you must use an older Node).

### 8.3 `electron-builder install-app-deps` fails on Linux with `libsecret-1` / `nss` / `atk` / `gtk` errors

Install the runtime libraries Electron requires:

```bash
sudo apt-get install -y libsecret-1-dev libnss3 libatk1.0-0 libatk-bridge2.0-0 \
                        libgtk-3-0 libxss1 libasound2 libgbm1
```

On Fedora / SteamOS substitute `dnf` package names accordingly. Then re-run `npm ci` inside `src/EmuSync.UI`.

### 8.4 Agent binary won't start on Linux: `Permission denied`

The published binary must be executable. Either rely on `-p:PublishSingleFile=true` (which sets the bit) or:

```bash
chmod +x src/EmuSync.UI/release/build/agent-linux/EmuSync.Agent
```

### 8.5 Agent exits immediately with "no listening URLs configured"

You launched the Agent directly outside of the Electron host. Edit `src/EmuSync.Agent/Properties/launchSettings.json` to set `applicationUrl`, or pass `--urls` when running manually:

```bash
dotnet run --project src/EmuSync.Agent -- --urls "http://127.0.0.1:5000"
```

When the UI launches the Agent itself it provides the URLs via environment / arguments.

### 8.6 `npm start` fails: "Address already in use" on the renderer dev server

The script `.erb/scripts/check-port-in-use.js` will detect this and abort. Either close the offending process or change the dev server port in `.erb/configs/webpack.config.renderer.dev.ts`.

### 8.7 `npm test` fails: "main.prod.js does not exist"

Jest requires the Electron main bundle. Run once:

```bash
cd src/EmuSync.UI
npm run build:main
```

### 8.8 Line-ending issues on Windows

If you see a wall of `CRLF` warnings from `dotnet format` or git complains on commit, normalise once with:

```bash
git config core.autocrlf input     # in the repo root, run once
git rm --cached -r .
git reset --hard
```

(Or rely on a `.gitattributes` file if/when one is added — currently there is none.)

### 8.9 Self-signed certificate or SmartScreen warnings on Windows

When launching the locally-built installer, Windows SmartScreen will warn that the binary is unsigned. This matches the production behaviour described in `README.md`; click **More info → Run anyway** for local testing.

### 8.10 `dotnet test` discovers zero tests

Confirm the solution built successfully first (`dotnet build src/EmuSync.sln`). If only one test project is affected, verify its `<TargetFramework>` matches the SDK (`net10.0`) and that `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` are restored.

---

## 9. Where build artifacts land

| Artifact | Path |
|----------|------|
| .NET build output (per project) | `src/<Project>/bin/<Config>/net10.0/` |
| .NET publish output (single-file) | `src/EmuSync.UI/release/build/agent/` (Windows) or `src/EmuSync.UI/release/build/agent-linux/` (Linux) |
| UI webpack output (dev & prod) | `src/EmuSync.UI/release/app/dist/` |
| Windows installer | `src/EmuSync.UI/release/build/EmuSync-Win-x64.exe` |
| Linux AppImage | `src/EmuSync.UI/release/build/*.AppImage` |
| Linux distributable zip | `src/EmuSync.UI/release/build/<version>/EmuSync-Linux-x64.zip` |
| Test results (TRX) | wherever `dotnet test` writes them (default `./TestResults/`) — pass `--logger "trx;LogFileName=results.trx"` for explicit paths |
| Coverage | `./TestResults/coverage/` (when collecting with coverlet, see §7.1) |

A successful build is verifiable by:

1. `dotnet build src/EmuSync.sln -c Release` exits 0 with **0 errors**.
2. `dotnet test src/EmuSync.sln` reports all tests passed.
3. `npm run build` in `src/EmuSync.UI` produces `release/app/dist/main.prod.js` and `release/app/dist/renderer.prod.js` without errors.
4. `npm run type-check` exits 0.

---

## 10. End-to-end smoke test (manual)

After completing §5.3, §5.4, §5.5 for your platform:

1. Install the produced installer (or run the AppImage directly).
2. Launch EmuSync.
3. Confirm the Agent process is running (Task Manager / `ps`) and the UI loads.
4. Link a storage provider (Google Drive / Dropbox / OneDrive / shared folder).
5. Add a game with a known save folder.
6. Trigger a manual sync and confirm the file appears in the linked provider.

If any step fails, capture the Agent log output (Serilog) from the install directory before opening an issue.

---

## 11. See also

- `README.md` — end-user installation and usage.
- `.github/workflows/build-and-release.yml` — Windows + Linux release pipeline.
- `.github/workflows/test-linux.yml` — Linux manual-build workflow.
- `src/EmuSync.sln` — the .NET solution.
- `src/EmuSync.UI/package.json` — every UI script (`scripts` block).