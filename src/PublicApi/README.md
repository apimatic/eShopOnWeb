# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

`StudioEndpoints/` adds the studio's classes, session times, bookings and trainers under `/api/studio/...`, backed by
the Mindbody SDK in `sdk/mindbody`. Settings come from the `Mindbody:` configuration section
(`ApiKey`, `SiteId`, `Username`, `Password` and the optional `BaseUrl` override). Keep the credentials out of the repository
with user-secrets, e.g. `dotnet user-secrets set "Mindbody:ApiKey" "<value>" --project src/PublicApi`.

Notes on behaviour: times are returned with the studio's UTC offset; first bookings create the studio client from the
shopper's eShop identity; repeating a booking or a cancellation answers with success again instead of an error.
