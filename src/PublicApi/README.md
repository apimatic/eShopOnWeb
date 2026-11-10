# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

`/api/studio/*` talks to the Mindbody public API (v6) over plain HTTP. Settings live in the `Mindbody:` section:
`Mindbody:ApiKey`, `Mindbody:SiteId`, `Mindbody:Username`, `Mindbody:Password` and the optional `Mindbody:BaseUrl`
(used verbatim as the API base address when set; defaults to `https://api.mindbodyonline.com/public/v6`).

Keep credentials out of the repository, e.g. with user-secrets (run in `src/PublicApi`):

```
dotnet user-secrets set "Mindbody:ApiKey" "<value of MINDBODY_API_KEY>"
dotnet user-secrets set "Mindbody:SiteId" "<value of MINDBODY_SITE_ID>"
dotnet user-secrets set "Mindbody:Username" "<value of MINDBODY_USERNAME>"
dotnet user-secrets set "Mindbody:Password" "<value of MINDBODY_PASSWORD>"
```

Notes: times are ISO-8601 with the studio's UTC offset; booking and cancelling are idempotent so the app can safely
retry; a class booking id is `<classId>-<visitId>`; a shopper's first booking must include the new-client details
(`homeLocationId`, `addressLine1`/`streetAddress`, `city`, `state`, `postalCode`, `birthDate`, `mobilePhone`, `referredBy`).
