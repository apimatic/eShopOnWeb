# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

The `StudioEndpoints` folder adds studio classes, one-to-one sessions and bookings under `/api/studio/`, backed by
Mindbody through the SDK in `sdk/mindbody`. Every endpoint needs a JWT; the `admin/` ones need the
`Administrators` role. Failures that the app can explain answer with a 4xx status and `{ "StatusCode", "Message" }`.

| Route | Purpose |
| --- | --- |
| `GET /api/studio/classes?from=&to=` | Classes shoppers can attend (default: today + 7 days) |
| `GET /api/studio/slots?sessionTypeId=&from=&to=` | Times a session can start, day by day |
| `POST /api/studio/class-bookings` | Book a class (`classId`) |
| `POST /api/studio/appointment-bookings` | Book a one-to-one session (`sessionTypeId`, `startTime`, optional `trainerId`) |
| `DELETE /api/studio/class-bookings/{bookingId}` | Cancel own class booking |
| `DELETE /api/studio/appointment-bookings/{bookingId}` | Cancel own session booking |
| `GET /api/studio/admin/trainers` | All trainers with contact details |
| `POST /api/studio/admin/class-bookings` | Place a shopper (`shopper` e-mail, `classId`) in a class |

A shopper's first booking creates their studio client and must also carry `homeLocationId`, `streetAddress`, `city`,
`state`, `postalCode`, `dateOfBirth`, `mobilePhone` and `referredBy`. Bookings and cancellations are idempotent, so the
mobile app can safely repeat a request it got no answer to. All times are ISO-8601 with the studio's UTC offset.

### Settings

Read from the `Mindbody:` section: `ApiKey`, `SiteId`, `Username`, `Password` and the optional `BaseUrl` (used verbatim
as the API's base address when set). Keep credentials out of the repository, e.g. with user-secrets:

```
dotnet user-secrets set "Mindbody:ApiKey" "<value>" --project src/PublicApi
dotnet user-secrets set "Mindbody:SiteId" "<value>" --project src/PublicApi
dotnet user-secrets set "Mindbody:Username" "<value>" --project src/PublicApi
dotnet user-secrets set "Mindbody:Password" "<value>" --project src/PublicApi
```

The tests in `tests/PublicApiIntegrationTests/StudioEndpoints` run against the Mindbody sandbox using those settings
(they are inconclusive when the settings are missing). `sdk/mindbody` is git-ignored and must be present to build.
