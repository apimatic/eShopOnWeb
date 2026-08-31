# Visa/CyberSource .NET SDK — Invoicing integration contract (eShopOnWeb `src/PublicApi`)

SDK: `APIMatic.VisaCyberSource`, root namespace `CyberSourceMergedSpec`, map release `v2.0.1`
(source commit `bbc9181`). Every capability below is driven through `client.Invoices`
(`Api/Invoices.cs`, 7 operations). All amounts, currency and the payment link are **strings**
over the wire; dates are `System.DateTimeOffset`. There are **no unions** in this SDK and **no
invoice-status enum** — `status` is a plain `string` on every model.

---

## 1. Scope & sequence

1. **Register the client** (`AddCyberSourceMergedSpecClient`) binding `Visa:BaseUrl` onto the
   server override, and set the four auth env vars **before** the client is constructed.
2. **CreateInvoice** — raise a draft bill (line items, amount total, customer, USD, due date),
   leaving it not-sent (`InvoiceInformation.SendImmediately = false`, the default).
3. **GetInvoice** — read current status, `InvoiceHistory` (how it got there), and — once sent —
   `InvoiceInformation1.PaymentLink`.
4. **UpdateInvoice** (HTTP **PUT**, full replace) — correct due date / customer on a draft.
5. **PerformSendAction** (POST `.../delivery`) — issue/deliver the draft to the customer.
6. **PerformCancelAction** (POST `.../cancelation`) — withdraw a created/sent invoice.
7. **GetAllInvoices** — paginate (offset/limit) for the reconciliation report. **See Blocker B1:
   there is no server-side date-range filter.**

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
> namespace). Enums, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### Namespaces used by this integration

| Type(s) | Namespace |
|---|---|
| `CyberSourceMergedSpecClient`, `CyberSourceMergedSpecClientOptions`, `ServerOptions` | `CyberSourceMergedSpec` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.ProductionOptions`) | `CyberSourceMergedSpec.Servers` |
| `Invoices` controller (`client.Invoices`) | `CyberSourceMergedSpec.Api` |
| All request/response records below | `CyberSourceMergedSpec.Models` |
| `CreateInvoiceError`, `GetInvoiceError`, `UpdateInvoiceError`, `PerformSendActionError`, `PerformCancelActionError`, `GetAllInvoicesError` (and the `…PublishActionError`) | `CyberSourceMergedSpec.Errors` |
| `SdkException<T>` | `CyberSourceMergedSpec.Core.Exceptions` |
| `RawError`, `ApiError` | `CyberSourceMergedSpec.Core.ErrorResponse` |

### Operations

| # | Call (`client.Invoices.…`) | Request model | Response type | Error case + accessors | Source |
|---|---|---|---|---|---|
| 1 CREATE | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateInvoiceRequest` | `InvoicingV2InvoicesPost201Response` | A: `SdkException<CreateInvoiceError>` — `TryGetInvoicingV2InvoicesPost400Response1(out …)` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError(out RawError)` | `operations/Invoices.md` |
| 2 GET | `GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesGet200Response` | A: `SdkException<GetInvoiceError>` — `TryGetInvoicingV2InvoicesGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | `operations/Invoices.md` |
| 3 UPDATE (PUT) | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `UpdateInvoiceRequest` | `InvoicingV2InvoicesPut200Response` | A: `SdkException<UpdateInvoiceError>` — `TryGetInvoicingV2InvoicesPut400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | `operations/Invoices.md` |
| 4 SEND | `PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesSend200Response` | A: `SdkException<PerformSendActionError>` — `TryGetInvoicingV2InvoicesSend400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | `operations/Invoices.md` |
| 5 CANCEL | `PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesCancel200Response` | A: `SdkException<PerformCancelActionError>` — `TryGetInvoicingV2InvoicesCancel400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | `operations/Invoices.md` |
| 6 LIST | `GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (query `offset`,`limit`,`status`) | `InvoicingV2InvoicesAllGet200Response` | A: `SdkException<GetAllInvoicesError>` — `TryGetInvoicingV2InvoicesAllGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | `operations/Invoices.md` |
| (alt) PUBLISH | `PerformPublishAction(string id, …)` — POST `.../publication` | — (path `id`) | `InvoicingV2InvoicesPublish200Response` | A: `SdkException<PerformPublishActionError>` — `…Publish400/404/502Response1` · `TryGetRawError` | `operations/Invoices.md` |

- **No-throw `…Result` variant: absent on every operation** (throw-only SDK). **No pagination
  helper** on any operation — `GetAllInvoices` is manual offset/limit.
- `status` on `GetAllInvoices` is `string?` with **no C# default → must be passed explicitly**
  (pass `null` for "all"). Call with named args (`offset:`, `limit:`, `status:`) per calling-endpoints.
- Every error payload accessor `out` type (`…400Response1` / `…404Response1`) is a record with
  fields `SubmitTimeUtc (submitTimeUtc): string?`, `Status (status): string?`,
  `Reason (reason): string?`, `Message (message): string?`, `Details (details): IReadOnlyList<Detail>?`.
  The `…502Response1` shape is the same minus `Details`. Read `Reason`/`Message` for the human
  message. (Source: `records-5-In-Me.md`.)

### Request models — fields (`Name (wire_name): Type`, `!req` = C# `required`)

**`CreateInvoiceRequest`** (`records-3-Cr-Ex.md`)
| Field | Type | Notes |
|---|---|---|
| `ClientReferenceInformation (clientReferenceInformation)` | `ClientReferenceInformation78?` | optional; **not echoed in any response** — do not rely on it for correlation |
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | customer name/email/id (below) |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72?` | optional |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation` | **`!req`** — due date, description |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **`!req`** — amount + line items |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>?` | optional |

