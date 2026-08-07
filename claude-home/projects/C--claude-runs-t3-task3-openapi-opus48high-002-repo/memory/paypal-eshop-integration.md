---
name: paypal-eshop-integration
description: Verified PayPal sandbox flow + design for the eShopOnWeb payments task
metadata:
  type: project
---

Task: add PayPal one-time card payments + saved cards (vault) to eShopOnWeb PublicApi. Specs under `api-specs/paypal/` are the authoritative contract; sandbox base `https://api-m.sandbox.paypal.com`.

Verified live against sandbox (2026-08, test Visa 4111111111111111):
- OAuth: `POST /v1/oauth2/token` Basic(client_id:secret) body `grant_type=client_credentials` → access_token, expires_in 32400s.
- Pay: `POST /v2/checkout/orders` intent=CAPTURE + `payment_source.card` (raw or `{vault_id}`) + header `PayPal-Request-Id` (idempotency, mandatory single-step) + `Prefer: return=representation` → status COMPLETED single-step, capture id at `purchase_units[0].payments.captures[0].id`. No separate capture call needed (handle APPROVED→capture defensively).
- Refund (full): `POST /v2/payments/captures/{capture_id}/refund` empty body → status COMPLETED.
- Vault card: `POST /v3/vault/payment-tokens` with `payment_source.card` raw + `customer.id` → permanent `id` (vault_id); response `payment_source.card` has last_digits/brand/expiry (safe). Delete: `DELETE /v3/vault/payment-tokens/{id}` → 204.

Env/build: SDK 8.0.x pinned but only .NET 9/10 SDK installed; ASP.NET 8 runtime IS present. Build/run with `DOTNET_ROLL_FORWARD=Major`. Run with `UseOnlyInMemoryDatabase=true` (in-memory per host). PublicApi ports 7923(https)/7924(http), port block 7920-7939. Creds in user-secrets (PayPal:ClientId/ClientSecret/Environment) loaded from PAYPAL_* env vars — never in repo. Test user demouser@microsoft.com / Pass@word1. BuyerId = ClaimTypes.Name (username/email).

STATUS: implemented + verified end-to-end (both flows). Domain: Order gains OrderPaymentStatus + MarkAsPaid/MarkAsRefunded (idempotent); PaymentMethod is now an aggregate root scoped by BuyerId. Gateway `IPayPalGateway` (ApplicationCore) impl in `src/Infrastructure/PayPal/` (PayPalApiClient typed HttpClient + token provider + PayPalGateway, wired by AddPayPalPayments). App services OrderPaymentService/PaymentMethodService. 7 endpoints in `src/PublicApi/OrderEndpoints` + `PaymentMethodEndpoints` (MinimalApi.Endpoint IEndpoint pattern). Pay/refund idempotency key = `order-{id}-{OrderDate.UtcTicks}-{op}` (survives in-memory id reuse across restarts); vault save uses a FRESH Guid request id (deterministic key breaks re-save-after-delete → PAYPAL_REQUEST_ID_PREVIOUSLY_USED). 70 unit + 15 PublicApi integration tests green. EF migration 20260806164949_AddPaymentStateAndSavedCards added (SqlServer path; in-memory ignores it).
