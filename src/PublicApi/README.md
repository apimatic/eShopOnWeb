# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Studio bookings (Mindbody)

`/api/studio/*` endpoints call the Mindbody Public API v6 over HTTP. Configure the `Mindbody:` section
(`ApiKey`, `SiteId`, `Username`, `Password`, and optional `BaseUrl` override) via .NET user-secrets or
environment variables (`Mindbody__ApiKey`, ...), e.g.:

    dotnet user-secrets set "Mindbody:ApiKey" "<value>" --project src/PublicApi

Shopper endpoints need any valid JWT; `/api/studio/admin/*` needs the Administrators role.
Class and session times are returned as ISO-8601 date-times with the studio's UTC offset. Booking and
cancelling are idempotent so the mobile app can safely resend a request.