**`UpdateInvoiceRequest`** (`records-11-To-We.md`) — HTTP **PUT = full replace**, not a partial PATCH
| Field | Type | Notes |
|---|---|---|
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | updatable customer |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72?` | optional |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation4` | **`!req`** — `Description` + `DueDate` both `!req` |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **`!req`** — `AmountDetails60` with `TotalAmount`+`Currency` `!req` |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>?` | optional |

> ⚠ **Update is a whole-document PUT.** Even to change only the due date or the customer you must
> re-send `InvoiceInformation4` (Description + DueDate, both required) **and** `OrderInformation60`
> (AmountDetails60.TotalAmount + Currency, both required). Omitting the amount block will not
> compile the request (`required` init) / will replace the order with an empty one — re-hydrate
> the amount and line items from the order every time.

**`InvoiceInformation`** — create body (`records-5-In-Me.md`, `Models/InvoiceInformation.cs`)
`InvoiceNumber (invoiceNumber): string?`, `Description (description): string !req`,
`DueDate (dueDate): DateTimeOffset !req`, `ExpirationDate (expirationDate): DateTimeOffset?`,
`SendImmediately (sendImmediately): bool? = false`, `AllowPartialPayments (allowPartialPayments): bool? = false`,
`DeliveryMode (deliveryMode): string?`.
→ **Draft = leave `SendImmediately` false (default) and do not call `PerformSendAction`.**

**`InvoiceInformation4`** — update body (`records-5-In-Me.md`)
`Description (description): string !req`, `DueDate (dueDate): DateTimeOffset !req`,
`ExpirationDate (expirationDate): DateTimeOffset?`, `SendImmediately (sendImmediately): bool? = false`,
`AllowPartialPayments (allowPartialPayments): bool? = false`, `DeliveryMode (deliveryMode): string?`.

**`OrderInformation60`** — create + update body (`records-6-Me-Pa.md`)
`AmountDetails (amountDetails): AmountDetails60 !req`, `LineItems (lineItems): IReadOnlyList<LineItem17>?`.

**`AmountDetails60`** (`records-1-Ac-Bi.md`)
`TotalAmount (totalAmount): string !req`, `Currency (currency): string !req` (= `"USD"`),
`DiscountAmount`, `DiscountPercent`, `SubAmount`, `MinimumPartialAmount` (all `string?`),
`TaxDetails (taxDetails): TaxDetails13?`, `Freight (freight): Freight?`.

**`LineItem17`** (`records-5-In-Me.md`)
`ProductSku (productSku): string?`, `ProductName (productName): string?`,
`Quantity (quantity): int? = 1`, `UnitPrice (unitPrice): string?`, `DiscountAmount`,
`DiscountPercent`, `TaxAmount`, `TaxRate`, `TotalAmount` (all `string?`).
→ product name + unit price + quantity per the brief; **all money fields are strings**.

**`CustomerInformation`** — create/update + also the shape returned by Get/Cancel/Send/Publish/Put
(`records-3-Cr-Ex.md`)
`Name (name): string?`, `Email (email): string?`, `MerchantCustomerId (merchantCustomerId): string?`,
`Company (company): Company6?`.
→ **`MerchantCustomerId` is the only app-supplied identifier echoed back in the list entry**
(see reconciliation note).

### Response envelopes — fields the integration reads

Create/Get/Put/Send/Cancel/Publish all share the same top-level shape; **the payload is NOT wrapped
in a single field** — read fields directly off the response object.

**`InvoicingV2InvoicesPost201Response`** (create) / **`InvoicingV2InvoicesGet200Response`** (get) /
**`InvoicingV2InvoicesPut200Response`** / **`…Send200Response`** / **`…Cancel200Response`** /
**`…Publish200Response`** (`records-5-In-Me.md`):
`Links (_links): Links251?`, `Id (id): string?`, `SubmitTimeUtc (submitTimeUtc): string?`,
`Status (status): string?`, `CustomerInformation (customerInformation): CustomerInformation?`,
`ProcessingInformation (processingInformation): ProcessingInformation72?`,
`InvoiceInformation (invoiceInformation): InvoiceInformation1?`,
`OrderInformation (orderInformation): OrderInformation61?`,
`MerchantDefinedFieldValuesWithDefinition (…): IReadOnlyList<…>?`
— **plus, on Get only:** `InvoiceHistory (invoiceHistory): IReadOnlyList<InvoiceHistory>?`.

- **Provider invoice id** ← `resp.Id` (string).
- **Status** ← `resp.Status` (string; no enum — do not hardcode literals, see UNVERIFIED note).
- **Payment link** ← `resp.InvoiceInformation.PaymentLink` (see `InvoiceInformation1` below).
- **Amounts** ← `resp.OrderInformation.AmountDetails` (`AmountDetails61`: `TotalAmount`, `Currency`,
  `BalanceAmount`, all `string?`) and `resp.OrderInformation.LineItems` (`IReadOnlyList<LineItem17>?`).
- **Customer** ← `resp.CustomerInformation` (`Name`, `Email`, `MerchantCustomerId`).
- **Due date** ← `resp.InvoiceInformation.DueDate`.

**`InvoiceInformation1`** — response invoice block (`records-5-In-Me.md`)
`InvoiceNumber (invoiceNumber): string?`, `Description (description): string?`,
`DueDate (dueDate): DateTimeOffset?`, `ExpirationDate (expirationDate): DateTimeOffset?`,
`AllowPartialPayments (allowPartialPayments): bool? = false`,
**`PaymentLink (paymentLink): string?`**, `DeliveryMode (deliveryMode): string?`,
`CustomLabels (customLabels): IReadOnlyList<CustomLabel>?`.

**`InvoiceHistory`** (Get response, "how it got there") (`records-5-In-Me.md`)
`Event (event): string?`, `Date (date): DateTimeOffset?`,
`TransactionDetails (transactionDetails): TransactionDetails?`.

**`InvoicingV2InvoicesAllGet200Response`** (list) (`records-5-In-Me.md`)
`Links (_links): Links251?`, `SubmitTimeUtc (submitTimeUtc): string?`,
`TotalInvoices (totalInvoices): int?`, `Invoices (invoices): IReadOnlyList<Invoice1>?`.
→ page until `offset >= TotalInvoices` (manual loop; no auto-pager).

**`Invoice1`** — each list entry (`records-4-Fe-In.md`)
`Links (_links): Links251?`, `Id (id): string?`, `Status (status): string?`,
`CreatedDate (createdDate): string?`, `CustomerInformation (customerInformation): CustomerInformation2?`,
`InvoiceInformation (invoiceInformation): InvoiceInformation2?`,
`OrderInformation (orderInformation): OrderInformation62?`.
- `CustomerInformation2` (`records-3`): `Name (name): string?`, `MerchantCustomerId (merchantCustomerId): string?`.
- `InvoiceInformation2` (`records-5`): `DueDate (dueDate): DateTimeOffset?`, `ExpirationDate (expirationDate): DateTimeOffset?` — **no InvoiceNumber, no PaymentLink in the list entry.**
- `OrderInformation62` (`records-6`) → `AmountDetails62` (`records-1`): `TotalAmount (totalAmount): string?`, `Currency (currency): string?`.

> **Reconciliation identity — YOUR CALL, driven by an SDK constraint.** A list `Invoice1` carries
> only: `Id`, `Status`, `CreatedDate`, customer `Name` + `MerchantCustomerId`, due/expiration
> dates, and amount total+currency. It does **not** carry `invoiceNumber`,
> `clientReferenceInformation`, or merchant-defined fields. Since the provider account also holds
> bills that are not this app's, the **only** app-controlled key echoed in the list is
> `CustomerInformation.MerchantCustomerId` — set it on create to a value the app can map back, or
> fall back to `GetInvoice(id)` per entry (richer, but N calls). Choosing the correlation strategy
> is the implementer's call. `| correlation key | MerchantCustomerId, or per-id GetInvoice | YOUR CALL — not in the map |`

### Enums

**None apply to invoicing.** `status`, `deliveryMode`, and all amounts are plain strings — there is
no `StringEnum` for invoice state in this SDK (`enums.md`: the only invoice-adjacent enum is
`ReferenceType`, unrelated to these operations).

### Client construction, base URL, auth

- **Construct:** `new CyberSourceMergedSpecClient(HttpClient httpClient, CyberSourceMergedSpecClientOptions options)`,
  or DI `services.AddCyberSourceMergedSpaceClient(o => { … })` → **`AddCyberSourceMergedSpecClient`**
  (`ServiceCollectionExtensions.cs`). `sdk-map.md`.
- **Environment:** `options.Environment = ServerEnvironment.Production` — the **only** member
  (`Servers/ServerEnvironment.cs`). Its default base URL is the **sandbox** host
  `https://apitest.cybersource.com/`, so "Production" is the sandbox by default — bind your own URL.
