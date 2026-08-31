# Visa / CyberSource invoicing — implementation plan & contract sheet

Target: add customer invoicing to eShopOnWeb via the `CyberSourceMergedSpec` SDK
(NuGet `APIMatic.VisaCyberSource`, root namespace `CyberSourceMergedSpec`). All Visa
traffic routes through `client.Invoices`. Environment: CyberSource TEST (apitest) — which is
the SDK default base host. Currency USD. No callback URL — every fact about a bill is obtained
by polling `GetInvoice` / `GetAllInvoices`, never a webhook.

Map provenance for every row below: `sdk-map.md` (tag `v2.0.1`, spec stamp `bbc9181`). No SDK
source clone was needed — the map answered every contract fact; the open items are
provider-enforced semantics (this SDK's operations carry **no** `<remarks>`/Notes, so
required-ness and refusal rules beyond the generated `required` flags are undocumented) and one
genuinely missing capability (§5).

---

## 1. Scope & sequence

| # | Step | Operation(s) |
|---|---|---|
| 1 | Register client + DI; wire base URL from `Visa:BaseUrl`; set the HTTP-Signature env vars **before** the client is constructed | (client setup — no op) |
| 2 | Raise a draft invoice for an order (line items, due date, customer, USD); keep it NOT sent | `client.Invoices.CreateInvoice` |
| 3 | Read an invoice by provider id — status, status history, payment link once sent | `client.Invoices.GetInvoice` |
| 4 | Correct a still-draft invoice (due date and/or customer; amount re-sent unchanged) | `client.Invoices.UpdateInvoice` |
| 5 | Send/issue the invoice to the shopper (→ delivered, yields payment link) | `client.Invoices.PerformSendAction` |
| 6 | Cancel/withdraw an invoice | `client.Invoices.PerformCancelAction` |
| 7 | List invoices for reconciliation (see §5 Blocker — no server-side date-range filter) | `client.Invoices.GetAllInvoices` |

Note a seventh Invoices op exists — `PerformPublishAction` (`POST …/publication`). The brief's
"send/issue → delivered + payment link" maps to `PerformSendAction` (`POST …/delivery`).
`PerformPublishAction` is a **distinct** transition; which of publish vs. deliver the shopper
flow actually needs, and their ordering, is not documented in the map (no operation Notes) —
see §5 (`UNVERIFIED`).

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

**Namespaces in play** (add a `using` for each — child namespaces are NOT imported transitively):

| Type kind | Namespace | Basis |
|---|---|---|
| `CyberSourceMergedSpecClient`, `CyberSourceMergedSpecClientOptions` | `CyberSourceMergedSpec` | sdk-map §namespaces |
| Request/response records (`CreateInvoiceRequest`, `InvoicingV2Invoices*Response`, `InvoiceInformation`, `OrderInformation60`, `CustomerInformation`, `Invoice1`, `LineItem17`, `AmountDetails60`, `Links251`, `InvoiceHistory`, …) | `CyberSourceMergedSpec.Models` | sdk-map §namespaces |
| Typed error classes (`CreateInvoiceError`, `GetInvoiceError`, `UpdateInvoiceError`, `PerformSendActionError`, `PerformCancelActionError`, `GetAllInvoicesError`) | `CyberSourceMergedSpec.Errors` | sdk-map §namespaces |
| `ServerEnvironment` | `CyberSourceMergedSpec.Servers` | source path `Servers/ServerEnvironment.cs` |
| `SdkException<T>` | `CyberSourceMergedSpec.Core.Exceptions` (path-implied) | source path `Core/Exceptions/SdkException.cs` — confirm via `dotnet-error-handling` |
| `RawError` | `CyberSourceMergedSpec.Core.ErrorResponse` (path-implied) | source path `Core/ErrorResponse/RawError.cs` — confirm via `dotnet-error-handling` |
| `RetryOptions`, `ServerOptions` (client tuning) | `CyberSourceMergedSpec.Core.Configuration` (path-implied) | source path `Core/Configuration/…` — confirm via `dotnet-configuration-resilience` |

### 2a. Operations

