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

## Self-hosted GitHub Actions runner

The workflow deliberately builds only on a self-hosted Windows x64 runner so reference DLLs remain on the local machine. In the repository settings, open Actions, then Runners, add a Windows x64 runner, and assign the custom label `heels-build`. Install and run it using the commands GitHub generates for that repository.

Create a repository Actions variable named `REFERENCE_ROOT`. Its value must be an absolute directory path visible to the Windows account running the runner service. That path is passed to MSBuild; the workflow never uploads the reference directory.

Branch pushes and manual workflow runs build both packages. Pushing a tag such as `v1.0.0` builds the packages and creates a GitHub Release with generated notes. The tag version must match the plugin version, otherwise the build stops before publishing.

For safety, pull requests do not directly execute on the self-hosted runner. Review external contributions before merging or manually running their code on the machine.
