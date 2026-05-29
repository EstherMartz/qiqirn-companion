# Qiqirn Companion

A Dalamud plugin for FFXIV. Browse open FC craft projects and claim tasks from
in-game, run trading queries, search items, and see what you can craft from your
current inventory with live Marketboard prices.

## Installing (guild mates — read this)

1. In-game, open the Dalamud settings: type `/xlsettings`.
2. Go to the **Experimental** tab.
3. Under **Custom Plugin Repositories**, paste this URL into the empty box and
   click the **+** button:

   ```
   https://raw.githubusercontent.com/EstherMartz/qiqirn-companion/main/repo.json
   ```

4. Click **Save and Close** (bottom right).
5. Open the plugin installer: `/xlplugins`.
6. Search for **Qiqirn Companion** and click **Install**.
7. Open it in-game with `/qiqirn`.

Updates are automatic — when a new version is released, Dalamud will offer it in
the plugin installer.

### First-time setup

Open the plugin (`/qiqirn`), then the config (gear icon / `/xlsettings` entry):

- **Guild ID** — your Discord server ID (right-click the server icon in Discord →
  *Copy Server ID*). Needed for the Projects tab.
- **Home World** — e.g. `Phantom`. Needed for home-scope trading presets.

## Releasing a new version (maintainer)

Releases are fully automated by GitHub Actions. To cut one:

1. Commit your changes to `main` and push.
2. Tag the release and push the tag — the tag **is** the version:

   ```sh
   git tag v1.1.0.0
   git push origin v1.1.0.0
   ```

The [release workflow](.github/workflows/release.yml) then:

- syncs the version into `QiqirnCompanion.json` and `repo.json`,
- builds against the live Dalamud distribution,
- publishes a GitHub Release with `latest.zip` attached,
- commits the version bump back to `main`.

Within a few minutes, everyone's Dalamud sees the update.

> The download links in `repo.json` always point at
> `releases/latest/download/latest.zip`, so they never need editing — only the
> `AssemblyVersion` changes, and the workflow handles that.

## Building locally

Requires the .NET 10 SDK and XIVLauncher installed (the project references the
Dalamud assemblies from `%AppData%\XIVLauncher\addon\Hooks\dev`).

```sh
dotnet build --configuration Release
```

A Release build auto-deploys the DLL into `%AppData%\XIVLauncher\devPlugins\QiqirnCompanion`
so `/xldev` → **Reload** picks it up immediately, and produces
`bin/Release/net10.0-windows/latest.zip` for distribution.
