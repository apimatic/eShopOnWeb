# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.



## Studio bookings (Mindbody)

The `/api/studio/...` endpoints (see `StudioEndpoints/`) put the studio's classes, one-to-one sessions, clients and
bookings from Mindbody next to the catalog. They call Mindbody through the SDK in `sdk/mindbody`.

Settings are read from the `Mindbody:` configuration section: `Mindbody:ApiKey`, `Mindbody:SiteId`,
`Mindbody:Username`, `Mindbody:Password` and the optional `Mindbody:BaseUrl` (used verbatim as the API's base address when
set). Never put the values in a file in the repository; load them into user-secrets, e.g.

```
dotnet user-secrets set "Mindbody:ApiKey" "$MINDBODY_API_KEY"
dotnet user-secrets set "Mindbody:SiteId" "$MINDBODY_SITE_ID"
dotnet user-secrets set "Mindbody:Username" "$MINDBODY_USERNAME"
dotnet user-secrets set "Mindbody:Password" "$MINDBODY_PASSWORD"
```

Behaviours worth knowing:

* Mindbody reports class and availability times as the studio's wall-clock time without an offset (and the SDK
  parses that using the host's own time zone). The studio's zone is read from `GetSites` and every time returned by
  the API is an ISO-8601 date-time with the studio's UTC offset.
* Mindbody lists are paged 100 at a time; every page is read.
* Requests are safe to repeat: booking something already booked answers with the existing booking, and cancelling
  something already cancelled succeeds.
* Failures the app cannot work around answer with a 4xx status and a `Message`; Mindbody outages answer 502/503/504.
