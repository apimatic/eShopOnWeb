# Visa / CyberSource .NET SDK — Integration plan: customer invoicing for eShopOnWeb

SDK: `APIMatic.VisaCyberSource` · root namespace `CyberSourceMergedSpec` · client
`CyberSourceMergedSpecClient` · map release `v2.0.1` (source stamp `bbc9181`).
Target project: `src/PublicApi` (ASP.NET Core, .NET 8→10). All six capabilities are the
`client.Invoices` controller (`Api/Invoices.cs`, 7 operations). Currency is always `USD`.

This SDK has **no `OneOf`/`AnyOf` unions** — every field below is a plain record, enum, or scalar.

---

## 1. Scope & build order

1. **Package + DI + config wiring** — add `APIMatic.VisaCyberSource`; register the client in
   `src/PublicApi` via `AddCyberSourceMergedSpecClient`; bind `Visa:BaseUrl`; set the auth env-var
   switch. (Ops: none yet.) → trap notes T1, T2, T3.
2. **Raise a bill (draft)** — `client.Invoices.CreateInvoice`. Build line items, amounts, USD
   currency, due date, customer, and a "mine" stamp. Keep it draft (`sendImmediately = false`).
3. **Get a bill's state** — `client.Invoices.GetInvoice`. Read status, history, payment link.
4. **Correct a draft bill** — `client.Invoices.UpdateInvoice` (due date + customer).
5. **Send / issue the bill** — `client.Invoices.PerformSendAction`.
6. **Withdraw / cancel** — `client.Invoices.PerformCancelAction`.
7. **Reconciliation report over a date range** — `client.Invoices.GetAllInvoices`, paging
   offset/limit across the whole range and filtering by created-date **client-side** (see
   Blocker B1) and separating "mine" by `customerInformation.merchantCustomerId` (see B2).

> `PerformPublishAction` (`POST …/publication`) also exists on the controller but is **not** one
> of the six capabilities; it is listed in the sheet for completeness only.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own row, never from a neighbour. Namespaces in play here:
> `CyberSourceMergedSpec` (client, options, `ServerOptions`, DI extension) ·
> `CyberSourceMergedSpec.Servers` (`ServerEnvironment`) ·
> `CyberSourceMergedSpec.Api` (`Invoices` controller) ·
> `CyberSourceMergedSpec.Models` (all request/response **and** error-payload records) ·
> `CyberSourceMergedSpec.Errors` (`{Operation}Error` types) ·
> `CyberSourceMergedSpec.Core.Exceptions` (`SdkException<T>`) ·
> `CyberSourceMergedSpec.Core.ErrorResponse` (`RawError`, `ApiError`) ·
> `CyberSourceMergedSpec.Core` (`RequestOptions`) ·
> `CyberSourceMergedSpec.Core.Configuration` (`RetryOptions`).

### 2a. Operations

All live on `client.Invoices`. All are **throw-based**, all **Case A (typed)**, all have **no
no-throw `…Result` variant**, all have **no pagination helper**. Every typed error exposes the
same three status accessors plus the inherited `TryGetRawError(out RawError)` fallback.

| # | Method signature (params in order) | Request model | Returns | Error type / accessors | Source |
|---|---|---|---|---|---|
| 2 | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateInvoiceRequest` (body) | `InvoicingV2InvoicesPost201Response` | `SdkException<CreateInvoiceError>` · `TryGetInvoicingV2InvoicesPost400Response1` [400] · `…Post404Response1` [404] · `…Post502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| 3 | `GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (id in path) | `InvoicingV2InvoicesGet200Response` | `SdkException<GetInvoiceError>` · `TryGetInvoicingV2InvoicesGet400Response1` [400] · `…Get404Response1` [404] · `…Get502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| 4 | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `UpdateInvoiceRequest` (body) | `InvoicingV2InvoicesPut200Response` | `SdkException<UpdateInvoiceError>` · `TryGetInvoicingV2InvoicesPut400Response1` [400] · `…Put404Response1` [404] · `…Put502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| 5 | `PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (id in path) | `InvoicingV2InvoicesSend200Response` | `SdkException<PerformSendActionError>` · `TryGetInvoicingV2InvoicesSend400Response1` [400] · `…Send404Response1` [404] · `…Send502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| 6 | `PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (id in path) | `InvoicingV2InvoicesCancel200Response` | `SdkException<PerformCancelActionError>` · `TryGetInvoicingV2InvoicesCancel400Response1` [400] · `…Cancel404Response1` [404] · `…Cancel502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| 7 | `GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (query: offset, limit, status) | `InvoicingV2InvoicesAllGet200Response` | `SdkException<GetAllInvoicesError>` · `TryGetInvoicingV2InvoicesAllGet400Response1` [400] · `…AllGet404Response1` [404] · `…AllGet502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| (—) | `PerformPublishAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `InvoicingV2InvoicesPublish200Response` | `SdkException<PerformPublishActionError>` · `…Publish400/404/502Response1` · `TryGetRawError` | operations/Invoices.md |

