# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

`/api/studio/*` endpoints talk to Mindbody through the SDK in `sdk/mindbody`. Configure the `Mindbody:` section
(`ApiKey`, `SiteId`, `Username`, `Password`, optional `BaseUrl` override used verbatim) with .NET user-secrets
or environment variables, e.g. `dotnet user-secrets set "Mindbody:ApiKey" "<value>"`. Never put the values in the repository.

Shopper endpoints: `GET classes`, `GET slots`, `POST class-bookings`, `POST appointment-bookings`,
`DELETE class-bookings/{id}`, `DELETE appointment-bookings/{id}`. Administrator endpoints: `GET admin/trainers`,
`POST admin/class-bookings`. Booking and cancelling are idempotent so the mobile app can safely resend a request.
