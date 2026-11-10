# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Studio bookings (Mindbody)

The studio's classes, one-to-one sessions, clients and bookings live in Mindbody and are reached through the SDK in `sdk/mindbody`.
The endpoints are in `StudioEndpoints/`, the Mindbody integration is in `Studio/`.

| Route | Who |
| --- | --- |
| `GET /api/studio/classes?from=&to=` | signed-in shopper (defaults to today and the next 7 days) |
| `GET /api/studio/slots?sessionTypeId=&from=&to=` | signed-in shopper |
| `POST /api/studio/class-bookings` | signed-in shopper |
| `POST /api/studio/appointment-bookings` | signed-in shopper |
| `DELETE /api/studio/class-bookings/{bookingId}` | the shopper who holds the booking |
| `DELETE /api/studio/appointment-bookings/{bookingId}` | the shopper who holds the booking |
| `GET /api/studio/admin/trainers` | Administrators |
| `POST /api/studio/admin/class-bookings` | Administrators |

All times are ISO-8601 date-times with the studio's UTC offset. When the app cannot carry out a request the answer is a 4xx status
(5xx only when Mindbody itself is unreachable) with `{ "StatusCode": ..., "Message": "<reason>" }`.

### Settings

Read from the `Mindbody:` configuration section; nothing is hard-coded and no value belongs in the repository:

| Key | |
| --- | --- |
| `Mindbody:ApiKey` | Mindbody API key |
| `Mindbody:SiteId` | Mindbody site id |
| `Mindbody:Username` / `Mindbody:Password` | staff login used to call Mindbody |
| `Mindbody:BaseUrl` | optional; when set it replaces the public API address verbatim |

For local development keep them in .NET user-secrets, e.g. from the sandbox variables `MINDBODY_API_KEY`, `MINDBODY_SITE_ID`,
`MINDBODY_USERNAME` and `MINDBODY_PASSWORD`:

```
dotnet user-secrets set "Mindbody:ApiKey" "$MINDBODY_API_KEY" --project src/PublicApi
dotnet user-secrets set "Mindbody:SiteId" "$MINDBODY_SITE_ID" --project src/PublicApi
dotnet user-secrets set "Mindbody:Username" "$MINDBODY_USERNAME" --project src/PublicApi
dotnet user-secrets set "Mindbody:Password" "$MINDBODY_PASSWORD" --project src/PublicApi
```

Without the settings the studio endpoints answer `503`.

### Behaviour worth knowing

* Mindbody times are the studio's wall clock without an offset; the studio's time zone is read from Mindbody and its offset applied per date (daylight saving included).
* The shopper's app may repeat a request: booking something already held answers with the existing booking, and cancelling something just cancelled is a success.
* The studio client of a shopper is created once, with a client id derived from the eShop user id, together with the first booking.
