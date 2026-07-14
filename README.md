# HeelsSettings

HeelsSettings is a BepInEx plugin for Koikatu (KK) and Koikatsu Sunshine (KKS). It provides a focused editor for symmetric heel-related ABMX rotations and height offsets in Maker and Studio, while storing compatible rules in the BonerStateSync data format.

## Requirements

The runtime requires BepInEx 5, KKAPI or KKSAPI, and KKABMX 5.0 or newer. The build additionally needs the matching game and plugin reference assemblies. Those assemblies are intentionally excluded from Git and from release packages.

The reference root must contain the existing `kk` and `kks` directory layouts used by the two project files. By default MSBuild reads them from `dlls` at the repository root. A different location can be supplied through the `ReferenceRoot` MSBuild property.

## Local build and packaging

PowerShell 7 and a compatible .NET SDK are required. The shared script restores, builds with warnings treated as errors, and creates installation-ready archives containing `BepInEx/plugins/<plugin>.dll`.

```powershell
.\scripts\Build-Release.ps1 `
  -ReferenceRoot C:\path\to\reference-dlls `
  -Version 1.0.0
```

The release version must match `HeelsPlugin.PluginVersion`. Development packages can use `dev` or a value beginning with `ci-`. Generated archives are written under `artifacts` and are ignored by Git.

## Manual GitHub Release

Reference DLLs remain local and GitHub Actions does not build this project. Run the packaging script locally, review the two archives under `artifacts`, then create and push a version tag. The tag version and package version must match `HeelsPlugin.PluginVersion`.

```powershell
git tag v1.0.0
git push origin v1.0.0

gh release create v1.0.0 `
  artifacts\KK_HeelsSettings-v1.0.0.zip `
  artifacts\KKS_HeelsSettings-v1.0.0.zip `
  --verify-tag --generate-notes --title v1.0.0
```

This publishes only the two installation archives. Game assemblies and build references remain outside the repository and are never uploaded.
