---
name: eshoponweb-repo-conventions
description: eShopOnWeb reference-app conventions worth mirroring for any new feature module (layering, DI composition roots, MediatR, endpoint style)
metadata:
  type: project
---

Conventions confirmed by reading the codebase directly, useful whenever adding a new feature module to this repo:

- **No `ImplicitUsings`** in ApplicationCore/Infrastructure csproj - every file needs explicit `using System;` etc. Web/PublicApi *do* have `ImplicitUsings` enabled. Check the target csproj before assuming.
- **Central package management** (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`) - never put a `Version=` attribute on a `<PackageReference>` in a `.csproj`; use `dotnet add package` and it correctly writes the version into `Directory.Packages.props` instead.
- **DI composition roots are duplicated, not shared**, between the Web host (`src/Web/Configuration/ConfigureCoreServices.cs` + `ConfigureWebServices.cs`) and the PublicApi host (`src/PublicApi/Program.cs` inline). PublicApi does NOT reference the Web project, so a shared `AddXServices` extension method placed in Web is unreachable from PublicApi - mirror the existing pattern of registering the same services twice (once per host) rather than trying to share.
- **MediatR** is registered via `AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(...))` in Web only by default; PublicApi has no MediatR at all unless a feature needs it there too (had to add the package + registration for the subscriptions feature).
- **Minimal API endpoints** (`MinimalApi.Endpoint` package) follow `IEndpoint<TResponse, TRequest, TDependency>` with request/response DTOs as separate files named `{Endpoint}.{RequestOrResponse}.cs`, both extending `BaseRequest`/`BaseResponse` (in `Microsoft.eShopWeb.PublicApi` root namespace). `[Authorize(Roles = ..., AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` goes directly on the route-lambda in `AddRoute`, not as a class attribute. `ClaimsPrincipal`/`HttpContext` bind automatically as extra lambda params alongside the DI service and route/body params.
- **`ExceptionMiddleware`** (`src/PublicApi/Middleware/ExceptionMiddleware.cs`) is the single place mapping ApplicationCore exception types to HTTP status codes for PublicApi - extend its switch expression when adding new domain exceptions rather than handling status codes per-endpoint.
- **Domain entities without persistence are legitimate** - not every ApplicationCore type needs to be `BaseEntity`/`IAggregateRoot`/EF-mapped; a plain class is correct when the aggregate lives in an external system of record (used this for the Maxio-backed `Subscription` domain type, which is always read fresh from the provider, never persisted locally).

See `[[maxio-eshoponweb-integration]]` for the specific Maxio integration built on top of these conventions.