Notes on operation rows: this SDK's operation rows carry **no `<remarks>`/Notes** — so which
optional fields the provider actually *requires* is documented nowhere and no compiler catches it.
Required-ness beyond the generated `required` flags below is **UNVERIFIED**.
`status` on `GetAllInvoices` is nullable with **no C# default** → you must pass it explicitly
(pass `null` for "all statuses"). Call list ops with **named arguments**.

### 2b. Request models & fields (`(wire_name): type` — `!req` = generated `required`)

**`CreateInvoiceRequest`** (`Models/CreateInvoiceRequest.cs`, records-3-Cr-Ex.md):
- `ClientReferenceInformation (clientReferenceInformation): ClientReferenceInformation78?`
- `CustomerInformation (customerInformation): CustomerInformation?`
- `ProcessingInformation (processingInformation): ProcessingInformation72?`
- `InvoiceInformation (invoiceInformation): InvoiceInformation` **!req**
- `OrderInformation (orderInformation): OrderInformation60` **!req**
- `MerchantDefinedFieldValues (merchantDefinedFieldValues): IReadOnlyList<MerchantDefinedFieldValue>?`

**`InvoiceInformation`** (create; `Models/InvoiceInformation.cs`, records-5-In-Me.md):
- `InvoiceNumber (invoiceNumber): string?`
- `Description (description): string` **!req**
- `DueDate (dueDate): DateTimeOffset` **!req**  ← the calendar due date
- `ExpirationDate (expirationDate): DateTimeOffset?`
- `SendImmediately (sendImmediately): bool? = false`  ← leave **false**/unset to keep it a draft (not sent)
- `AllowPartialPayments (allowPartialPayments): bool? = false`
- `DeliveryMode (deliveryMode): string?`

**`OrderInformation60`** (`Models/OrderInformation60.cs`, records-6-Me-Pa.md):
- `AmountDetails (amountDetails): AmountDetails60` **!req**
- `LineItems (lineItems): IReadOnlyList<LineItem17>?`

**`AmountDetails60`** (`Models/AmountDetails60.cs`, records-1-Ac-Bi.md):
- `TotalAmount (totalAmount): string` **!req**  ← amounts are **strings**, not decimals
- `Currency (currency): string` **!req**  ← set to `"USD"`
- `DiscountAmount?`, `DiscountPercent?`, `SubAmount?`, `MinimumPartialAmount?`, `TaxDetails (TaxDetails13)?`, `Freight?` — all optional strings/records

**`LineItem17`** (`Models/LineItem17.cs`, records-5-In-Me.md):
- `ProductSku (productSku): string?`, `ProductName (productName): string?`,
  `Quantity (quantity): int? = 1`, `UnitPrice (unitPrice): string?`,
  `DiscountAmount?`, `DiscountPercent?`, `TaxAmount?`, `TaxRate?`, `TotalAmount (totalAmount): string?`
  — all optional; amounts are strings.

**`CustomerInformation`** (`Models/CustomerInformation.cs`, records-3-Cr-Ex.md):
- `Name (name): string?`, `Email (email): string?`,
  `MerchantCustomerId (merchantCustomerId): string?` ← **the "mine" stamp — see B2**,
  `Company (company): Company6?` (`Company6` = `Name (name): string?`)

**`MerchantDefinedFieldValue`** (`Models/MerchantDefinedFieldValue.cs`, records-5-In-Me.md):
- `DefinitionId (definitionId): long?`, `Value (value): string?` — merchant-defined stamp on
  **create**, but **not returned on the reconciliation list** (see B2).

