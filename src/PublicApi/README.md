# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

Studio classes, one-to-one sessions and bookings are served under `/api/studio/` and call Mindbody through the SDK in `sdk/mindbody`.

Settings come from the `Mindbody:` configuration section: `Mindbody:ApiKey`, `Mindbody:SiteId`, `Mindbody:Username`, `Mindbody:Password` and the optional `Mindbody:BaseUrl` (used verbatim when set). Keep the values out of the repository, for example in user-secrets:

```
dotnet user-secrets set "Mindbody:ApiKey" "<value>" --project src/PublicApi
```