- **Custom base URL (bind `Visa:BaseUrl`, route every call through it):**
  set `options.Server.Default.Production.BaseUrl = <Visa:BaseUrl value>;`
  — `ServerOptions.Default` is a `DefaultOptions`; `DefaultOptions.Production` is a
  `ProductionOptions` whose `BaseUrl` (default `"https://apitest.cybersource.com/"`) is the literal
  base address every request is built against (`Servers/DefaultOptions.cs`, confirmed in source).
  Bind `Visa:BaseUrl` from `IConfiguration` and assign it here; there is no other base-URL knob.
- **Auth: this SDK has NO credentials property.** See the ⚠ auth trap and REQUIRED READING.

---

## 3. Trap notes

> ⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline must be long-lived and reused (not
> rebuilt per request); how the SDK client wraps it and how to register it in ASP.NET Core is not
> visible in the signature. **MUST load `dotnet-client-initialization`** before wiring the client.

> ⚠ Step 1 (auth) — this SDK breaks the APIMatic pattern: the merged spec declares **no security
> scheme**, so `CyberSourceMergedSpecClientOptions` carries **no credentials property** and nothing
> is wired into the `IAuthScheme` pipeline. Every request is signed by an **opt-in HTTP Signature
> `SdkHook`** configured from environment variables (`VISA_MERCHANT_ID`, `VISA_KEY_ID`,
> `VISA_SECRET_KEY`, plus an enable switch) that are read **once, inside the client constructor** —
> so they must be set **before** the client is constructed, and leaving the switch unset does not
> disable auth, it sends **every request unsigned** while appearing to work. The exact variable
> names (including the switch and the value it must hold) and the hook-wiring are **not resolved
> here**. **MUST load `dotnet-authentication`** before wiring auth or the first call.