All 7 Invoices ops are **Case A (typed)** errors, **no** no-throw `…Result` variant, **no**
pagination helper. Every op's error accessors are identical in shape: `TryGet…400Response1`
[400] · `TryGet…404Response1` [404] · `TryGet…502Response1` [502] · `TryGetRawError(out RawError)`
[fallback]. Each typed payload (e.g. `InvoicingV2InvoicesPost400Response1`) is a record with
`SubmitTimeUtc: string?`, `Status: string?`, `Reason: string?`, `Message: string?`,
`Details: IReadOnlyList<Detail>?` — extract best-effort, fall back to `Reason`/`Message`.

| Cap | Controller.Method — signature (params in order) | Request model + required members | Response envelope → fields read | Error type + typed accessor payload | Source |
|---|---|---|---|---|---|
| 2 Create | `client.Invoices.CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateInvoiceRequest`: `InvoiceInformation (invoiceInformation): InvoiceInformation` **!req**, `OrderInformation (orderInformation): OrderInformation60` **!req**; optional `CustomerInformation`, `ProcessingInformation72`, `ClientReferenceInformation78`, `MerchantDefinedFieldValues` | `InvoicingV2InvoicesPost201Response` → `Id (id): string?`, `Status (status): string?`, `InvoiceInformation: InvoiceInformation1?`, `OrderInformation: OrderInformation61?`, `Links (_links): Links251?`, `SubmitTimeUtc: string?` | `SdkException<CreateInvoiceError>`; `TryGetInvoicingV2InvoicesPost400Response1` / `…404Response1` / `…502Response1` / `TryGetRawError` | `operations/Invoices.md`; `records-3-Cr-Ex.md`; `records-5-In-Me.md` |
| 3 Get | `client.Invoices.GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path `id` (provider invoice id) | `InvoicingV2InvoicesGet200Response` → `Id`, `Status (status): string?`, `InvoiceInformation: InvoiceInformation1?` (→ `PaymentLink (paymentLink): string?` once sent), `InvoiceHistory: IReadOnlyList<InvoiceHistory>?` (each: `Event (event): string?`, `Date (date): DateTimeOffset?`, `TransactionDetails`), `OrderInformation: OrderInformation61?`, `Links (_links): Links251?` | `SdkException<GetInvoiceError>`; `TryGetInvoicingV2InvoicesGet400Response1` / `…404Response1` / `…502Response1` / `TryGetRawError` | `operations/Invoices.md`; `records-5-In-Me.md` |
| 4 Update | `client.Invoices.UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `UpdateInvoiceRequest`: `InvoiceInformation (invoiceInformation): InvoiceInformation4` **!req**, `OrderInformation (orderInformation): OrderInformation60` **!req**; optional `CustomerInformation`, `ProcessingInformation72`, `MerchantDefinedFieldValues` | `InvoicingV2InvoicesPut200Response` → `Id`, `Status`, `InvoiceInformation: InvoiceInformation1?`, `OrderInformation: OrderInformation61?` | `SdkException<UpdateInvoiceError>`; `TryGetInvoicingV2InvoicesPut400Response1` / `…404Response1` / `…502Response1` / `TryGetRawError` | `operations/Invoices.md`; `records-11-To-We.md`; `records-5-In-Me.md` |
| 5 Send | `client.Invoices.PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path `id` only (no body) | `InvoicingV2InvoicesSend200Response` → `Id`, `Status`, `InvoiceInformation: InvoiceInformation1?` (→ `PaymentLink`), `OrderInformation: OrderInformation61?`, `Links (_links): Links251?` | `SdkException<PerformSendActionError>`; `TryGetInvoicingV2InvoicesSend400Response1` / `…404Response1` / `…502Response1` / `TryGetRawError` | `operations/Invoices.md`; `records-5-In-Me.md` |
| 6 Cancel | `client.Invoices.PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path `id` only (no body) | `InvoicingV2InvoicesCancel200Response` → `Id`, `Status`, `InvoiceInformation: InvoiceInformation1?`, `OrderInformation: OrderInformation61?` | `SdkException<PerformCancelActionError>`; `TryGetInvoicingV2InvoicesCancel400Response1` / `…404Response1` / `…502Response1` / `TryGetRawError` | `operations/Invoices.md`; `records-5-In-Me.md` |
| 7 List | `client.Invoices.GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `offset`, `limit`, `status` are all required positionally; `status` is nullable-but-must-pass (pass `null` for all statuses). **Call with named args** (`offset:`, `limit:`, `status:`). | query only: `offset`, `limit`, `status` | `InvoicingV2InvoicesAllGet200Response` → `TotalInvoices (totalInvoices): int?`, `Invoices (invoices): IReadOnlyList<Invoice1>?`. Each `Invoice1`: `Id (id): string?`, `Status (status): string?`, `CreatedDate (createdDate): string?`, `OrderInformation: OrderInformation62?` → `AmountDetails: AmountDetails62?` → `TotalAmount (totalAmount): string?`, `Currency (currency): string?`, `Links (_links): Links251?` | `SdkException<GetAllInvoicesError>`; `TryGetInvoicingV2InvoicesAllGet400Response1` / `…404Response1` / `…502Response1` / `TryGetRawError` | `operations/Invoices.md`; `records-4-Fe-In.md`; `records-6-Me-Pa.md`; `records-1-Ac-Bi.md` |

### 2b. Request sub-models (how to build the create/update body)

`InvoiceInformation` (create — `Models/InvoiceInformation.cs`, `records-5-In-Me.md`):
- `Description (description): string` **!req**
- `DueDate (dueDate): DateTimeOffset` **!req** — a calendar due date; SDK type is `DateTimeOffset`
- `SendImmediately (sendImmediately): bool? = false` — **leave at default `false`** so the invoice starts NOT-sent (draft). Do not set `true` for the draft-first flow.
- optional: `InvoiceNumber (invoiceNumber): string?`, `ExpirationDate (expirationDate): DateTimeOffset?`, `AllowPartialPayments (allowPartialPayments): bool? = false`, `DeliveryMode (deliveryMode): string?`

`InvoiceInformation4` (update — `Models/InvoiceInformation4.cs`, `records-11-To-We.md` row):
- `Description (description): string` **!req**, `DueDate (dueDate): DateTimeOffset` **!req**
- optional: `ExpirationDate`, `SendImmediately (= false)`, `AllowPartialPayments (= false)`, `DeliveryMode`
- **No `InvoiceNumber` field** here (that is the difference from `InvoiceInformation`). Updatable fields are exactly Description, DueDate, ExpirationDate, AllowPartialPayments, DeliveryMode, SendImmediately — plus the top-level `CustomerInformation` on `UpdateInvoiceRequest`.

`OrderInformation60` (create + update — `Models/OrderInformation60.cs`, `records-6-Me-Pa.md`):
- `AmountDetails (amountDetails): AmountDetails60` **!req**
- `LineItems (lineItems): IReadOnlyList<LineItem17>?` (optional)

`AmountDetails60` (`records-1-Ac-Bi.md`): `TotalAmount (totalAmount): string` **!req**, `Currency (currency): string` **!req** (= `"USD"`). Amounts are **strings**, not decimals.

`LineItem17` (`records-5-In-Me.md`): `ProductName (productName): string?`, `Quantity (quantity): int? = 1`, `UnitPrice (unitPrice): string?`, `TotalAmount (totalAmount): string?`, `ProductSku`, tax/discount fields — all optional. Product name → `ProductName`, quantity → `Quantity`, unit amount → `UnitPrice` (string).

`CustomerInformation` (create + update — `Models/CustomerInformation.cs`, `records-3-Cr-Ex.md`):
- `Name (name): string?`, `Email (email): string?`, `MerchantCustomerId (merchantCustomerId): string?`, `Company (company): Company6?` — **all optional per the generated flags** (see §5 required-ness caveat).

### 2c. Invoice STATUS — there is NO status enum in this SDK

The brief asks for "the enum type + exact values for invoice status." The map's `enums.md`
lists **12 enums; none is an invoice-status enum.** On every invoice response, `Status` is a
plain **`string?`** — not a `StringEnum<T>`. The concrete set of status strings the provider
emits (e.g. draft vs. sent vs. paid vs. cancelled) is **not enumerated anywhere in the SDK**.
Do not hard-code a status vocabulary from memory. `UNVERIFIED` — only the provider docs / live
traffic settle the string values; compare defensively (case-insensitive, unknown-tolerant) and
drive the app's state machine off your own persisted state, treating the provider `Status`
string as advisory. Source: `enums.md`, `records-5-In-Me.md`.

### 2d. Client construction, auth, base URL / server

- Client: `new CyberSourceMergedSpecClient(HttpClient httpClient, CyberSourceMergedSpecClientOptions options)`; or DI `services.AddCyberSourceMergedSpecClient(o => { … })`. `options` members: `Environment: ServerEnvironment`, `Server: ServerOptions`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Hooks: IReadOnlyList<SdkHook>`. Source: `sdk-map.md` §client-options.
- **Auth — no credentials property.** `CyberSourceMergedSpecClientOptions` exposes **no** credential field; the merged spec declares no security scheme. Every request is signed by an opt-in **HTTP Signature `SdkHook`** appended at client construction when its env vars (`VISA_MERCHANT_ID`, `VISA_KEY_ID`, `VISA_SECRET_KEY`) resolve. See trap §3 and REQUIRED READING. Source: `sdk-map.md` §Servers & auth.
- **Environment / base URL.** `options.Environment` is `ServerEnvironment` whose **only** member is `Production` — and its default base host is the **sandbox/apitest** host (every operation row reads `(Default (apitest))`). So the default already targets the CyberSource TEST environment the brief requires; do not assume `Production` means the live host. To force every call through `Visa:BaseUrl` verbatim, set the base URL via `options.Server` (`ServerOptions`) — the exact member and override precedence are governed by `dotnet-configuration-resilience` (trap §3). Source: `sdk-map.md` §Servers & auth, §client-options.