**`UpdateInvoiceRequest`** (`Models/UpdateInvoiceRequest.cs`, records-11-To-We.md):
- `CustomerInformation (customerInformation): CustomerInformation?`
- `ProcessingInformation (processingInformation): ProcessingInformation72?`
- `InvoiceInformation (invoiceInformation): InvoiceInformation4` **!req**
- `OrderInformation (orderInformation): OrderInformation60` **!req**  ← see hazard below
- `MerchantDefinedFieldValues (merchantDefinedFieldValues): IReadOnlyList<MerchantDefinedFieldValue>?`

**`InvoiceInformation4`** (update; `Models/InvoiceInformation4.cs`, records-5-In-Me.md):
- `Description (description): string` **!req**, `DueDate (dueDate): DateTimeOffset` **!req**,
  `ExpirationDate?`, `SendImmediately? = false`, `AllowPartialPayments? = false`, `DeliveryMode?`
  (no `invoiceNumber` on the update model).

> ⚠ **Update is NOT a partial patch.** Even though the task only changes due date + customer,
> `UpdateInvoiceRequest` marks **both** `invoiceInformation` (`InvoiceInformation4`, requires
> `Description` + `DueDate`) **and** `orderInformation` (`OrderInformation60`, whose
> `AmountDetails60` requires `TotalAmount` + `Currency`) as **required**. So an update call must
> **re-send** the description and the full amount (USD) alongside the new due date and customer.
> Read the current invoice first (op 3) and echo its amount/description back. Whether the provider
> also *rejects* an amount that differs from the original on a draft is **UNVERIFIED** (no Notes).

### 2c. Response envelopes — where id / status / payment-link / history live

Create (2), Get (3), Update (4), Send (5), Cancel (6), Publish all share the same top-level shape
(single-object envelope — **no wrapper field**, read fields directly off the response):

| Field (wire) | Type | Meaning |
|---|---|---|
| `Id (id)` | `string?` | **provider invoice id** — persist this; it is the `id` path arg for ops 3–6 |
| `Status (status)` | `string?` | **invoice status — a free string, NOT an enum** (see 2e) |
| `SubmitTimeUtc (submitTimeUtc)` | `string?` | server submit time |
| `Links (_links)` | `Links251?` | HATEOAS links: `Self`, `Update`, `Deliver`, `Cancel` (each a nested record) |
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | echoed customer (name/email/merchantCustomerId/company) |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation1?` | **holds `PaymentLink (paymentLink): string?`** ← the customer-facing pay URL, plus `InvoiceNumber`, `DueDate`, `AllowPartialPayments`, `DeliveryMode`, `CustomLabels` |
| `OrderInformation (orderInformation)` | `OrderInformation61?` | `AmountDetails (AmountDetails61)?` + `LineItems (IReadOnlyList<LineItem17>)?`; `AmountDetails61` adds `BalanceAmount` |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72?` | `RequestPhone`, `RequestShipping` (bools) — **no payment link here** |

- **`InvoicingV2InvoicesGet200Response`** additionally carries:
  `MerchantDefinedFieldValuesWithDefinition (IReadOnlyList<MerchantDefinedFieldValuesWithDefinition>)?`
  (each: `ReferenceType`, `Label`, `FieldType`, `CustomerVisible`, `ReadOnly`, `Value`,
  `Position`, `DefinitionId (int?)`, `MerchantDefinedDataIndex (int?)`, …) **and**
  `InvoiceHistory (IReadOnlyList<InvoiceHistory>)?` — each `InvoiceHistory` =
  `Event (event): string?`, `Date (date): DateTimeOffset?`, `TransactionDetails?`.
  → **status/history detail the task wants is `Status` + `InvoiceHistory` on the Get-by-id response.**
- Create/Update/Send/Cancel/Publish responses carry `MerchantDefinedFieldValuesWithDefinition?`
  but **not** `InvoiceHistory`.
- **Payment link location:** `response.InvoiceInformation.PaymentLink` — `null` until the invoice is
  **sent** (op 5). On **cancel** (op 6) the response is the same envelope; whether `PaymentLink`
  is nulled/invalidated server-side is **UNVERIFIED** — treat a cancelled invoice as no longer
  payable regardless of the returned link, and extract the link best-effort.

