# PayPal .NET SDK Integration Plan — eShopOnWeb

SDK: `PayPalServerSdk` (NuGet `AsadAli.Checkout.Sdk`, install version-less), generated at tag `v1.0.1`.

## 1. Scope & sequence

1. **Client & DI setup** — register `PayPalServerSdkClient` via `AddPayPalServerSdkClient`, bound from
   `PayPal:ClientId` / `PayPal:ClientSecret` / `PayPal:Environment` / `PayPal:Currency` / `PayPal:BaseUrl`.
2. **Vaulting (Flow 2)** — `client.Vault.CreatePaymentToken` (raw card → saved token, no redirect),
   `client.Vault.GetPaymentToken` / `ListCustomerPaymentTokens` (fetch/list), `client.Vault.DeletePaymentToken`
   (deactivate).
3. **Order authorize — pay step (Flow 1)** — `client.Orders.CreateOrder` (intent `AUTHORIZE`) then
   `client.Orders.AuthorizeOrder` with `payment_source` set directly (raw card or vault token id) — no
   buyer-redirect round trip.
4. **Capture — fulfil step (Flow 1)** — `client.Payments.CaptureAuthorizedPayment`; on a stale authorization,
   `client.Payments.ReauthorizePayment`; distinguish "reauthorizable" vs "no longer reauthorizable" from the
   typed error (see Trap notes — this is UNVERIFIED at the exact-code level).
5. **Void — cancel step (Flow 1, pre-fulfilment)** — `client.Payments.VoidPayment`.
6. **Refund — post-fulfilment (Flow 1)** — `client.Payments.RefundCapturedPayment`, full or partial, with
   `payPalRequestId` as the idempotency key.
7. **Reconciliation** — `client.TransactionSearch.SearchTransactions`, chunked into ≤31-day windows, each
   window paged via `page`/`pageSize` until `page > TotalPages`.
8. **Amount/currency formatting** — `Money.Value`/`CurrencyCode` convention, applied everywhere amounts are
   built (steps 3, 4, 6).
9. **Error boundary** — one boundary used by every step above; see CONTRACT SHEET error rows + REQUIRED
   READING.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### 2.1 Namespaces actually needed in this integration

| Type(s) | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `Orders`, `Payments`, `Vault`, `TransactionSearch` (controllers) | `PayPalServerSdk.Api` |
| All request/response records (`Money`, `CardRequest`, `OrderRequest`, `OrderAuthorizeRequest`, `CaptureRequest`, `RefundRequest`, `PaymentTokenRequest`, `SearchResponse`, `Error`, `Error1`, `TransactionDetails`, `TransactionInformation`, `SellerReceivableBreakdown`, `SellerPayableBreakdown`, etc.) | `PayPalServerSdk.Models` |
| All enums (`CardBrand`, `CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `PayPalReferenceIdType`, etc.) | `PayPalServerSdk.Models.Enums` |
| Per-operation error classes (`CreatePaymentTokenError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, `RefundCapturedPaymentError`, `ReauthorizePaymentError`, `VoidPaymentError`, `DeletePaymentTokenError`, `GetPaymentTokenError`, `ListCustomerPaymentTokensError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` |
| `ApiError`, `RawError` | `PayPalServerSdk.Core.ErrorResponse` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` |
| `ServerEnvironment` **and** `ServerOptions.Default`'s type `DefaultOptions` (incl. its nested `SandboxOptions`) | `PayPalServerSdk.Servers` |
| `RequestOptions` | `PayPalServerSdk.Core` |
| `RetryOptions`, `LoggingOptions` | `PayPalServerSdk.Core.Configuration` |

Note the split: `options.Server` is type `ServerOptions` (root `PayPalServerSdk` namespace) but
`options.Server.Default` is type `DefaultOptions`, declared in `PayPalServerSdk.Servers` — a `using
PayPalServerSdk;` alone will not resolve `DefaultOptions` if you ever name it explicitly.
*(map: `sdk-map.md` "Getting a client" + "Namespaces"; SDK source: `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` — read on a real map-side gap, since none of these member lists are in the map's client-options table.)*

### 2.2 Client construction, auth, environment, base-URL override

- Constructor: `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`. DI:
  `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure)` (registers a
  **singleton** built from `IHttpClientFactory.CreateClient()` — an unnamed client). *(map: sdk-map.md
  "Getting a client"; source: `ServiceCollectionExtensions.cs`.)*
- Credentials: `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <PayPal:ClientId>, ClientSecret =
  <PayPal:ClientSecret> }`. `OAuth2ClientCredentials.ClientId` and `.ClientSecret` are both `required string`;
  there's also an optional `Scope`. *(source: `OAuth2ClientCredentials.cs` — the map's client-options table
  names the property but not its members.)*
- Environment: `options.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox`. **This SDK release
  has exactly one `ServerEnvironment` member: `Sandbox`.** There is no `Live`/`Production` member —
  `ServerEnvironment.Match` throws `ArgumentOutOfRangeException` for anything but `Sandbox`, and no other
  static instance exists to construct. *(source: `Servers/ServerEnvironment.cs`.)* Binding `PayPal:Environment
  = "live"` therefore **cannot select a distinct SDK environment** in this generated release — see Assumptions
  & Blockers.
- Base-URL override (this is also the *only* lever for pointing at a non-sandbox host): `options.Server` is a
  `ServerOptions` with `.Default` (`DefaultOptions`) with `.Sandbox` (`DefaultOptions.SandboxOptions`) with
  `.BaseUrl` (`string`, defaults to `"https://api-m.sandbox.paypal.com"`). Every call — including the OAuth2
  token request itself, since token acquisition goes through the same `Server.Default` resolution — uses this
  URL because `ServerEnvironment.Match` only ever resolves through the `Sandbox` branch. Set
  `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` when the config key is supplied (verbatim, no
  trailing-slash normalization implied by the source). *(source: `ServerOptions.cs`, `Servers/DefaultOptions.cs`
  — a real map-side gap since sdk-map.md's client-options table lists `Server: ServerOptions` but not its
  nested shape.)*
- Currency: `PayPal:Currency` is **not** an SDK config value — it is your own default for `Money.CurrencyCode`
  when building request amounts (§2.7). There is no client-level currency setting.

### 2.3 Vault — save / list / fetch / delete a card token (Flow 2)

| Op (`client.Vault.…`) | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentTokenResponse>` | `PaymentTokenRequest { Customer (customer): Customer?, PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req }`; `PaymentTokenRequestPaymentSource { Card (card): PaymentTokenRequestCard? }`; `PaymentTokenRequestCard { Name (name): string?, Number (number): string?, Expiry (expiry): string?, SecurityCode (security_code): string?, Brand (brand): CardBrand?, BillingAddress (billing_address): Address? }`; `Customer { Id (id): string?, MerchantCustomerId (merchant_customer_id): string? }` | `PaymentTokenResponse { Id (id): string?, Customer (customer): CustomerResponse?, PaymentSource (payment_source): PaymentTokenResponsePaymentSource?, Links (links): IReadOnlyList<LinkDescription>? }`; `PaymentTokenResponsePaymentSource.Card`: `CardPaymentTokenEntity? { Name, LastDigits (last_digits), Brand (brand): CardBrand?, Expiry (expiry), VerificationStatus (verification_status): CardVerificationStatus?, Verification (verification): CardVerificationDetails?, AuthenticationResult (authentication_result): CardAuthenticationResponse?, BinDetails, Type }` — **no `Number`/PAN field exists on this type at all**, so there is no accidental full-PAN echo to guard against | `SdkException<CreatePaymentTokenError>` (Case A) — `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError(out RawError)` fallback | none |
| `ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CustomerVaultPaymentTokensResponse>`; query wire: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired` | — | `CustomerVaultPaymentTokensResponse { TotalItems, TotalPages, Customer (customer): VaultResponseCustomer?, PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?, Links }` | `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` fallback | `page` only, no `perPage` |
| `GetPaymentToken` | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentTokenResponse>` | — | same `PaymentTokenResponse` as above | `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError` fallback | none |
| `DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task` (void) | — | — | `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` fallback | none |

