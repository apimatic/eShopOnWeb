# third-party

Vendored source of the **PayPal Server SDK for .NET** (`third-party/paypal-csharp-sdk`).

The SDK is not published to a package feed, so it is built from source and referenced as a project
(`src/Infrastructure/Infrastructure.csproj` → `ProjectReference`). Upstream:
<https://github.com/context-plugins/paypal-csharp-sdk> (branch `main`).

Generated code — do not hand-edit. To update, re-clone upstream and replace the
`Api/ Core/ Errors/ Models/ Servers/` directories, the root `*.cs` files and `PayPal.csproj`.

`Directory.Packages.props` here opts this tree out of the solution's central package management,
because the generated `PayPal.csproj` pins its own package versions inline.
