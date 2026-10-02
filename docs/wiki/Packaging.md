# Packaging

Every project under `src/src` is packable and ships as `Purview.Containers.*`; test projects
never pack. Packages are produced into `./artifacts` with a matching `.snupkg`.

## What a package contains

Each package ships exactly:

| Entry | Source |
| --- | --- |
| `lib/$(TFM)/Purview.Containers.*.dll` | The built assembly: `net10.0` for `Purview.Containers` (umbrella), `Purview.Containers.Core` and the service modules; `Purview.Containers.Wsl` ships `net10.0` (the portable facade) and `net10.0-windows10.0.19041` (the implementation). |
| `lib/$(TFM)/<Assembly>.xml` | XML documentation, generated because `GenerateDocumentationFile` is on for packable projects. |
| `README.md` | The package's bespoke `Sdk/README.md` (see below). |
| `purview-logo-light.png` | The shared Purview package icon (`assets/images/purview-logo-light.png`). |
| `buildTransitive/Purview.Containers.Core.props` / `.targets` | **Core (abstractions) package.** Declares the `PurviewContainersBackends` list and turns it into a generated backend initializer in the consuming assembly. |
| `buildTransitive/Purview.Containers.Wsl.props` / `.targets` | **WSL Containers backend only.** Defaults `WindowsSdkPackageVersion`/`PlatformTarget` and raises `PCC0001`/`PCC0002` for unsupported consumers. |
| `buildTransitive/Purview.Containers.Docker.props` | **Docker backend only.** Adds the Docker backend to the `PurviewContainersBackends` list. |
| `payload/win-x64/*`, `payload/win-arm64/*` | **WSL Containers backend only.** The Windows implementation, the WSLC projection, its Windows SDK dependencies and the native SDK. The `net10.0` facade loads them at run time on a Windows host; the `buildTransitive` targets copy the matching folder to the consumer output. |

Portable PDBs are delivered through the `.snupkg`, never inside the `.nupkg`. The umbrella
`Purview.Containers` package has no `buildTransitive` of its own: its dependencies' assets flow
transitively.

`$(TFM)` is expanded per shipped framework: `Purview.Containers`, `Purview.Containers.Core`,
`Purview.Containers.Docker` and the service modules ship `lib/net10.0/`; `Purview.Containers.Wsl` ships
both `lib/net10.0/` (the facade) and `lib/net10.0-windows10.0.19041/` (the implementation), with the
`payload/` folders alongside. Every package therefore restores on a portable `net10.0` project. Consumers
must target .NET 10+; see [Consumer Requirements](Consumer-Requirements.md).

## Package metadata

Metadata is split by where its source of truth lives:

| Metadata | Source |
| --- | --- |
| `id`, `description`, `tags`, licence, authors | each project's `.csproj` |
| `version`, `repository`, `bugs` | `package.json` (applied by `Purview.BuildSdk`) |
| icon, package README, **project site** | `src/Directory.Build.props`, for every packable project |

The **project site** (`PackageProjectUrl`) is `https://github.com/purview-dev/containers`, the
repository that hosts this documentation wiki; it shows as *Project Site* on nuget.org. Repository and
commit metadata come from SourceLink, so the `repository` entry in a locally packed `.nupkg` points at
the branch and commit that produced it.

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
   description and tags, and add it to `src/Containers.slnx`.
2. Add `Sdk/README.md` documenting the package (see the existing modules for the shape).
3. Add the package to `RequiredContent` in `purview-build.json`.
4. Prove it with `just pack` and `just pipeline-pack-validate`.

> **Packaging a backend or a new MSBuild asset?** NuGet only auto-imports `buildTransitive/<PackageId>.props`
> and `buildTransitive/<PackageId>.targets`, so an asset named after anything else — including a shorter
> product name — is shipped but never applied. A regression here is invisible to the build of this
> repository, because our own projects reference each other by project and never import these assets;
> `just verify-consumers` is the check that catches it.

The `Sdk/buildTransitive/` assets are per package, and NuGet only auto-imports a file named after its own
package id (`buildTransitive/<PackageId>.props|targets`). The abstractions package ships the backend
registration, each backend package ships its own registration entry (and, for the WSL backend, the
consumer defaults and guards), and the service modules ship none — a module is backend-neutral, so a
consumer adds a backend package explicitly. They are declared in `RequiredContent` like any other asset,
so a package that starts or stops shipping MSBuild assets has to update that manifest — a produced
package with no rule, or a packed entry matched by no glob, fails validation.

## Related

- [Release Flow](Release-Flow.md) — how packages are versioned and published.
- [Contributing Modules](Contributing-Modules.md) — the module contract a new package follows.
