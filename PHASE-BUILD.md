# Studio bookings for eShopOnWeb, with Mindbody

The brand behind eShopOnWeb runs a studio with classes and one-to-one sessions at several locations, and the studio's schedule, clients and bookings live in Mindbody. Add studio bookings to eShopOnWeb alongside the existing catalog, basket and orders. Call Mindbody's API over HTTP, without an SDK. Test the integration against the Mindbody sandbox.

Add each feature below to `src/PublicApi` as an endpoint under `/api/`, following that project's conventions: the caller's identity and role come from the JWT, and the staff features are for the administrator role the project already uses. Each feature gives a suggested route. When the app can't carry out a request, the endpoint answers with a 4xx status and a reason. Do not stub Mindbody or fabricate its responses.

Shoppers use the studio's mobile app. It sends a request again when it gets no answer in time, and it treats any answer other than a success as a failure. Every class and session time the app returns is an ISO-8601 date-time with its UTC offset. The app runs for weeks without a restart, and everything keeps working throughout.

## Features

1. **Classes.** A shopper can list the studio's classes for a range of days; when no range is given, the list covers today and the following 7 days. The list shows the classes shoppers can attend. Each class has its `startTime` and `endTime`. Suggested route: `GET /api/studio/classes`, with optional `from` and `to` dates.
2. **Session times.** For a session type, a shopper can list the times a session can start on each day between any two dates, whether or not a trainer is free then, each with its `startTime` and `endTime`. Suggested route: `GET /api/studio/slots` with the session type and the `from` and `to` dates.
3. **Book.** A shopper books a place in a class or a one-to-one session. A signed-in shopper is a client of the studio, created once from their eShop identity, at the home location they choose when they first book. With that first booking they also give the details the studio asks of every new client: street address, city, state, postal code, date of birth, mobile number and how they heard of the studio. Suggested routes: `POST /api/studio/class-bookings` and `POST /api/studio/appointment-bookings`.
4. **Cancel a booking.** A shopper can cancel their own booking. Suggested routes: `DELETE /api/studio/class-bookings/{bookingId}` and `DELETE /api/studio/appointment-bookings/{bookingId}`.
5. **Trainers.** Front-desk operators can see every trainer with all their contact details. Suggested route: `GET /api/studio/admin/trainers`.
6. **Book for a shopper.** A front-desk operator can place a shopper in any class in the timetable. Suggested route: `POST /api/studio/admin/class-bookings` with the shopper and the class.

## Settings

Read the Mindbody settings from the `Mindbody:` configuration section using exactly these keys, and hard-code none of their values: `Mindbody:ApiKey`, `Mindbody:SiteId`, `Mindbody:Username`, `Mindbody:Password` and `Mindbody:BaseUrl`. The sandbox credentials are in the environment variables `MINDBODY_API_KEY`, `MINDBODY_SITE_ID`, `MINDBODY_USERNAME` and `MINDBODY_PASSWORD`. `Mindbody:BaseUrl` is an optional override: when it is set, use it verbatim as the API's base address instead of the public one.

---

## Constraints

- **Secrets never enter the repository.** Read the API credentials from the environment
  variables above and load them into **.NET user-secrets** yourself. Never write their
  **values** into any file inside this repository — not into `appsettings*.json`, not into
  a launch profile, a script, a test fixture, a comment, or a commit message. Referencing
  the variable/secret **names** is fine, the values are not.
- **You are running headless — there is no one to answer you.** Work until the integration
  is fully complete. Never hand back, never end with a question, and never defer remaining
  work to the user: decide and proceed.

