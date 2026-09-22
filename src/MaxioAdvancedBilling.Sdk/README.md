# Maxio Advanced Billing .NET SDK (vendored)

This directory is a **vendored copy** of the APIMatic-generated Maxio Advanced Billing .NET SDK.

- Source: <https://github.com/context-plugins/maxio-csharp-sdk> (branch `main`, API spec `1.0`).
- Root namespace: `MaxioAdvancedBilling`; client class: `MaxioAdvancedBillingClient`.
- Target framework: `netstandard2.0`.

## Why it is vendored

The SDK is **not published to any NuGet feed**, so it must be built from source and referenced as a
project. Vendoring keeps the build reproducible and offline: `src/Infrastructure` references this project
directly (`ProjectReference`).

## Do not hand-edit

These files are generated. If the SDK needs to change, regenerate/re-vendor from the upstream repo rather
than editing here. Only `Directory.Build.props` and this `README.md` are local additions (they scope the
host repo's Central Package Management and analyzer settings away from the generated code — see the props
file for details). The `map/` documentation and `api-reference.md` from upstream are intentionally omitted.
