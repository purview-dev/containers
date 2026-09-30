# Packaging

Every project under `src/src` is packable and ships as `Purview.WslContainers.*`; test and spike projects
never pack. Packages are produced into `./artifacts` with a matching `.snupkg`.

## What a package contains

Each package ships exactly:

| Entry | Source |
| --- | --- |
| `lib/$(TFM)/Purview.WslContainers[.<Module>].dll` | The built assembly (`net11.0-windows10.0.19041`). |
| `lib/$(TFM)/Purview.WslContainers[.<Module>].xml` | XML documentation, generated because `GenerateDocumentationFile` is on for packable projects. |
| `README.md` | The package's bespoke `Sdk/README.md` (see below). |
| `purview-logo-light.png` | The shared Purview package icon (`assets/images/purview-logo-light.png`). |

Portable PDBs are delivered through the `.snupkg`, never inside the `.nupkg`.

## The `Sdk/` folder convention

`Purview.BuildSdk` packs a packable project's `Sdk/` folder automatically (`PurviewAutoSdkPack`):

| Source path | Package path |
| --- | --- |
| `Sdk/*.md`, `Sdk/*.png`, `Sdk/*.svg`, … | package root |
| `Sdk/build/**`, `Sdk/buildTransitive/**`, `Sdk/buildMultiTargeting/**` | the matching package folder |
| `Sdk/**` (anything else) | `Sdk/**` |

Two consequences matter for this repository:

1. **Each packable project owns its documentation** in `src/src/<Project>/Sdk/README.md`. Because a
   README-named file is already being packed, the SDK leaves the repository-root `README.md` out of the
   package, so every package ships documentation specific to itself.
2. **The icon is linked, not copied.** `src/Directory.Build.props` adds the shared logo as a `None` item
   with `Link="Sdk/purview-logo-light.png"`, and the SDK packs linked `Sdk/` files at the package root and
   declares `<PackageIcon>`/`<PackageReadmeFile>`. Projects must therefore keep
   `<IsPackable>true</IsPackable>` literally in the `.csproj`: the SDK resolves `IsPackable` by scanning the
   project file, and both property groups are conditioned on it.

## Pack validation

`just pipeline-pack-validate` runs the shared `Purview.Build` pipeline with `Build:RunPack=true`,
`Build:ValidatePack=true` and `Release:Mode=None`. `purview-build.json` configures the validation:

- `RequireSymbolPackage` / `RequireSymbolFiles` — every `.nupkg` needs a matching `.snupkg`, and every
  `.snupkg` must contain at least one `.pdb`.
- `RequiredContent` — because `RequireExplicitContent` defaults to `true`, this map is the **exhaustive**
  declaration of every package's contents: a produced package with no matching rule fails, and any packed
  entry not matched by one of that package's globs fails. `$(TFM)` expands to each target framework the
  package actually ships, so the rules are satisfied per framework.
- Additional checks always run: file names must match the nuspec id/version, and `.nupkg` files must not
  contain PDBs outside `tools/`/`analyzers/`.

Adding a packable project therefore means adding a `RequiredContent` entry keyed by its lowercase package id
alongside the `Sdk/README.md`. Removing content from a package means removing the corresponding glob.

## Adding a package

1. Create `src/src/<Name>/<Name>.csproj` with `<IsPackable>true</IsPackable>`, an assembly/package id, a
   description and tags, and add it to `src/WSLTestContainers.slnx`.
2. Add `Sdk/README.md` documenting the package (see the existing modules for the shape).
3. Add the package to `RequiredContent` in `purview-build.json`.
4. Prove it with `just pack` and `just pipeline-pack-validate`.

## Related

- [Release Flow](Release-Flow.md) — how packages are versioned and published.
- [Contributing Modules](Contributing-Modules.md) — the module contract a new package follows.