Source for all response records: `map/models/records-5-In-Me.md` (Post201/Get200/Put200/
Send200/Cancel200/Publish200 + `InvoiceInformation1`, `Links251`, `MerchantDefinedFieldValues‑
WithDefinition`, `InvoiceHistory`); `records-6-Me-Pa.md` (`OrderInformation61`);
`records-1-Ac-Bi.md` (`AmountDetails61`); `records-8-Pr-Pt.md` (`ProcessingInformation72`).

### 2d. Reconciliation list (`GetAllInvoices`) — `InvoicingV2InvoicesAllGet200Response`

(`Models/InvoicingV2InvoicesAllGet200Response.cs`, records-5-In-Me.md):
- `Links (_links): Links251?`
- `SubmitTimeUtc (submitTimeUtc): string?`
- `TotalInvoices (totalInvoices): int?` ← **total count for paging** across the whole range
- `Invoices (invoices): IReadOnlyList<Invoice1>?`

**`Invoice1`** (list item; `Models/Invoice1.cs`, records-4-Fe-In.md) — the fields available per
returned invoice for line-up:
| Field (wire) | Type | Reconciliation use |
|---|---|---|
| `Id (id)` | `string?` | provider invoice id |
| `Status (status)` | `string?` | status (free string) |
| `CreatedDate (createdDate)` | `string?` | **created date — a string; parse for date-range filter (see B1)** |
| `CustomerInformation (customerInformation)` | `CustomerInformation2?` | `Name (name): string?` + `MerchantCustomerId (merchantCustomerId): string?` ← **the "mine" stamp, see B2** |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation2?` | only `DueDate` + `ExpirationDate` — **no invoiceNumber, no description here** |
| `OrderInformation (orderInformation)` | `OrderInformation62?` | `AmountDetails (AmountDetails62)?` = only `TotalAmount (string?)` + `Currency (string?)` |

Sources: `records-4-Fe-In.md` (`Invoice1`), `records-3-Cr-Ex.md` (`CustomerInformation2`),
`records-5-In-Me.md` (`InvoiceInformation2`), `records-6-Me-Pa.md` (`OrderInformation62`),
`records-1-Ac-Bi.md` (`AmountDetails62`).

### 2e. Invoice status — there is NO status enum

`enums.md` lists 12 enums; **none is an invoice status.** `Status` is a plain `string?` on every
invoice response and list item. The map/source define no closed set of status values, so the
specific literals (e.g. draft / created / sent / paid / partial / cancelled) **cannot** be given
from the map — treat `Status` as an **opaque string**, compare case-insensitively, and do not
switch exhaustively on it. Any concrete status-string list is `UNVERIFIED` (not in the map).

### 2f. Error body shape (all Case-A typed payloads here are identical)

`InvoicingV2Invoices{Post|Get|Put|Send|Cancel|AllGet}{400|404|502}Response1`
(records-5-In-Me.md) all carry:
`SubmitTimeUtc (submitTimeUtc): string?`, `Status (status): string?`, `Reason (reason): string?`,
`Message (message): string?`, `Details (details): IReadOnlyList<Detail>?`.
Read via the operation's `TryGet…(out var typed)` accessor, then `typed.Reason` / `typed.Message`.
The `TryGetRawError(out RawError)` fallback gives `StatusCode` + `ReadAsString()` for any other
status. See trap T5 for the two `JsonException` directions.

> **Update refused once sent/cancelled** (op 4): the provider rejects updates on a non-draft
> invoice. This surfaces as an `SdkException<UpdateInvoiceError>` — most plausibly the **400**
> accessor (`TryGetInvoicingV2InvoicesPut400Response1` → `Reason`/`Message`), possibly 404. The
> map documents no reason string, and the exact status is **UNVERIFIED** (no Notes). Directive:
> catch `SdkException<UpdateInvoiceError>`, try the 400 then 404 accessor, else `TryGetRawError`;
> surface `Reason`/`Message` **best-effort** and fall back to the generic message — do not
> hard-code a single status or reason literal.

### 2g. Client construction, base URL, and DI

- **DI (recommended):** `services.AddCyberSourceMergedSpecClient(o => { … })` (extension in
  `ServiceCollectionExtensions.cs`, namespace `CyberSourceMergedSpec`). It calls
  `services.AddHttpClient()`, builds the `HttpClient` from `IHttpClientFactory`, and registers the
  `CyberSourceMergedSpecClient` as a **singleton**. `CyberSourceMergedSpecClientOptions` properties:
  `Environment (ServerEnvironment)`, `Retry (RetryOptions)`, `Logging`, `Server (ServerOptions)`,
  `Hooks (IReadOnlyList<SdkHook>)`. Source: `sdk-map.md` "Getting a client" + `ServiceCollectionExtensions.cs`.
- **Base URL (`Visa:BaseUrl`) — the exact override so no call bypasses it:** every controller
  shares one `Server` built from `options.Environment` + `options.Server`, and the only
  `ServerEnvironment` member is `Production` (wire `"production"`), which resolves its URL from
  `ServerOptions.Default.Production.BaseUrl` (**default `https://apitest.cybersource.com/`** — the
  sandbox host, not a prod host). Set:
  ```
  o.Server.Default.Production.BaseUrl = config["Visa:BaseUrl"];   // used verbatim for EVERY call
  ```
  Leave `o.Environment` at its default (`Production`). Because all invoice ops route through this
  single server, setting `Server.Default.Production.BaseUrl` is sufficient and unbypassable.
  Bind `Visa:BaseUrl` from configuration (`IConfiguration`), not a raw env var. Source (clone):
  `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`, `Server.cs`.
