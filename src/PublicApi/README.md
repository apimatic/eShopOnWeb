# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

`/api/studio/*` endpoints talk to the Mindbody Public API over HTTP. Settings live in the `Mindbody:` section
(`ApiKey`, `SiteId`, `Username`, `Password`, optional `BaseUrl` used verbatim). Load them with user-secrets, e.g.
`dotnet user-secrets set "Mindbody:ApiKey" "<value>"`; never commit values.
