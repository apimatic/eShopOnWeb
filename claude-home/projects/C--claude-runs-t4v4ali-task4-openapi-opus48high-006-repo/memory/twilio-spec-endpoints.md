---
name: twilio-spec-endpoints
description: Which Twilio OpenAPI docs/endpoints back the eShop SMS notification integration
metadata:
  type: reference
---

Twilio integration for eShopOnWeb SMS notifications is built to the OpenAPI specs in `api-specs/twilio/`. Auth is HTTP Basic (`accountSid_authToken`): username=AccountSid, password=AuthToken. Both APIs share it.

**Messaging = `twilio_api_v2010/twilio_api_v2010.yaml`** (base `https://api.twilio.com`, overridable by `Twilio:BaseUrl`):
- Send: `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` form-urlencoded. Immediate: `To`,`From`,`Body`. Scheduled follow-up: `To`,`Body`,`MessagingServiceSid`,`ScheduleType=fixed`,`SendAt` (ISO-8601, 15min–7d out). Returns `sid`,`status`.
- Fetch status: `GET .../Messages/{Sid}.json` → `status`,`error_code`,`error_message`.
- List (reconciliation): `GET .../Messages.json?From=&DateSent>=&DateSent<=&PageSize=&Page=` paginate via `next_page_uri` until null.
- Cancel scheduled: `POST .../Messages/{Sid}.json` body `Status=canceled` (only while status=scheduled).
- Redact body (content disposal): `POST .../Messages/{Sid}.json` body `Body=` (empty). Keeps record+status, removes text at provider.
- Delete record: `DELETE .../Messages/{Sid}.json` (not used — redaction preferred to preserve audit).
- message status enum: queued, sending, sent, failed, delivered, undelivered, accepted, scheduled, read, canceled, partially_delivered.

**Phone validation = `twilio_lookups_v2/twilio_lookups_v2.yaml`** (base `https://lookups.twilio.com`, NOT governed by Twilio:BaseUrl):
- `GET /v2/PhoneNumbers/{PhoneNumber}` → `valid` (bool), `phone_number` (canonical E.164). Reject registration when `valid=false`; store canonical `phone_number`.
