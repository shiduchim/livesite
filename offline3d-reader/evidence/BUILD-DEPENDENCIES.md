# Build dependencies and source inspection

The supplied ClickTo3D project was inspected. Its shared source uses per-year configurations: Revit 2024/net48, 2025–2026/net8 and 2027/net10. It builds against installed Revit assemblies and its reinstall script deploys a versioned bundle. That script removes broadly matched old ClickTo3D/EK3D registrations and elevates; this prototype deliberately uses a separate identity and per-user manifests, and never performs that cleanup.

A legitimate published **build reference** route is available: Nice3point's `revit-api` project provides separate NuGet reference assemblies. Exact packages inspected and pinned:

| Revit | RevitAPI + RevitAPIUI package version | Reference framework |
| --- | --- | --- |
| 2024 | 2024.0.0 | net48 |
| 2025 | 2025.0.2 | net8.0 |
| 2026 | 2026.0.4 | net8.0 |
| 2027 | 2027.0.0 | net10.0-windows |

The embedded package `License.md` files were read and are included in `licenses/Revit-build-references/`. They declare MIT for these packages. This is not a claim that the Revit product/runtime is MIT. Package contents are reference-only assemblies under `ref/`, used only for compilation with `PrivateAssets=all` and `IncludeAssets=compile;build`. **No RevitAPI.dll or RevitAPIUI.dll is in the delivered application payload.** Revit supplies its own runtime assemblies under the recipient's licensed installation.

The temporary Windows build option instead automatically uses DLLs from the detected licensed local Revit installation. The Microsoft .NET Framework reference-assemblies package supplies the net48 targeting reference pack, so no Visual Studio/targeting-pack installation is needed. A compatible .NET 10 SDK is detected or, with explicit consent, obtained as a private Microsoft ZIP with a pinned official SHA-512. It does not change PATH. `scripts/dependencies.json` records the URL/hash.

Current Autodesk update guidance requires runtime detection within a year: **2025.5 and 2026.5 changed to .NET 10**. Additional net10 builds for both years compile against the pinned baseline API reference shapes; these are not runtime-tested compatibility claims. The local build fallback compiles against the recipient's actual API assemblies.

Sources:
- Nice3point source and package purpose: https://github.com/Nice3point/revit-api
- Versioned 2024 package: https://www.nuget.org/packages/Nice3point.Revit.Api.RevitAPI/2024.0.0
- Autodesk runtime transition: https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Revit--Requirements-for-products-affected-by-the-Microsoft--NET-10-transition.html
- Revit 2027 SDK/.NET 10: https://blog.autodesk.io/revit-2027-sdk-net-10-api-changes-and-additions/
- Microsoft SDK release metadata: https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
- Microsoft WebView2 distribution/detection: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution

The viewer can use either a locally bundled Fixed Runtime or an installed Evergreen Runtime. This package contains the .NET runtime, PDFium, WebView2 SDK loader, local engine scripts and shaders; it does not contain the full WebView2 browser runtime. A missing Evergreen Runtime is installed from Microsoft's official signed bootstrapper after user consent. The build environment's network policy rejected the Microsoft bootstrapper download (HTTP 403), so that dependency's download/installation could not be tested here. Shared WebView2 is retained on uninstall.
