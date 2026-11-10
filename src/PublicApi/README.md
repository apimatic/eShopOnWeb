# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

`/api/studio/...` endpoints call the Mindbody Public API v6 over HTTP. Settings live in the `Mindbody:` section
(`ApiKey`, `SiteId`, `Username`, `Password`, and an optional `BaseUrl` override used verbatim). Keep the values out of
the repository, e.g. `dotnet user-secrets set "Mindbody:ApiKey" "<value>"` in `src/PublicApi`.