- **Auth — HTTP Signature hook, env-var driven (this SDK breaks the APIMatic pattern):** there is
  **no credentials property** on the options. `CyberSourceMergedSpecClientOptions` carries nothing
  for auth. Instead the constructor calls `VisaHttpSignatureConfigResolver.Resolve()` **once**,
  which:
  - returns `null` (→ **no signing hook added → every request is sent UNSIGNED, no error**) unless
    env var **`APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE`** equals exactly the string **`"true"`**;
  - when enabled, reads **`VISA_MERCHANT_ID`**, **`VISA_KEY_ID`**, **`VISA_SECRET_KEY`** and throws
    `VisaHttpSignatureConfigurationError` at construction if any is missing/blank;
  - appends a `VisaHttpSignatureHook` (namespace `CyberSourceMergedSpec.Core.Experimental.VisaHttpSignature`).
  Because these are read **once inside the constructor** (and the DI client is a **singleton**), all
  four env vars must be set **before** the client is first resolved. Source (clone):
  `Core/Experimental/VisaHttpSignature/VisaHttpSignatureConfigResolver.cs`,
  `CyberSourceMergedSpecClient.cs`. **This is a contract fact; the signing wiring itself → load
  `dotnet-authentication` (T2).**

---

## 3. Trap notes (name the hazard; load the skill before coding that step)

- ⚠ **T1 — Step 1 (client & DI).** The DI extension registers the client as a **singleton** over
  an `IHttpClientFactory`-built `HttpClient`; whether that handler pipeline is long-lived/reused
  correctly and how the singleton interacts with your app's lifetimes is not shown by the
  signature. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ **T2 — Step 1 (auth).** Auth is an **opt-in env-var signature hook read once at construction**,
  not a credentials property; leaving the switch unset does not disable auth — it silently sends
  every request **unsigned**, and locally it looks fine. The signing mechanics, and *where in
  startup* the four env vars must be guaranteed-set relative to client construction, are not
  something the signature reveals. **MUST load `dotnet-authentication`** before the first call.
- ⚠ **T3 — Step 1 (base URL / resilience).** The SDK's retry/timeout options do **not** bound a
  whole call and are **not** the `HttpClient` timeout; and a transport failure can re-send a
  non-idempotent write (`CreateInvoice`, `PerformSendAction`, `PerformCancelAction` are all POST).
  What `Timeout` actually bounds and which calls can re-execute is not visible in the option names.
  **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts/base URL.