> ⚠ Step 7 (base URL / resilience) — the SDK's retry/timeout options do **not** bound a whole call
> and are **not** the `HttpClient` timeout; `HttpMethodsToRetry` gates only the status trigger while
> a transport failure is retried on every verb (so a create/send POST can execute more than once),
> and pagination/logging need explicit wiring. Whether a failed write can be safely re-sent is a
> resilience question, not a call-site one. **MUST load `dotnet-configuration-resilience`** before
> tuning retries/timeouts or the base-URL override.

> ⚠ Steps 2–6 (models) — response `status`/amounts are strings, request amounts/currency are
> strings, enums (elsewhere) are `StringEnum<T>` not C# enums, and unmodeled JSON is dropped on
> deserialize. How to build required-init records and read nested optionals safely is not in the
> signature. **MUST load `dotnet-models`** before constructing payloads or mapping responses.

> ⚠ All steps (error boundary) — see REQUIRED READING; the two `JsonException` directions and the
> Case-A accessor mechanics are load-bearing. **MUST load `dotnet-error-handling`**.

> ⚠ Testing — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`**
> before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — HTTP Signature `SdkHook`, the four env vars + switch, constructor-time read (this SDK's auth is unlike every other APIMatic .NET SDK) |
| `dotnet-configuration-resilience` | Step 7 — base-URL override semantics, retries/timeouts, manual pagination, logging |
| `dotnet-calling-endpoints` | Steps 2–7 — named-argument calls (esp. `GetAllInvoices` `status:`), async/cancellation |
| `dotnet-models` | Steps 2–6 — required-init records, string amounts, StringEnum, dropped-field behaviour |
| `dotnet-error-handling` | All steps — the exception boundary (always required) |
| `dotnet-testing` | Tests — faking the `HttpClient` seam |

**Two hazard rows that MUST shape the error boundary from the first version (not a later revision):**

- A drifted or malformed **2xx** body (a missing `required` member, or a field whose live type
  differs from the generated one) surfaces as a `System.Text.Json.JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- A1. The app supplies its own money values as strings already formatted for the wire (SDK amount
  fields are all `string`). Formatting/rounding is the app's decision.
