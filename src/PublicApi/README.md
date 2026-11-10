# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

The `/api/studio/...` endpoints (`StudioEndpoints/`) read the studio schedule and place bookings through the Mindbody SDK in `sdk/mindbody`.

Settings come from the `Mindbody:` section: `ApiKey`, `SiteId`, `Username`, `Password` and the optional `BaseUrl` (used verbatim as the API base address when set). Keep the credentials out of the repository, e.g. in .NET user-secrets:

    dotnet user-secrets set "Mindbody:ApiKey" "<value of MINDBODY_API_KEY>" --project src/PublicApi

(and likewise `Mindbody:SiteId`, `Mindbody:Username`, `Mindbody:Password`).

Notes: Mindbody times are the studio's local wall-clock times, so they are re-read in the studio time zone and returned with their UTC offset; bookings and cancellations are safe to retry.