- ⚠ **T4 — Steps 2–7 (models).** Amounts are **strings**, `DueDate`/`ExpirationDate` are
  `DateTimeOffset`, enums are `StringEnum<T>` (not C# enums), and unmodeled JSON is dropped on
  deserialize — how to construct these safely and map them onto domain types is not shown by the
  field list. **MUST load `dotnet-models`** before building request payloads.
- ⚠ **T5 — every step (error boundary).** Which exception types actually reach your `catch`, and how
  to read status + body without parsing `.ToString()`, is not visible from the throw-based
  signature. **MUST load `dotnet-error-handling`** before writing any try/catch. (See the two
  mandatory `JsonException` rows in REQUIRED READING.)
- ⚠ **T6 — Step 7 (calling list ops).** `GetAllInvoices` has optional params with no C# default
  (`status`) that mis-bind positionally; call it with **named arguments** and page manually with
  `offset`/`limit`/`TotalInvoices`. **MUST load `dotnet-calling-endpoints`** before writing the call.
- ⚠ **T7 — tests.** The `HttpClient` constructor argument is the test seam. **MUST load
  `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI singleton |
| `dotnet-authentication` | Step 1 — the opt-in HTTP Signature env-var hook (this SDK's non-standard auth) |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts, base-URL override, pagination semantics |
| `dotnet-calling-endpoints` | Steps 2–7 — call shapes, named args, cancellation (`ct:`) |
| `dotnet-models` | Steps 2–7 — building request models, string amounts, `DateTimeOffset`, StringEnum |
| `dotnet-error-handling` | every step — the try/catch boundary, Case-A accessors, status/body reads |
| `dotnet-testing` | tests — faking the `HttpClient` seam |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary (load
`dotnet-error-handling` before writing it):**
- A drifted or malformed **2xx** body (a missing `required` member, or a field whose live type
  differs from the generated one) surfaces as a `JsonException` from **deserialization**, **not** as
  an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

**Assumptions**
- A1 — Customer details are invented fixtures (per brief); populate `CustomerInformation.Name`/
  `Email`/`Company` freely, but **`MerchantCustomerId` must be a deterministic eShop-owned value**
  (see B2), not a fixture, or the reconciliation "mine" filter breaks.
- A2 — `Description` (required on both create and update `InvoiceInformation`) is not called out in
  the brief; derive it from the order (e.g. order number) — **YOUR CALL — not in the map**.
- A3 — `TotalAmount` and each `LineItem17.TotalAmount`/`UnitPrice` are provider-facing **strings**;
  the app formats its own order money into these (USD, 2dp). Format/rounding is **YOUR CALL — not in the map**.
- A4 — Persistence of the returned provider `Id` (and mapping it back to an eShop order) is an
  application concern; the SDK only returns it on create. **YOUR CALL — not in the map.**

**Blockers**
- **B1 — `GetAllInvoices` has NO server-side date-range filter.** Its only params are
  `offset`, `limit`, `status`. The brief's "list invoices raised in an ISO-8601 from/to range"
  therefore **cannot be filtered server-side**: the app must page the full result set
  (`offset`/`limit` up to `TotalInvoices`) and filter by `Invoice1.CreatedDate` **client-side**.
  `CreatedDate` is a `string?`; whether it is ISO-8601 is **UNVERIFIED** — parse best-effort and
  skip/So-log unparseable rows rather than throwing. Confirm this manual-paging design is
  acceptable, or the "date-range report" scope must change. (operations/Invoices.md, records-4-Fe-In.md)
- **B2 — "mine vs not-mine" on the reconciliation list is limited to `merchantCustomerId`.**
  The list item `Invoice1` does **not** carry `merchantDefinedFieldValues`, and its
  `InvoiceInformation2` has **no `invoiceNumber`** — so neither a merchant-defined field nor the
  invoice number is readable on the list. The **only** app-controllable identifier present on
  each `Invoice1` is `customerInformation.merchantCustomerId` (`CustomerInformation2.MerchantCustomerId`).
  Therefore: **stamp every created invoice with an eShop-owned, recognizable `CustomerInformation.
  MerchantCustomerId`** (e.g. a fixed prefix + order id) at create time (op 2), and filter the
  report on that prefix. `merchantDefinedFieldValues` and `invoiceNumber` remain usable for
  richer detail via **Get-by-id** (op 3) but not for the list scan. Confirm the merchantCustomerId
  stamping scheme. (records-4-Fe-In.md, records-3-Cr-Ex.md, records-5-In-Me.md)