---

## 3. Trap notes (name the hazard, load the skill — do NOT implement from these lines)

- ⚠ **Step 1 (client & DI).** The `HttpClient`/handler pipeline must be long-lived and reused; whether the SDK client wrapper is transient or singleton, and how it binds to `IHttpClientFactory`, changes correctness under load. **MUST load `dotnet-client-initialization`** before writing `new CyberSourceMergedSpecClient(...)` or `AddCyberSourceMergedSpecClient`.
- ⚠ **Step 1 (authentication).** There is no credentials property to set; auth is the HTTP-Signature `SdkHook`, and the env vars are read **once, inside the client constructor** — the timing of when they must exist, and what an unset switch does to every request, is the whole hazard. **MUST load `dotnet-authentication`** before constructing the client or making the first call.
- ⚠ **Step 1 (base URL / server / resilience).** The SDK's retry/timeout options do **not** bound a whole call, and the base-URL override member on `options.Server` plus its precedence over `Environment` is not something the option names reveal. Whether a failed write can be silently re-sent is decided here, not in the call site. **MUST load `dotnet-configuration-resilience`** before wiring the client or routing through `Visa:BaseUrl`.
- ⚠ **Step 7 (list).** `GetAllInvoices` optional-ish params have no C# defaults and mis-bind positionally; how to page (`offset`/`limit`) without a built-in pager, and what `Timeout` bounds per page, are covered by the skills. **MUST load `dotnet-calling-endpoints`** (call shape) and `dotnet-configuration-resilience` (paging/timeout).
- ⚠ **Steps 2 & 4 (building bodies).** Amounts and unit prices are **strings**, `DueDate` is a `DateTimeOffset`, `Status` is a bare string, and unmodeled JSON is dropped on deserialize — the mapping traps between SDK models and eShop's domain types are the hazard. **MUST load `dotnet-models`** before constructing request payloads or mapping responses.
- ⚠ **Every step (error boundary).** Which exception types actually reach a catch, and why a single-status catch ladder is silently wrong here, is not visible in the signatures. **MUST load `dotnet-error-handling`** before writing any `try/catch` — see REQUIRED READING for the two `JsonException` hazards that an SDK-exception-only ladder misses.
- ⚠ **Tests.** The `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Client construction, HttpClient lifetime, DI registration (Step 1) |
| `dotnet-authentication` | HTTP-Signature `SdkHook`, env-var timing, no-credentials-property model (Step 1) — **always required for this SDK; its auth is unlike every other APIMatic .NET SDK** |
| `dotnet-configuration-resilience` | Base URL via `options.Server`, `Environment`, retries/timeouts, list paging (Steps 1 & 7) |
| `dotnet-calling-endpoints` | Named-argument call shape, cancellation, `GetAllInvoices` param binding (all call steps) |
| `dotnet-models` | Building request bodies, string amounts, `DateTimeOffset`, dropped-field behaviour (Steps 2 & 4) |
| `dotnet-error-handling` | The `SdkException<…Error>` catch boundary and the two `JsonException` hazards below (every step) — **always required** |
| `dotnet-testing` | Faking the `HttpClient` seam for the integration tests |

**Two `JsonException` hazards that an SDK-exception-only catch ladder lets escape — load `dotnet-error-handling` before writing the boundary:**

- A drifted or malformed **2xx** body (a missing `required` member, or a field whose live type differs from the generated one) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so a catch ladder that only catches `SdkException<…>` lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

**Assumptions**
- "Draft / not-yet-delivered" = create with `InvoiceInformation.SendImmediately = false` (the default) and do NOT call `PerformSendAction` until Step 5. Assumed; the map has no operation Notes confirming the exact state name.
- The eShop order reference can travel in the optional `ClientReferenceInformation78` (create) for reconciliation — `YOUR CALL — not in the map` whether/where you carry it.
- Reconciliation matches provider invoices to eShop records by an identifier eShop controls (e.g. `ClientReferenceInformation` code and/or the stored provider `Id`), because the provider account carries invoices NOT created by this app — filtering by those is the app's responsibility, not a provider filter.

**Blockers**
1. **No server-side date-range filter for listing invoices.** Capability 6 asks for invoices created between two ISO-8601 date-times. `GetAllInvoices(int offset, int limit, string? status, …)` is the **only** list op and exposes **no** created-from/created-to (or any date) parameter — confirmed against `operations/Invoices.md`. The SDK cannot filter by date range server-side. The available path: page with `offset`/`limit` (there is no pager helper — `Pagination: none`) until `TotalInvoices` is covered, read each `Invoice1.CreatedDate` (a **`string?`**, not a typed date), and filter to the range **client-side**, additionally discarding invoices not created by this app. Confirm this client-side approach is acceptable, or the date-range reconciliation requirement cannot be met as written.
2. **Update-while-draft refusal rules are undocumented.** The brief asks what `UpdateInvoice` refuses once the invoice is delivered/cancelled. This SDK's operations carry **no** Notes/`<remarks>`, so the map states no such rule; it is provider-enforced and surfaces only as a runtime `SdkException<UpdateInvoiceError>` (likely 400). `UNVERIFIED` — handle by catching the typed error and reading `Reason`/`Message`, not by pre-checking a documented rule.
3. **`UpdateInvoiceRequest` requires `OrderInformation` (amount) even when amount is unchanged.** `OrderInformation (orderInformation): OrderInformation60` is **!req** on update, and its `AmountDetails60.TotalAmount`/`Currency` are **!req**. To "not change the amount," the caller must **re-send the current amount unchanged** (read it from the invoice / eShop record first). Confirmed against `records-11-To-We.md` + `records-6-Me-Pa.md`.
4. **Send vs. Publish semantics unverified.** Both `PerformSendAction` (`…/delivery`) and `PerformPublishAction` (`…/publication`) exist and return the same envelope shape (id, status, `InvoiceInformation1` with `PaymentLink`). The map has no Notes distinguishing them or stating an ordering (e.g. must publish before deliver). The plan uses `PerformSendAction` for "put the bill to the shopper." `UNVERIFIED` — confirm against provider docs whether `PerformPublishAction` is also required in the shopper flow.

**Required-ness caveat (applies to every request body).** This SDK's operations have **no**
Notes, so the ONLY machine-checked required-ness is the generated `!req` flags shown above.
`CustomerInformation` (and its `Name`/`Email`) is marked **optional**, yet a real, sendable
invoice almost certainly needs the customer email — carry name + email because the scope needs
them, but note that whether the provider actually rejects a create/send without them is
**`UNVERIFIED`** and not something the compiler or the map will catch. Do not imply required-ness
beyond the `!req` flags was verified.
