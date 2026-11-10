# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

`/api/studio/*` endpoints call the Mindbody public API over HTTP. Settings come from the `Mindbody:` section
(`ApiKey`, `SiteId`, `Username`, `Password`, and the optional `BaseUrl` override). Keep the values in user-secrets or
environment variables (`Mindbody__ApiKey`, ...), never in the repository:

```
dotnet user-secrets set "Mindbody:ApiKey" "<value>" --project src/PublicApi
```