*(map: `map/operations/Vault.md`; `map/models/records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`.)*

**Customer id you control**: set `PaymentTokenRequest.Customer.Id` (or `MerchantCustomerId`) yourself when
creating the token; the same value round-trips on `PaymentTokenResponse.Customer.Id` and is what you pass as
`ListCustomerPaymentTokens`'s `customerId`. *(map records: `Customer`, `CustomerResponse`,
`VaultResponseCustomer` — all `{ Id, MerchantCustomerId }`, `records-1-Ac-Pa.md` / `records-2-Pa-Ve.md`.)*

**Wire format**: `PaymentTokenRequestCard.Number` — regex `^[0-9]{13,19}$` (digits only, no spaces/dashes;
sandbox `4111111111111111` fits). `.Expiry` — `^[0-9]{4}-(0[1-9]|1[0-2])$` (`YYYY-MM`, **not** `MM/YY`).
`.SecurityCode` — `^[0-9]{3,4}$`. *(source: `Models/PaymentTokenRequestCard.cs` — the map's field-type column
gives `string?`/`string !req` but not the wire regex; opened on a real gap since amount/PAN format precision
matters for a PCI-adjacent field.)*

**Billing address shape (`Address`, used by `CardRequest.BillingAddress` §2.4 and
`PaymentTokenRequestCard.BillingAddress` above)**: `Address { AddressLine1 (address_line_1): string?,
AddressLine2 (address_line_2): string?, AdminArea2 (admin_area_2): string?, AdminArea1 (admin_area_1):
string?, PostalCode (postal_code): string?, CountryCode (country_code): string !req }` —
`PayPalServerSdk.Models`. Every field is optional **except `CountryCode`, which is `required string`** (2-letter
ISO 3166-1 alpha-2, e.g. `"US"` — the map's field-type column marks it `!req` but does not carry the format;
follow ISO 3166-1 alpha-2 since that is PayPal's documented convention for this field elsewhere in the SDK).
`AdminArea1` is state/province/region (e.g. `"CA"`), `AdminArea2` is city, `PostalCode` is ZIP/postal code.
Because every field except `CountryCode` is nullable/optional at the type level, the SDK contract alone does not
force AVS-quality data — a `CardRequest`/`PaymentTokenRequestCard.BillingAddress` with only `CountryCode` set is
still request-shape-valid and will not fail client-side validation, even though the sandbox direct-card
processor may still refuse the transaction (a `422 TRANSACTION_REFUSED`) if it wants fuller AVS data. **UNVERIFIED**:
which of `AddressLine1`/`PostalCode`/`AdminArea1` the sandbox processor actually requires (beyond `CountryCode`)
before it accepts a direct-card `AUTHORIZE`/vault call — that is a processor/AVS policy fact, not something the
generated contract encodes. Defensive-coding directive: always populate `CountryCode` (required by the type
system) **and** `PostalCode` + `AddressLine1` when known (the fields AVS checks most commonly key on), rather
than sending only `CountryCode`; on a continued `422 TRANSACTION_REFUSED` after doing so, extract and log
`Error1`/`ErrorDetails1.Issue` (§2.8-style accessors) rather than guessing which address field was missing.
*(map: `map/models/records-1-Ac-Pa.md` row `Address`; source file not needed — the map row is unambiguous and
already gives every field name, wire name, type, and required flag.)*

**No browser-approval/3DS challenge is modeled on this call's response.** `PaymentTokenResponse` carries no
status/`payer_action_required`/redirect-link shape (contrast `SetupTokenResponse.Status:
PaymentTokenStatus?`, which *does* have a `PayerActionRequired` member — that's the vaulting path built for a
buyer-approval round trip, and it is **not** the one this plan uses). The closest signal
`CreatePaymentToken` can return is `CardPaymentTokenEntity.VerificationStatus: CardVerificationStatus?`
(`Verified`/`Failed`) and, nested under `AuthenticationResult.ThreeDSecure`,
`ThreeDSecureCardAuthenticationResponse { AuthenticationStatus (authentication_status): ParesStatus?,
EnrollmentStatus (enrollment_status): EnrollmentStatus? }`. **UNVERIFIED**: whether a live processor decision
to step up 3DS on this synchronous endpoint surfaces as a populated-but-non-clean `ThreeDSecure` block, as a
`422` typed error, or is simply not triggerable through this direct-card path at all — the generated contract
does not say which. Defensive-coding directive: after a successful `CreatePaymentToken`, best-effort-read
`CardPaymentTokenEntity.VerificationStatus` and `AuthenticationResult?.ThreeDSecure` (both optional, may be
null); if `VerificationStatus == CardVerificationStatus.Failed` or `ThreeDSecure` is present with anything
other than a clean pass, log it and surface it to the caller as "verification could not be confirmed" —
**do not** build a redirect/challenge handler, and do not treat a null/absent `ThreeDSecure` block as an
error (absence is the expected shape for a plain successful vault). *(map: `records-1-Ac-Pa.md` rows
`CardVerificationDetails`, `AuthenticationResponse`; `records-2-Pa-Ve.md` row `PaymentTokenResponsePaymentSource`,
`SetupTokenResponse`; `enums.md` rows `CardVerificationStatus`, `EnrollmentStatus`, `ParesStatus`.)*

Merchant vaulting/direct-card-processing enablement: not a fact the SDK surface can confirm either way (it's
an account-capability flag on PayPal's side, not modeled in any response). Per the brief, assumed already
enabled on the sandbox business account.

### 2.4 Orders — authorize (Flow 1 pay step)

| Op (`client.Orders.…`) | Signature | Request | Response | Error |
|---|---|---|---|---|
| `CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Order>` | `OrderRequest { Intent (intent): CheckoutPaymentIntent !req, PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req, Payer?, PaymentSource?, ApplicationContext? }`; `PurchaseUnitRequest { Amount (amount): AmountWithBreakdown !req, ReferenceId?, … }`; `AmountWithBreakdown { CurrencyCode !req, Value !req, Breakdown? }` | `Order { Id (id): string?, Status (status): OrderStatus?, PurchaseUnits, Links, … }` | `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400,401,422] · `TryGetRawError` fallback |
| `AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<OrderAuthorizeResponse>` | `OrderAuthorizeRequest { PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource? }`; `OrderAuthorizeRequestPaymentSource { Card (card): CardRequest?, Token (token): Token? }` | `OrderAuthorizeResponse { Id, Status: OrderStatus?, PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?, … }` — **the authorization itself is nested**: `PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → each has `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount`, `ExpirationTime (expiration_time): string?` | `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` fallback |

**One-off card**: `OrderAuthorizeRequestPaymentSource.Card = new CardRequest { Number, Expiry, SecurityCode,
Name, BillingAddress }` (same `CardRequest` shape/regexes as §2.3, plus `VaultId (vault_id): string?`).
**Saved card**: `OrderAuthorizeRequestPaymentSource.Card = new CardRequest { VaultId = <saved payment-token
id> }` — set **only** `VaultId`, leave `Number`/`Expiry`/`SecurityCode` null. **Do not use
`OrderAuthorizeRequestPaymentSource.Token`** for this: `Token { Id !req, Type (type): TokenType !req }` and
`TokenType` has **exactly one member, `BillingAgreement (BILLING_AGREEMENT)`** — it references a PayPal
billing-agreement id, not a Vault payment-token id, so it cannot carry a saved-card reference in this SDK.
*(map: `records-1-Ac-Pa.md` rows `CardRequest`, `Token`, `OrderAuthorizeRequestPaymentSource`,
`OrderAuthorizeResponse`; `records-2-Pa-Ve.md` row `PaymentCollection`; `enums.md` row `TokenType` — the
single-member enum is the load-bearing fact here and is easy to miss without reading the full member list.)*

**`CardRequest`'s full field surface (relevant to a sandbox `422 TRANSACTION_REFUSED` on `AuthorizeOrder`)**:
beyond `Name`/`Number`/`Expiry`/`SecurityCode`/`BillingAddress`/`VaultId` above, the generated contract also
models `Attributes (attributes): CardAttributes?` → `{ Customer?, Vault (vault): VaultInstructionBase?,
Verification (verification): CardVerification? }` → `CardVerification { Method (method):
OrdersCardVerificationMethod? = ScaWhenRequired }` (members: `ScaAlways`, `ScaWhenRequired`, `_3DSecure`,
`AvsCvv`); `StoredCredential (stored_credential): CardStoredCredential?` → `{ PaymentInitiator !req,
PaymentType !req, Usage? = Derived, PreviousNetworkTransactionReference? }`; `NetworkToken (network_token):
NetworkToken?`; and `ExperienceContext (experience_context): CardExperienceContext?` → `{ ReturnUrl
(return_url): string?, CancelUrl (cancel_url): string? }` — its own doc-comment: "Customizes the payer
experience during the 3DS Approval for payment." **None of these five are set in the one-off-card/saved-card
construction above, and none is required by the type system** (`CardAttributes?`, `StoredCredential?`,
`ExperienceContext?` are all nullable). Contrast `Vault.CreatePaymentToken`'s card shape,
`PaymentTokenRequestCard { Name, Number, Expiry, SecurityCode, Brand, BillingAddress }` (§2.3) — it has **no**
`Attributes`/`StoredCredential`/`ExperienceContext`/verification-method field at all, so the vault path
structurally cannot exercise verification/3DS decisioning the way `AuthorizeOrder`'s `CardRequest` can. That
asymmetry is a concrete, contract-level reason the same PAN can vault successfully while a direct-card
`AuthorizeOrder` on the identical card is declined: the two operations use materially different generated
card shapes, not the same shape with different defaults. **UNVERIFIED** (business-rule/live-processor fact,
not resolvable from the static contract): whether the sandbox processor requires
`Attributes.Verification.Method` and/or `ExperienceContext.ReturnUrl`/`CancelUrl` to be present before it
approves a direct-card `AUTHORIZE`, or whether this is unrelated to the `TRANSACTION_REFUSED` observed.
Concrete next-diagnostic-step directive (a same-shape, contract-legal variation — not a guess at a different
endpoint or field): retry the raw-card `AuthorizeOrder` call once with `CardRequest.Attributes = new
CardAttributes { Verification = new CardVerification { Method =
OrdersCardVerificationMethod.ScaWhenRequired } } }` (the type's own default, made explicit on the wire instead
of omitted); if it still refuses, the decline is very likely PayPal-sandbox/processor-side (e.g. a simulated
hard-decline test card or an account-capability gap) rather than a missing-field defect in the request, and
should be reported as such rather than iterated on further from the SDK surface alone. *(map:
`records-1-Ac-Pa.md` rows `CardRequest`, `CardAttributes`, `CardVerification`, `CardStoredCredential`,
`CardExperienceContext`; `records-2-Pa-Ve.md` row `PaymentTokenRequestCard`; `enums.md` row
`OrdersCardVerificationMethod`; source `Models/CardRequest.cs`, `Models/CardVerification.cs` — opened on a
real gap, since the map's one-line field list does not carry the doc-comment context connecting
`ExperienceContext`/`Attributes.Verification` to 3DS/verification decisioning.)*

**`PurchaseUnitRequest`'s full field surface (all fields, not just `Amount`)**: `ReferenceId (reference_id):
string?`, `Amount (amount): AmountWithBreakdown !req`, `Payee (payee): PayeeBase?` (`{ EmailAddress?,
MerchantId? }` — identifies which merchant/sub-merchant receives funds in a marketplace/platform setup; not a
risk-scoring input for a single-merchant direct-card charge), `PaymentInstruction (payment_instruction):
PaymentInstruction?`, `Description (description): string?`, `CustomId (custom_id): string?`, `InvoiceId
(invoice_id): string?`, `SoftDescriptor (soft_descriptor): string?`, `Items (items):
IReadOnlyList<ItemRequest>?`, `Shipping (shipping): ShippingDetails?`, `SupplementaryData
(supplementary_data): SupplementaryData?`. Source doc-comments (`Models/PurchaseUnitRequest.cs`) for
`Description`/`CustomId`/`InvoiceId`/`SoftDescriptor`/`Items`/`Shipping` describe reconciliation/statement-
display/fulfilment purposes only — **none of their doc-comments mention risk, fraud, or processor
acceptance**. The **one** field whose own doc-comment explicitly ties to processor risk decisioning is
`SupplementaryData`: *"Supplementary data about a payment. This object passes information that can be used
to improve risk assessments and processing costs, for example, by providing Level 2 and Level 3 payment
data"* (source: `Models/PurchaseUnitRequest.cs`) — and `Api/Orders.cs`'s own `CreateOrder`/`PatchOrder`
`<remarks>` repeat the same claim: *"Merchants and partners can add Level 2 and 3 data to payments to reduce
risk and payment processing costs."* `SupplementaryData { Card (card): CardSupplementaryData?, Risk (risk):
RiskSupplementaryData? }` (map: `records-2-Pa-Ve.md` row `SupplementaryData`) — `CardSupplementaryData`
carries Level-2/3 line-item/tax data, `RiskSupplementaryData.Customer: ParticipantMetadata?` carries
buyer-history hints. **UNVERIFIED** whether omitting `SupplementaryData` is itself sufficient to cause a
`TRANSACTION_REFUSED` on a first-time low-risk sandbox test card (PayPal's own wording is "reduce risk," not
"required for approval") — but it is a concrete, previously-untried, contract-legal field to populate. *(map:
`records-2-Pa-Ve.md` rows `PurchaseUnitRequest`, `PayeeBase`, `SupplementaryData`; source
`Models/PurchaseUnitRequest.cs` — opened on a real gap, since the map's one-line field list doesn't carry
per-field doc-comment text.)*

**`OrderRequest.Payer` is DEPRECATED and PayPal-wallet-only — not a lever for direct-card risk/acceptance**:
its full doc-comment (`Models/OrderRequest.cs`) reads *"DEPRECATED. ... The Payer object was intended to only
be used with the `payment_source.paypal` object. ... Please use `payment_source.paypal`."* Setting it has no
documented bearing on a direct-`CardRequest` `AuthorizeOrder`/`CreateOrder` call.

**`OrderRequest.ApplicationContext` (`OrderApplicationContext`) is ENTIRELY deprecated, field-by-field, and
is the PayPal-redirect/wallet approval-experience object, not a direct-card lever**: every one of its 8
fields (`BrandName`, `Locale`, `LandingPage`, `ShippingPreference`, `UserAction`, `PaymentMethod`,
`ReturnUrl`, `CancelUrl`, `StoredPaymentSource`) carries a `DEPRECATED` doc-comment in
`Models/OrderApplicationContext.cs`, each pointing at a *different, current* replacement location under
`payment_source.paypal.experience_context.*` (i.e., `PayPalWalletExperienceContext`, already excluded from
this plan as wallet-only) — **except** `StoredPaymentSource`, whose replacement note is the one directly
relevant here: *"The fields in `stored_payment_source` are now available in the `stored_credential` object
under the `payment_source` which supports them (eg. `payment_source.card.stored_credential.payment_initiator`).
Please specify this field in the `payment_source` object instead of the `application_context` object."* That
confirms `CardRequest.Attributes.StoredCredential` (type `CardStoredCredential`, §2.4 above) — not
`ApplicationContext` — is the current, correct location for stored-credential/card-on-file signaling on a
direct-card `AuthorizeOrder`.

**New, concrete, previously-untried diagnostic: `CardRequest.Attributes.StoredCredential`.**
`CardStoredCredential`'s own doc-comment states hard compatibility rules: *"payment_type=ONE_TIME is
compatible only with payment_initiator=CUSTOMER. usage=FIRST is compatible only with
payment_initiator=CUSTOMER. previous_transaction_reference or previous_network_transaction_reference is
compatible only with payment_initiator=MERCHANT."* (source: `Models/CardStoredCredential.cs`). Fields/enum
values (map: `enums.md` rows `PaymentInitiator`, `StoredPaymentSourcePaymentType`,
`StoredPaymentSourceUsageType`): `PaymentInitiator !req` (`Customer`/`Merchant`), `PaymentType !req`
(`OneTime`/`Recurring`/`Unscheduled`), `Usage? = Derived` (`First`/`Subsequent`/`Derived`),
`PreviousNetworkTransactionReference?: NetworkTransaction?`. For a checkout-initiated, buyer-present,
first-time direct-card charge, the contract-consistent values are `PaymentInitiator.Customer` +
`PaymentType.OneTime` + `Usage.First` — a combination the doc-comment itself declares mutually compatible,
unlike, e.g., `OneTime`+`Merchant` which the doc-comment does not list as compatible. Concrete
next-diagnostic-step directive: retry the raw-card `AuthorizeOrder` call with `CardRequest.Attributes = new
CardAttributes { StoredCredential = new CardStoredCredential { PaymentInitiator = PaymentInitiator.Customer,
PaymentType = StoredPaymentSourcePaymentType.OneTime, Usage = StoredPaymentSourceUsageType.First } }` (leave
`Verification` as previously tried, or omit it) — this is a materially different, previously-untried field
from `Verification.Method`, and its doc-comment is the closest thing in the whole SDK surface to an explicit
"here is how the processor classifies this specific charge" signal. If this still produces the identical
`TRANSACTION_REFUSED`, that strengthens (does not prove) the case for a sandbox/processor/account-capability-
side decline rather than a missing-field defect. *(map: `records-1-Ac-Pa.md` row `CardStoredCredential`;
`enums.md` rows `PaymentInitiator`, `StoredPaymentSourcePaymentType`, `StoredPaymentSourceUsageType`;
`records-2-Pa-Ve.md` row `OrderApplicationContext`; source `Models/OrderApplicationContext.cs`,
`Models/CardStoredCredential.cs`, `Models/OrderRequest.cs` — opened on a real gap, since only source
doc-comments carry the deprecation cross-references and the compatibility rules.)*

**Where payment_source goes**: the operation's own doc-remarks state authorize succeeds when "the buyer …
approve[s] the order **or** a valid `payment_source` [is] provided in the request" — "the request" being
`AuthorizeOrder`'s own body (`OrderAuthorizeRequest.PaymentSource`), which is the mechanism this plan uses to
avoid any redirect. `CreateOrder`'s `OrderRequest.PaymentSource` field also exists but this plan does not rely
on it — see Assumptions & Blockers. *(map/source: `map/operations/Orders.md` "Notes" line for
`AuthorizeOrder`; SDK source `Api/Orders.cs` XML `<remarks>`.)*

**Source doc-comment update — a documented "single-step create order" flow exists that this plan does not
use**: `CreateOrder`'s own `payPalRequestId` XML doc states *"It is mandatory for all single-step create order
calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id,
PayPal.billing_agreement_id, etc)."* (source: `Api/Orders.cs`). This is direct source evidence that PayPal
names a distinct **single-step** pattern where `OrderRequest.PaymentSource` (Card/vault_id/billing_agreement_id)
is supplied **on `CreateOrder` itself**, separate from the two-step pattern above (`CreateOrder` with no
`PaymentSource`, then a separate `AuthorizeOrder` call carrying `OrderAuthorizeRequest.PaymentSource`). Read
together, `AuthorizeOrder`'s "a valid payment_source must be provided in the request" remark and this
doc-comment are ambiguous between "in *that* request" (the two-step reading above) and "in *the (create-order)*
request" (the single-step reading this doc-comment surfaces) — the source does not disambiguate further, and no
doc-comment states the two-step pattern is invalid or unsupported. **UNVERIFIED**: whether the two-step pattern
above is itself a supported, first-class path for a direct (non-redirect) card `AUTHORIZE`, or whether the
observed `TRANSACTION_REFUSED` is downstream of using it instead of the documented single-step pattern.
Concrete next-diagnostic-step directive (not a change to the shipped contract — both `OrderRequest.PaymentSource`
and `OrderAuthorizeRequest.PaymentSource` are independently valid per their own map rows): as a diagnostic, try
the single-step pattern — set `OrderRequest.PaymentSource = new PaymentSource { Card = <same CardRequest> }` on
`CreateOrder` (intent `AUTHORIZE`) and pass a fresh, non-null `payPalRequestId` (contract-confirmed mandatory
for this pattern by the doc-comment above), then call `AuthorizeOrder` with `body: null` (the map does not say
whether `PaymentSource` must also be repeated on `AuthorizeOrder` in single-step mode — `null` is the
minimal-diff first try, since `AuthorizeOrder`'s `body` parameter is itself `OrderAuthorizeRequest?`). If this
changes the decline outcome, revise this plan's §2.4 to make single-step the primary pattern; if the same
`TRANSACTION_REFUSED` persists, that is evidence the decline is not caused by which of the two documented
patterns is used. *(map: `map/operations/Orders.md` `CreateOrder`/`AuthorizeOrder` rows; `records-1-Ac-Pa.md` row
`OrderRequest`; `records-2-Pa-Ve.md` row `PaymentSource`; source `Api/Orders.cs` — the `payPalRequestId` XML
`<param>` doc on `CreateOrder`, a real map-side gap since `Orders.md`'s per-op row does not carry per-parameter
doc text.)*

**`prefer` default is `"return=minimal"`, which returns only `id`, `status`, and HATEOAS links** — the nested
`Authorizations[]`/breakdown data will not be populated unless you pass `prefer: "return=representation"`
explicitly on `AuthorizeOrder` (and, per §2.5, on `CaptureAuthorizedPayment`/`RefundCapturedPayment` too).
*(source: `Api/Orders.cs`, `Api/Payments.cs` — the `prefer` XML `<param>` doc; the map's operation rows show
the default value but not what "minimal" omits, so this is a real map-side gap resolved from source.)*

**Idempotency**: pass a caller-generated, **stable-across-retries** string as `payPalRequestId` on
`CreateOrder` and `AuthorizeOrder` — the SDK maps it verbatim to header `PayPal-Request-Id`. **The SDK itself
also attaches a fresh, randomly-generated `Idempotency-Key` header (`Guid.NewGuid()`) to every mutating call,
including these** — this header is regenerated on every call (including a retry of the exact same logical
operation) and provides **no** idempotency; it is not something you configure or can rely on. `PayPal-Request-Id`
is the only lever that actually works, and only if your code reuses the same string across a retry. *(SDK
source: `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs` — this auto-header is invisible from the map, which
only lists the C#-level `payPalRequestId` parameter; confirmed by reading the request-builder call in each
generated method body.)*

### 2.5 Payments — capture / reauthorize / void / refund (Flow 1 fulfil/cancel/refund steps)

| Op (`client.Payments.…`) | Signature | Request | Response | Error |
|---|---|---|---|---|
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CapturedPayment>` | `CaptureRequest { Amount (amount): Money?, InvoiceId?, FinalCapture (final_capture): bool? = false, PaymentInstruction?, NoteToPayer?, SoftDescriptor? }` | `CapturedPayment { Status (status): CaptureStatus?, StatusDetails, Id (id): string?, Amount, FinalCapture, SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?, DisbursementMode, Links, ProcessorResponse, CreateTime, UpdateTime, … }`; `SellerReceivableBreakdown { GrossAmount (gross_amount): Money !req, PaypalFee (paypal_fee): Money?, PaypalFeeInReceivableCurrency, NetAmount (net_amount): Money?, ReceivableAmount, ExchangeRate, PlatformFees }` | `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback |
| `ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>` | `ReauthorizeRequest { Amount (amount): Money? }` | `PaymentAuthorization { Status: AuthorizationStatus?, Id, Amount, ExpirationTime, … }` | `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback |
| `VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>` | — (no body) | `PaymentAuthorization` (same shape) | `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback |
| `RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Refund>` | `RefundRequest { Amount (amount): Money?, CustomId?, InvoiceId?, NoteToPayer?, PaymentInstruction? }` — omit `Amount` entirely for a full refund; set it for a partial refund | `Refund { Status (status): RefundStatus?, Id, Amount, SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown? }`; `SellerPayableBreakdown { GrossAmount, PaypalFee (paypal_fee), NetAmount (net_amount), TotalRefundedAmount (total_refunded_amount): Money?, … }` | `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback |
| `GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>` | — | `PaymentAuthorization` | `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` fallback |

*(map: `map/operations/Payments.md`; `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`.)*

**Full-amount capture**: `AuthorizationStatus`/`CaptureStatus` do not document what an omitted `CaptureRequest.Amount`
defaults to (the source XML doc only says the request "captures either a portion or the full authorized
amount", not which happens when `Amount` is null). Defensive-coding directive (grounded in the fact that
`Authorization`/`PaymentAuthorization.Amount: Money?` is already on hand from the authorize/get response): **do
not omit `Amount`** — always pass `CaptureRequest.Amount` explicitly equal to the authorization's own `Amount`,
and set `FinalCapture = true`, when the intent is a full capture. Label the omit-for-full-capture behavior
**UNVERIFIED**.

**Stale/expired authorization, reauthorize vs. terminally expired**: `AuthorizationStatus` has no `Expired`
member (`Created, Captured, Denied, PartiallyCaptured, Voided, Pending` only — `enums.md`). The only
proactively-checkable signal is `Authorization`/`PaymentAuthorization.ExpirationTime (expiration_time):
string?` — compare it to current time before attempting capture. Beyond that, whether an authorization is
"reauthorizable" (day 4–29) vs. "no longer reauthorizable at all" (day 30+) is a PayPal business rule the
generated contract does not encode as a distinct status or error type — both cases surface as the same
`SdkException<ReauthorizePaymentError>`/`SdkException<CaptureAuthorizedPaymentError>` Case-A shape
(`TryGetError(out Error)`, `Error { Name !req, Message !req, DebugId !req, Details:
IReadOnlyList<ErrorDetails>? }`, `ErrorDetails { Issue !req, Field?, Value?, Location?, Description? }`).
**UNVERIFIED**: the exact `Error.Name`/`ErrorDetails.Issue` string(s) PayPal returns for "expired, not
reauthorizable" vs. any other 422. Defensive-coding directive: on a `CaptureAuthorizedPayment` or
`ReauthorizePayment` failure, extract best-effort via `ex.Error.TryGetError(out var err)`, log `err.Name`,
`err.DebugId`, and every `err.Details[].Issue`/`.Description` you get; do **not** hard-match a specific issue
string to decide "give up" vs. "retry reauthorize" — surface the raw name/issue/debug-id to an operator and
let a human (or a documented allow-list you maintain and can update independently of this plan) decide,
rather than silently swallowing an unrecognized code as success or as a hard failure.

**Over-refund**: `RefundCapturedPaymentError`'s accessor list includes status `422` (map row, §2.5 table above)
— that is direct evidence PayPal validates and can reject a refund server-side (rather than this being purely
your responsibility to pre-check). `Refund.SellerPayableBreakdown.TotalRefundedAmount` on each refund response
also gives you the cumulative refunded-so-far amount for free. Defensive-coding directive: do **not** attempt
to pre-block an over-refund locally as the source of truth — issue the `RefundCapturedPayment` call and treat
any `422` (via `TryGetError`) as the authoritative rejection; optionally track `TotalRefundedAmount` locally
for UX display only. The exact `Issue` string for "exceeds remaining refundable amount" is **UNVERIFIED**
(same defensive extraction as above).

**Idempotency**: `payPalRequestId` on `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, and
`RefundCapturedPayment` all map to header `PayPal-Request-Id` — reuse the same string across a retry of the
same logical operation. Same auto-`Idempotency-Key` caveat as §2.4 applies to all four (SDK source:
`Api/Payments.cs`).

**`prefer` default is `"return=minimal"`** on all four write ops here too — pass `prefer:
"return=representation"` explicitly to get `SellerReceivableBreakdown`/`SellerPayableBreakdown`/full
`Authorization` fields back (same source doc-comment as §2.4).

### 2.6 Reconciliation — transaction search

| Op | Signature | Request (query, wire ← C#) | Response |
|---|---|---|---|
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<SearchResponse>` | `start_date`←`startDate`, `end_date`←`endDate`, `transaction_id`←`transactionId`, `page_size`←`pageSize`, `page`←`page`, … (full list in map) | `SearchResponse { TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?, Page (page): int?, TotalItems (total_items): int?, TotalPages (total_pages): int?, StartDate, EndDate, Links }` |

`TransactionDetails { TransactionInfo (transaction_info): TransactionInformation?, PayerInfo, ShippingInfo,
CartInfo, StoreInfo, AuctionInfo, IncentiveInfo }`. `TransactionInformation` fields you'll read:
`TransactionId (transaction_id): string?`, `PaypalReferenceId (paypal_reference_id): string?` +
`PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` (members: `Odr (ODR)` = order,
`Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` — use `Odr` to line a transaction up against one of your order ids;
**there is no dedicated capture-id field** — correlation below order level is not directly modeled),
`TransactionAmount (transaction_amount): Money?`, `TransactionStatus (transaction_status): string?` (**plain
string, not an enum** — the set of values PayPal actually sends is documentation-only and outside the
generated contract; treat unrecognized values as "unrecognized", never throw on one),
`TransactionInitiationDate`/`TransactionUpdatedDate` (both `string?`, RFC-3339). *(map:
`map/operations/TransactionSearch.md`, `map/models/records-2-Pa-Ve.md` rows `SearchResponse`,
`TransactionDetails`, `TransactionInformation`; `enums.md` row `PayPalReferenceIdType`.)*

**Date-range cap**: the SDK's own `endDate` parameter doc states **"The maximum supported range is 31 days"**
— exceeding it is rejected, not silently truncated. For any caller-supplied range longer than 31 days, split
it into consecutive ≤31-day windows and issue one `SearchTransactions` call per window. *(SDK source:
`Api/TransactionSearch.cs` XML `<param name="endDate">` doc — a real map-side gap, since `TransactionSearch.md`
does not carry this constraint.)*

**Full-range pagination within a window**: loop `page = 1, 2, …` (the `pageSize`/`page` params, default
`pageSize=100`), reading `SearchResponse.TotalPages` from each response and stopping once `page > TotalPages`.
No `perPage`/cursor alternative exists — `page`/`pageSize` is the only mechanism. Error case is **B**
(`SdkException<RawError>`) for `SearchTransactions` — read via `.StatusCode`/`.ReadAsString()`/`.ReadAsJson<T>()`,
**not** a typed `TryGetError`. **UNVERIFIED**: no explicit max-`pageSize` value is stated anywhere in the map
or source; keep the SDK's own default (`100`) rather than guessing a larger number.

Sandbox transaction-reporting lag (up to ~3 hours per `SearchBalances`'/`SearchTransactions`' own doc notes) is
expected PayPal sandbox behavior, not a bug to route around.

### 2.7 Amounts and currency

- Every amount is `Money { CurrencyCode (currency_code): string !req, Value (value): string !req }`.
- `Value` wire format: regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`, max length 32 — a **decimal string**
  (e.g. `"19.99"`), not integer minor units, not a `decimal`/`double` on the wire. Build it with
  `amount.ToString("F2", CultureInfo.InvariantCulture)` (or the currency's correct decimal-place count — most
  are 2, some like JPY are 0) so it matches your order total to the cent; never interpolate a `decimal`
  directly without a fixed format, since default `ToString()` can drop trailing zeros or use a
  culture-dependent separator.
- `CurrencyCode`: three-character ISO-4217, `StringLength(3,3)` — bind straight from `PayPal:Currency`.
- Every `Money`-typed field in this plan (`PurchaseUnitRequest.Amount.Value`/`.CurrencyCode`,
  `CaptureRequest.Amount`, `RefundRequest.Amount`, `ReauthorizeRequest.Amount`) uses this same shape.
  *(source: `Models/Money.cs`.)*

### 2.8 Error handling — shared across every op above

- **Case A** (39 of 40 ops, including every op in §2.3–2.5): `catch (SdkException<{Op}Error> ex)` →
  `ex.Error.TryGet…(out var typed)` for the status-specific shape named in that op's row above, else
  `ex.Error.TryGetRawError(out var raw)`. The Vault ops use `Error1`/`TryGetError1`; the Orders/Payments ops
  use `Error`/`TryGetError` — **do not assume one shared error type across controllers**, they're separate
  generated records (`Error` vs `Error1`, `ErrorDetails` vs `ErrorDetails1`) even though their fields line up
  field-for-field.
- **Case B** (`SearchTransactions` only, §2.6): `catch (SdkException<RawError> ex)` → `ex.Error.StatusCode`,
  `.ReadAsString()`, `.ReadAsJson<T>()`.
- `Error`/`Error1` fields you read: `Name (name): string !req` (top-level PayPal error code, e.g. the
  generic category), `Message (message): string !req`, `DebugId (debug_id): string !req` (surface this in
  logs/support tickets), `Details (details): IReadOnlyList<ErrorDetails>?` → each `ErrorDetails.Issue (issue):
  string !req` (the specific machine-readable sub-code). *(map: `records-1-Ac-Pa.md` rows `Error`, `Error1`,
  `ErrorDetails`, `ErrorDetails1`.)*
- `CaptureAuthorizedPayment`/`ReauthorizePayment`/`VoidPayment`/`RefundCapturedPayment`/`GetAuthorizedPayment`
  all additionally expose `TryGetNoContent(out RawError)` for a `500` — check it after `TryGetError` and
  before `TryGetRawError`.
- **`TRANSACTION_REFUSED` (and every other `ErrorDetails.Issue` string) — confirmed undocumented in this
  generated contract.** `ErrorDetails.Issue` is `required string`, doc-comment *"The unique, fine-grained
  application-level error code"* — no enum, no value list, no map/source note distinguishing
  `TRANSACTION_REFUSED` from any other `422` issue (source: `Models/ErrorDetails.cs`). This is a genuine,
  confirmed gap, not an incomplete lookup: `AuthorizeOrderError` (source: `Errors/AuthorizeOrderError.cs`)
  maps every one of `400/401/403/404/422/500` to the **same** undifferentiated `Error` shape — there is no
  per-status or per-issue typed variant to consult. One adjacent enum, `ProcessorResponseCode` (`enums.md`),
  does define a `_9540 = REFUSED_CARD` member with a similarly-worded name, but that enum lives on
  `ProcessorResponse`, attached to a **`CapturedPayment`** — i.e. only reachable *after* a successful
  `AuthorizeOrder` → capture — not on `Error`/`ErrorDetails`; it is a different model on a later step of the
  flow and is not the documented meaning of `TRANSACTION_REFUSED` here. Defensive-coding directive: log
  `Error.Name`, `Error.DebugId`, every `ErrorDetails.Issue`/`.Description`, and — not previously called out —
  every `ErrorDetails.Links[]` (doc-comment: *"HATEOAS links that are either relevant to the issue by
  providing additional information or offering potential resolutions"*, source: `Models/ErrorDetails.cs`),
  since a resolution link, if present, is the only contract-modeled place PayPal could point you at
  issue-specific guidance; do not hard-code a response to `TRANSACTION_REFUSED` specifically.
- See **REQUIRED READING** below for the two `JsonException` hazard rows — mandatory for this boundary.

---

## 3. Trap notes

⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline must be long-lived and reused via
`IHttpClientFactory`, not rebuilt per request; whether the SDK client wrapper itself should be transient or
singleton in *your* DI graph (the built-in `AddPayPalServerSdkClient` registers it singleton) affects how
config changes (e.g. a rotated `ClientSecret`) propagate. **MUST load `dotnet-client-initialization`** before
wiring registration.

⚠ Step 1 (auth) — set `Oauth2` before constructing the client or inside the DI `configure` callback, and load
secrets from configuration rather than hardcoding; whether/how `Oauth2TokenStrategy` affects token caching
across requests is not covered by this sheet. **MUST load `dotnet-authentication`**.

⚠ Steps 3–6 (every call site) — many optional parameters (`payPalMockResponse`, `payPalClientMetadataId`,
`payPalAuthAssertion`, etc.) have no C# default and must be passed explicitly (`null` to skip); a positional
call silently mis-binds them. **MUST load `dotnet-calling-endpoints`**.

⚠ Steps 2–7 (every model touched) — enums here are `StringEnum<T>`, not C# enums (construct via the static
member or `Type.FromValue`); unmodeled/renamed JSON fields are silently dropped on deserialize, which matters
if PayPal ever adds fields this generated release doesn't know about. **MUST load `dotnet-models`**.

⚠ Step 9 (error boundary, all steps) — which read/list/find/delete-shaped ops are Case B vs Case A, and
whether a no-throw `…Result` variant exists, must be re-confirmed per operation rather than assumed uniform
(`SearchTransactions` is the one Case-B exception here; the rest of §2.3–2.5 are Case A). `TryGetRawError` is
not a catch-all on a typed error. **MUST load `dotnet-error-handling`**.

⚠ Step 6 (refund, and every write in §2.4/§2.5) — the SDK's built-in `RetryOptions.HttpMethodsToRetry` gates
only the **status**-triggered retry; a **transport failure** (`HttpRequestException`) is retried on **every**
verb including `POST`, so a `RefundCapturedPayment`/`AuthorizeOrder`/`CaptureAuthorizedPayment` call can
execute more than once purely from a connection blip even with conservative retry config — this is exactly
why `payPalRequestId` must be set and reused, not left as a "nice to have". **MUST load
`dotnet-configuration-resilience`** before tuning retries/timeouts on the registered client.

⚠ Step 1 (base URL / environment) — `Timeout` in `RetryOptions` is per-attempt, not a total budget for a
multi-step flow (create→authorize, or a paginated transaction-search loop); don't assume one `Timeout` value
bounds the whole checkout or the whole reconciliation sweep. **MUST load `dotnet-configuration-resilience`**.

⚠ Step 7 (testing) — the `HttpClient` constructor argument is the test seam for every op above; match the
project's existing test framework/assertion style rather than introducing a new one. **MUST load
`dotnet-testing`** before writing tests for this integration layer.

---

## 4. REQUIRED READING

Load every skill below **before implementation starts** — this sheet deliberately does not carry their
contents.

- `dotnet-client-initialization` — governs Step 1 (client construction, DI registration, `HttpClient`
  lifetime).
- `dotnet-authentication` — governs Step 1 (setting `OAuth2ClientCredentials`, credential rotation).
- `dotnet-calling-endpoints` — governs Steps 3–6 (every `client.{Group}.{Operation}(...)` call site; named
  vs. positional arguments).
- `dotnet-models` — governs Steps 2–7 (building every request record, reading every response record, enum
  and union handling).
- `dotnet-error-handling` — governs Step 9 (the error boundary for every step). **Mandatory** two hazard
  rows, verbatim:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `System.Text.Json.JsonException` *while the error object is being constructed*, so the
    `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary
    that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a
    caller that retries 5xx retries something that can never succeed.
- `dotnet-configuration-resilience` — governs Step 1 (base-URL override, timeouts) and Step 6/every write
  (retry semantics vs. idempotency keys).
- `dotnet-testing` — governs testing this integration layer.

---

## 5. Assumptions & Blockers

- **Blocker-adjacent (flagged, not a build-stopper)**: this SDK release's `ServerEnvironment` has only one
  member, `Sandbox` (source: `Servers/ServerEnvironment.cs`). `PayPal:Environment = "live"`/`"production"`
  cannot select a distinct SDK-modeled environment. The only way to reach a live PayPal host at all is to
  override `options.Server.Default.Sandbox.BaseUrl` to a production URL (e.g. `https://api-m.paypal.com`) —
  which is exactly what `PayPal:BaseUrl` already gives you, so this plan treats `PayPal:BaseUrl` as the *only*
  supported way to run against anything other than PayPal's sandbox host in this generated release, and
  treats `PayPal:Environment` as informational/logging-only (it does not change SDK behavior beyond always
  selecting the single `Sandbox` enum member). If true production support requires a different `ServerEnvironment`
  member, that requires a newer SDK generation — out of scope to invent here.
- Assumed the sandbox business account already has vaulting/direct-card-processing enabled, per the brief;
  this cannot be confirmed or denied from the SDK surface (it's an account-capability flag, not a modeled
  field).
- This plan does not use `Orders.ConfirmOrder` (a separate confirm-payment-source operation, typically for
  wallet-style payer-confirmation flows) — the direct-card/vault-token flow goes through
  `AuthorizeOrder`'s own `PaymentSource` per its documented remarks (§2.4). If a future requirement needs
  `ConfirmOrder`, its contract (`ConfirmOrderRequest { PaymentSource !req, ApplicationContext? }`) is on
  `map/operations/Orders.md` and `records-1-Ac-Pa.md` but is not resolved here. **Update**: source evidence
  (`CreateOrder`'s `payPalRequestId` doc-comment, §2.4) surfaces a second, documented "single-step create
  order" pattern (`payment_source` on `CreateOrder` itself) that this plan also does not use and has not yet
  ruled in or out as the fix for the live-sandbox `TRANSACTION_REFUSED` finding below — see §2.4's
  next-diagnostic-step directive.
- **Live-sandbox finding (updated)**: the same Visa `4111111111111111` succeeds on `Vault.CreatePaymentToken`
  (raw card, no vault id) but is refused (`422`, `Error.Name = "UNPROCESSABLE_ENTITY"`, one `ErrorDetails`
  with `Issue = "TRANSACTION_REFUSED"`) on `Orders.AuthorizeOrder`, tried so far with: raw card and `VaultId`
  (two-step), `payment_source` on `CreateOrder` itself with `AuthorizeOrder` `body: null` (single-step),
  `Attributes.Verification.Method` = `ScaWhenRequired` and `ScaAlways`, varying amounts, with and without a
  full billing address. A **1167-transaction successful history** on this same sandbox account (via
  `TransactionSearch.SearchTransactions`, confirmed "S"-status past transactions with realistic e-commerce
  amounts) is direct evidence the account's direct-card capability is not itself disabled — narrowing this to
  a request-shape or per-attempt-decisioning question rather than a blanket account-capability gap. Three
  contract-grounded, untried avenues remain (Verification.Method and single-step `CreateOrder` are now
  *tried*, not just theorized): (1) **`CardRequest.Attributes.StoredCredential`** (`CardStoredCredential`,
  §2.4 new paragraph) — structurally never yet exercised; its own doc-comment compatibility rules
  (`payment_type=ONE_TIME` only compatible with `payment_initiator=CUSTOMER`, etc.) make it the closest thing
  in the whole SDK surface to an explicit "how the processor should classify this charge" signal — try
  `PaymentInitiator.Customer` + `PaymentType.OneTime` + `Usage.First`; (2) **`PurchaseUnitRequest.SupplementaryData`**
  (§2.4 new paragraph) — its doc-comment and `Api/Orders.cs`'s own `CreateOrder` remarks both explicitly tie
  Level-2/3 payment data to "reduce risk and payment processing costs"; every other untried `PurchaseUnitRequest`
  field (`Payee`, `Description`, `CustomId`, `SoftDescriptor`, `Items`, `Shipping`, `InvoiceId`) has **no**
  risk/processor-acceptance doc-comment and is not a plausible lever — ruled out by source, not by guess; (3)
  confirmed **ruled out**: `OrderRequest.Payer` is DEPRECATED and PayPal-wallet-only per its own doc-comment,
  and `OrderRequest.ApplicationContext` is deprecated field-by-field (every one of its 8 fields points at a
  different current replacement, none of them a direct-card lever) — neither is worth setting. The exact
  meaning of the `TRANSACTION_REFUSED` issue string itself is a **confirmed, permanent gap** — undocumented
  anywhere in the map or source (§2.8) — and cannot be resolved further from the SDK contract; if avenues (1)
  and (2) are tried and the refusal persists unchanged, report it as a sandbox/processor-side per-transaction
  decline (e.g. a simulated hard-decline test-card behavior or velocity/risk rule tied to this specific
  request shape), not an SDK integration defect — the 1167-transaction history means "account capability" is
  no longer the leading theory, but the SDK contract still cannot adjudicate a live processor risk decision.
- Every "UNVERIFIED" item above (exact PayPal `Issue`/`Name` error-code strings, whether an omitted
  `CaptureRequest.Amount` defaults to full capture, whether a 3DS challenge is reachable at all through
  direct-card vaulting, max `SearchTransactions` page size) is a fact the generated SDK contract does not
  encode as a type, enum, or doc-comment — resolving them further would require either live PayPal traffic or
  PayPal's REST API prose documentation, neither of which this agent is permitted to use as a source of
  contract truth. Each has a concrete defensive-coding directive above instead of an open question.
- No blockers found that require halting: every numbered capability in the brief (1–9) has an operation and
  model in this SDK surface; nothing needed a redirect/browser-approval flow to be built.