- A2. USD is passed as the literal `"USD"` in `AmountDetails60.Currency`.
- A3. "Draft" is achieved by `InvoiceInformation.SendImmediately = false` (default) and not calling
  `PerformSendAction`. No explicit "draft" flag exists in the SDK.
- A4. Correlation of provider invoices to app records will use `CustomerInformation.MerchantCustomerId`
  (the only app-supplied field echoed in list entries) unless the implementer prefers per-id
  `GetInvoice`. (See reconciliation YOUR CALL note.)

**Blockers**
- **B1 (blocks the reconciliation requirement as specified).** `GetAllInvoices` exposes **only**
  `offset`, `limit`, and `status` query parameters — **there is NO from/to date-range filter** in
  the SDK (`operations/Invoices.md`). A "raised in an ISO-8601 datetime range" report cannot be
  filtered server-side. The app must page the full list (offset/limit up to `TotalInvoices`) and
  filter client-side on `Invoice1.CreatedDate` — which is a **`string?`**, whose exact format is not
  documented in the map (parse defensively). Decide with the requester whether full-list paging +
  client-side date filtering is acceptable, or whether reconciliation should key off app-side
  records instead. This is a capability gap, not a data path to invent around.

**UNVERIFIED (live-traffic only; code defensively)**
- **U1 (send vs publish).** Two operations can plausibly "issue" a draft: `PerformSendAction`
  (POST `.../delivery`) and `PerformPublishAction` (POST `.../publication`). Operation rows carry
  **no remarks** (this SDK generates none), so the map cannot confirm which transition sends the
  bill to the customer and populates `PaymentLink`; the source has no `<remarks>` either. Plan uses
  **`PerformSendAction`** as the deliver-to-customer op. Verify against live/provider behaviour;
  after issuing, **read `PaymentLink` from the response and treat null/empty as "not yet available"**
  rather than assuming which call produced it. `UNVERIFIED`.
- **U2 (update/cancel on a non-draft).** Whether the provider refuses an `UpdateInvoice` or
  `PerformCancelAction` on an already-sent or already-cancelled invoice is a provider business rule
  the SDK does not enforce and the map does not document (no remarks). A refusal would most likely
  surface as `SdkException<UpdateInvoiceError>` / `<PerformCancelActionError>` with the **400**
  accessor payload (`Reason`/`Message`); treat 400 as a state-conflict signal, **extract
  `Reason`/`Message` best-effort and fall back to the raw error** — do not assume a specific status
  beyond the generated 400/404/502 set. `UNVERIFIED`.
- **U3 (draft status string).** The exact `status` string a freshly created draft carries is not in
  the map (plain string, no enum). Read `resp.Status` and branch on observed values / persist it
  verbatim; do not hardcode a literal. `UNVERIFIED`.
- **U4 (required-ness beyond generated flags).** With no operation remarks in this SDK, there is no
  documented signal for which *optional* fields the provider actually insists on. The sheet carries
  the fields the scope needs and the generated `!req` flags; required-ness beyond those flags is
  **not verified** — it was not checked because the map cannot show it. `UNVERIFIED`.
