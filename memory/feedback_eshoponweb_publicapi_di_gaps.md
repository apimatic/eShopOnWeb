---
name: feedback-eshoponweb-publicapi-di-gaps
description: PublicApi/Program.cs does not call the Web host's AddCoreServices/AddWebServices helpers — it hand-registers services inline, so anything Web gets "for free" (IEmailSender, MediatR/AddMediatR) must be added to PublicApi's Program.cs explicitly
metadata:
  type: project
---

Unlike `src/Web`, which wires most of ApplicationCore/Infrastructure through `ConfigureCoreServices.AddCoreServices` and `ConfigureWebServices.AddWebServices`, `src/PublicApi/Program.cs` does its own inline `builder.Services.Add...()` calls and does not call those Web-specific helpers (PublicApi has no reference to the Web project, by design — different host).

**Why this matters:** PublicApi originally had **no `IEmailSender` registration and no `AddMediatR` call at all**. Adding a MediatR notification handler in Infrastructure that depends on `IEmailSender` (for the subscription-activated email) crashed PublicApi's DI container validation at startup with `Unable to resolve service for type IEmailSender` — but only surfaced via `PublicApiIntegrationTests` (`WebApplicationFactory`), not via a plain `dotnet build`.

**How to apply:** When adding any new ApplicationCore/Infrastructure service, notification handler, or dependency that Web's `AddCoreServices`/`AddWebServices` would normally supply, remember to also add the equivalent registration directly in `src/PublicApi/Program.cs` — it is not inherited. Cross-check by running `dotnet test tests/PublicApiIntegrationTests` (not just `dotnet build`), since DI container validation errors only throw at `WebApplicationFactory` construction time, not at compile time. Shared cross-host registrations (e.g. the new `AddSubscriptionServices` extension) were deliberately placed in `src/Infrastructure/Dependencies.cs` — a project both hosts already reference — specifically to avoid this trap for future shared services.
