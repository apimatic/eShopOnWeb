# Visa/CyberSource Invoicing — integration plan & contract sheet (eShopOnWeb)

SDK: `CyberSourceMergedSpec` (NuGet `APIMatic.VisaCyberSource`, install version-less). All
Invoicing goes through `client.Invoices` (7 operations). This sheet is grounded entirely in the
bundled SDK map; no fact here is from memory. Where the map cannot settle something it is marked
`UNVERIFIED`, `YOUR CALL — not in the map`, or raised as a Blocker (§5).

---

## 1. Scope & sequence

| # | Capability | Operation(s) used |
|---|---|---|
| 1 | Client + DI + auth + base URL wiring | `AddCyberSourceMergedSpecClient` / `new CyberSourceMergedSpecClient` + HTTP Signature hook |
| 2 | Raise a bill (create draft invoice, USD) | `client.Invoices.CreateInvoice(...)` |
| 3 | Get an invoice (state, history, pay link) | `client.Invoices.GetInvoice(...)` |
| 4 | Update/correct an unsent invoice (due date, customer) | `client.Invoices.UpdateInvoice(...)` |
| 5 | Send an invoice (issue to shopper) | `client.Invoices.PerformSendAction(...)` (see trap re: Publish vs Send) |
| 6 | Cancel an invoice (withdraw) | `client.Invoices.PerformCancelAction(...)` |
| 7 | List / reconcile in a date range | `client.Invoices.GetAllInvoices(...)` — **see Blockers §5** (no date filter, no MDF filter) |
| 8 | Error boundary + JSON-drift boundary | all of the above |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C#
> identifier. The cancellation-token parameter really is named `ct`: in named arguments write
> `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it.** Records
> (request/response models + nested models) live in `CyberSourceMergedSpec.Models`. Enums live in
> `CyberSourceMergedSpec.Models.Enums`. Typed error classes (`CreateInvoiceError`, …) live in
> `CyberSourceMergedSpec.Errors`. `ServerEnvironment` lives in `CyberSourceMergedSpec.Servers`.
> Client + options live in root `CyberSourceMergedSpec`. `SdkException<T>` is implied by its
> source path `Core/Exceptions/` → `CyberSourceMergedSpec.Core.Exceptions`; `RawError` by
> `Core/ErrorResponse/` → `CyberSourceMergedSpec.Core.ErrorResponse` (confirm both via
> `dotnet-error-handling`). Dropping a type to the wrong namespace breaks the build — child
> namespaces are NOT imported transitively.

### 2a. Operations (all on `client.Invoices`, source `operations/Invoices.md`)

Every operation is **throw-based, Case A (typed error)**, **no `…Result` no-throw variant**, **no
SDK-level pagination helper**. Each typed error exposes `TryGet…400Response1` [400],
`…404Response1` [404], `…502Response1` [502], and `TryGetRawError(out RawError)` [fallback].

| # | Method signature (params in order) | Request type | Returns |
|---|---|---|---|
| 2 | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateInvoiceRequest` | `InvoicingV2InvoicesPost201Response` |
| 3 | `GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (id in path) | `InvoicingV2InvoicesGet200Response` |
| 4 | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `UpdateInvoiceRequest` | `InvoicingV2InvoicesPut200Response` |
| 5 | `PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (id in path) | `InvoicingV2InvoicesSend200Response` |
| 5b | `PerformPublishAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (id in path) | `InvoicingV2InvoicesPublish200Response` |
| 6 | `PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (id in path) | `InvoicingV2InvoicesCancel200Response` |
| 7 | `GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (query params) | `InvoicingV2InvoicesAllGet200Response` |

- HTTP paths: POST `/invoicing/v2/invoices` · GET `/invoicing/v2/invoices/{id}` · PUT
  `/invoicing/v2/invoices/{id}` · POST `.../{id}/delivery` (Send) · POST `.../{id}/publication`
  (Publish) · POST `.../{id}/cancelation` (Cancel) · GET `/invoicing/v2/invoices` (list).
- `GetAllInvoices` query params (wire ← C#): `offset`←`offset`, `limit`←`limit`, `status`←`status`.
  `status` is nullable with **no default → must be passed explicitly** (pass `null` for no filter).
  Call list ops with **named arguments**.
- Error accessor payload types are ordinary records; all share the shape
  `SubmitTimeUtc: string?`, `Status: string?`, `Reason: string?`, `Message: string?`,
  `Details: IReadOnlyList<Detail>?` (the 502 variants omit `Details`). Read status/body via the
  typed accessors, not `.ToString()`. Source: `operations/Invoices.md`.

### 2b. Request models — fields to populate (`Name (wire_name): type, required?`)

**`CreateInvoiceRequest`** (source `records-3-Cr-Ex.md`):

| Field (wire) | Type | Req? | Notes |
|---|---|---|---|
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation` | **!req** | due date, description |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **!req** | amount + line items |
| `CustomerInformation (customerInformation)` | `CustomerInformation` | optional | name/email/company |
| `ClientReferenceInformation (clientReferenceInformation)` | `ClientReferenceInformation78` | optional | partner ref only |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72` | optional | not needed for scope |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>` | optional | the stamp field — see §2f |

**`UpdateInvoiceRequest`** (source `records-11-To-We.md`) — used for capability 4:

| Field (wire) | Type | Req? | Notes |
|---|---|---|---|
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation4` | **!req** | updatable invoice fields |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **!req** | ⚠ amount STILL required on the wire (see §4 trap + §5) |
| `CustomerInformation (customerInformation)` | `CustomerInformation` | optional | correctable customer details |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72` | optional | |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>` | optional | |

Note: `UpdateInvoiceRequest` has **no `ClientReferenceInformation`** field (partner ref is set at
create only).

**Nested request models:**

`InvoiceInformation` (create; source `records-5-In-Me.md`) — "invoice-specific fields":
`InvoiceNumber (invoiceNumber): string?`, `Description (description): string !req`,
`DueDate (dueDate): DateTimeOffset !req`, `ExpirationDate (expirationDate): DateTimeOffset?`,
`SendImmediately (sendImmediately): bool? = false`, `AllowPartialPayments (allowPartialPayments): bool? = false`,
`DeliveryMode (deliveryMode): string?`.
→ For a **draft / not-yet-sent** invoice, leave `SendImmediately` at its default `false`.
→ `DueDate` is the calendar due date but its C# type is `DateTimeOffset` (a date-time, not date-only).

`InvoiceInformation4` (update; source `records-5-In-Me.md`) — "updatable invoice information":
`Description (description): string !req`, `DueDate (dueDate): DateTimeOffset !req`,
`ExpirationDate (expirationDate): DateTimeOffset?`, `SendImmediately (sendImmediately): bool? = false`,
`AllowPartialPayments (allowPartialPayments): bool? = false`, `DeliveryMode (deliveryMode): string?`.
→ These are exactly the invoice fields the SDK exposes as updatable (no `InvoiceNumber`, no amount).

`OrderInformation60` (create + update; source `records-6-Me-Pa.md`):
`AmountDetails (amountDetails): AmountDetails60 !req`, `LineItems (lineItems): IReadOnlyList<LineItem17>?`.

`AmountDetails60` (source `records-1-Ac-Bi.md`):
`TotalAmount (totalAmount): string !req`, `Currency (currency): string !req`,
`DiscountAmount (discountAmount): string?`, `DiscountPercent (discountPercent): string?`,
`SubAmount (subAmount): string?`, `MinimumPartialAmount (minimumPartialAmount): string?`,
`TaxDetails (taxDetails): TaxDetails13?`, `Freight (freight): Freight?`.
→ Amounts are **strings** (e.g. `"100.00"`). For USD set `Currency = "USD"`.

`LineItem17` (optional; source `records-5-In-Me.md`):
`ProductSku (productSku): string?`, `ProductName (productName): string?`,
`Quantity (quantity): int? = 1`, `UnitPrice (unitPrice): string?`, `DiscountAmount`, `DiscountPercent`,
`TaxAmount`, `TaxRate`, `TotalAmount` (all `string?`).

`CustomerInformation` (create + update; source `records-3-Cr-Ex.md`):
`Name (name): string?`, `Email (email): string?`, `MerchantCustomerId (merchantCustomerId): string?`,
`Company (company): Company6?`. `Company6` = `Name (name): string?` only (source `records-2-Bi-Cr.md`).
→ No street/billing address is modeled on the invoice customer.

`ClientReferenceInformation78` (create only; source `records-2-Bi-Cr.md`):
`Partner (partner): Partner38?`. `Partner38` = `DeveloperId (developerId): string?`,
`SolutionId (solutionId): string?` (source `records-6-Me-Pa.md`).
→ This is a **partner/solution identity**, not a per-invoice reference — do NOT use it to tag
individual eShop orders. Use `MerchantDefinedFieldValues` for that (§2f).

### 2c. Response envelopes — fields you read

All single-invoice responses (Post201 / Get200 / Put200 / Send200 / Publish200 / Cancel200) share
this top shape (source `records-5-In-Me.md`):

`Links (_links): Links251?`, `Id (id): string?`, `SubmitTimeUtc (submitTimeUtc): string?`,
`Status (status): string?`, `CustomerInformation (customerInformation): CustomerInformation?`,
`ProcessingInformation (processingInformation): ProcessingInformation72?`,
`InvoiceInformation (invoiceInformation): InvoiceInformation1?`,
`OrderInformation (orderInformation): OrderInformation61?`.

Extra fields per response:
- `Post201`, `Put200`: also `MerchantDefinedFieldValuesWithDefinition (…): IReadOnlyList<MerchantDefinedFieldValuesWithDefinition>?`.
- `Get200`: also `MerchantDefinedFieldValuesWithDefinition (…)` **and**
  `InvoiceHistory (invoiceHistory): IReadOnlyList<InvoiceHistory>?`.

→ **Capability 1** (provider invoice id + current status): read `.Id` and `.Status` off
`InvoicingV2InvoicesPost201Response`. `Status` is a plain wire **string** — draft/sent/etc. are not
enumerated by the SDK (see §2e).
→ **Capability 3** (state + how it got there + pay URL): `.Status`; `.InvoiceHistory` (Get200 only,
see below) for status history; `.InvoiceInformation.PaymentLink` for the pay URL.
→ **Capabilities 4/5/6** (post-action state): read `.Status` (and for cancel, note no pay link is
returned once withdrawn — verify by reading `.InvoiceInformation.PaymentLink` is null).

`InvoiceInformation1` (response invoice info; source `records-5-In-Me.md`):
`InvoiceNumber (invoiceNumber): string?`, `Description (description): string?`,
`DueDate (dueDate): DateTimeOffset?`, `ExpirationDate (expirationDate): DateTimeOffset?`,
`AllowPartialPayments (allowPartialPayments): bool? = false`, **`PaymentLink (paymentLink): string?`**,
`DeliveryMode (deliveryMode): string?`, `CustomLabels (customLabels): IReadOnlyList<CustomLabel>?`.
→ **`PaymentLink` is the customer-facing pay URL.** It is a response-only field (not on any request
model). It is only populated once the provider has issued/sent the invoice — treat it as
best-effort and null-check it. `UNVERIFIED` whether it is present pre-send.

`InvoiceHistory` (Get200 only; source `records-5-In-Me.md`):
`Event (event): string?`, `Date (date): DateTimeOffset?`,
`TransactionDetails (transactionDetails): TransactionDetails?`.
→ This is the "how it reached its state" status history. It is returned **only by `GetInvoice`**,
not by create/update/action responses — so capability 3's history requires a `GetInvoice` call.

`Links251` (the `_links` envelope; source `records-5-In-Me.md`):
`Self (self): Self?`, `Update (update): Update?`, `Deliver (deliver): Deliver?`, `Cancel (cancel): Cancel?`.
→ These are HATEOAS action links (self/update/deliver/cancel), **not** the customer pay URL — the
pay URL is `InvoiceInformation1.PaymentLink`, above. Which of these link objects are present can
reflect which actions the provider still permits, but the map does not document that; `UNVERIFIED`.

### 2d. List / reconciliation response (`InvoicingV2InvoicesAllGet200Response`, source `records-5-In-Me.md`)

Fields: `Links (_links): Links251?`, `SubmitTimeUtc (submitTimeUtc): string?`,
`TotalInvoices (totalInvoices): int?`, `Invoices (invoices): IReadOnlyList<Invoice1>?`.

`Invoice1` (list item; source `records-4-Fe-In.md`):
`Links (_links): Links251?`, `Id (id): string?`, `Status (status): string?`,
`CreatedDate (createdDate): string?`, `CustomerInformation (customerInformation): CustomerInformation2?`,
`InvoiceInformation (invoiceInformation): InvoiceInformation2?`,
`OrderInformation (orderInformation): OrderInformation62?`.
→ **The list item does NOT carry `MerchantDefinedFieldValues`** — you cannot tell "ours" from
"not-ours" off the list payload by a stamp; you would have to `GetInvoice(id)` per row. See §5.
→ Paging is manual via `offset`/`limit`; `TotalInvoices` gives the total to page against.

### 2e. Enums / status values

There is **no invoice-status enum** in the SDK (source `enums.md`). Every `Status` field is a plain
`string`, so "draft", "sent", "delivered", "canceled" etc. are wire strings the map does not
enumerate. `UNVERIFIED` — the exact status literals the provider returns are not in the map or
source; do **not** hard-code a status ladder off guessed literals. When filtering
`GetAllInvoices(status:)`, the accepted string values are likewise undocumented here.
(The only invoice-adjacent enum is `ReferenceType` = `Invoice`/`Purchase`/`Donation`, which is not
wired to the invoice `Status` field.)

### 2f. Stamping/identifying our invoices (`MerchantDefinedFieldValue`)

`MerchantDefinedFieldValue` (source `records-5-In-Me.md`): `DefinitionId (definitionId): long?`,
`Value (value): string?`. Set this list on `CreateInvoiceRequest.MerchantDefinedFieldValues` to
stamp an eShop reference (e.g. the order id) onto invoices we create. On `GetInvoice` these come
back enriched as `MerchantDefinedFieldValuesWithDefinition` (`Value`, `DefinitionId`, `Label`,
`ReferenceType`, `CustomerVisible`, …; source `records-5-In-Me.md`).
→ **`DefinitionId` is a numeric id of a field definition that must already exist on the provider
account.** Creating/reading those definitions is a *different* controller (`MerchantDefinedFields`,
4 ops) / `InvoiceSettings` — out of the scope you gave, but a prerequisite to stamping. `YOUR CALL —
not in the map` which definition id eShop uses; the map does not fix it.
→ **Filtering by this stamp server-side is NOT supported** — see §5.

### 2g. Client construction / DI / auth / base URL (source `sdk-map.md`)

- Construct: `new CyberSourceMergedSpecClient(HttpClient httpClient, CyberSourceMergedSpecClientOptions options)`.
  DI: `services.AddCyberSourceMergedSpecClient(o => { /* set options on o */ });`.
- `CyberSourceMergedSpecClientOptions` properties: `Environment: ServerEnvironment`,
  `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`,
  `Hooks: IReadOnlyList<SdkHook>`. **There is NO credentials property** — see the auth trap (§3).
- **Environment / base URL:** `options.Environment` is `ServerEnvironment` whose **only** member is
  `ServerEnvironment.Production`, and its default base URL is the **apitest sandbox** host (every
  operation path is tagged "Default (apitest)"). Relying on the default therefore routes to
  sandbox, not a production host. The base-URL override point is `options.Server` (`ServerOptions`)
  — to bind `Visa:BaseUrl` from configuration and route every call through it verbatim you set the
  server URL there. The exact `ServerOptions` property/mechanism for a fully custom base URL is
  governed by `dotnet-configuration-resilience` (§3 trap) — take it from that skill, do not
  hard-code a host. `| Visa:BaseUrl binding property | resolve via dotnet-configuration-resilience | YOUR CALL — bind from config, mechanism per skill |`

---

## 3. Trap notes (name the hazard, load the skill — do not implement from these lines)

> ⚠ Step 1 (auth) — **This SDK breaks the APIMatic auth pattern.** The merged spec declares no
> security scheme, so `CyberSourceMergedSpecClientOptions` has **no credentials property** and
> nothing is wired into the `IAuthScheme` pipeline. Every request is signed by an **opt-in HTTP
> Signature `SdkHook`** configured from environment variables (`VISA_MERCHANT_ID`, `VISA_KEY_ID`,
> `VISA_SECRET_KEY`, plus an opt-in switch) that are read **once, inside the client constructor** —
> so they must be set *before* the client is built, and leaving the switch unset does not disable
> auth, it sends every request **unsigned** while appearing to work locally. Exact variable names,
> the switch, and the hook wiring are in the skill. **MUST load `dotnet-authentication`.**

> ⚠ Step 1 (client + DI) — the `HttpClient`/handler pipeline must be long-lived and reused (via
> `IHttpClientFactory`), not rebuilt per request; which part of the SDK client may be transient is
> not shown by the constructor. **MUST load `dotnet-client-initialization`.**

> ⚠ Step 1 (base URL / resilience) — the SDK's retry/timeout options do **not** bound a whole call
> and are **not** the timeout on the `HttpClient` you register; and the base-URL/server override
> (needed to honour `Visa:BaseUrl` and avoid the sandbox default) is a resilience-config concern
> whose exact mechanism the option names alone don't reveal. A **transport failure** on a write
> (e.g. `CreateInvoice`) may be retried on POST even when status-based retry is off — whether a
> failed create can be re-sent (double-billing) is decided here. **MUST load
> `dotnet-configuration-resilience`.**

> ⚠ Steps 2–7 (calling / models) — list ops take named args (optional params have no C# default and
> mis-bind positionally); amounts and dates are wire strings / `DateTimeOffset` on generated records
> and unmodeled JSON is dropped on deserialize; there are no unions in this SDK. **MUST load
> `dotnet-calling-endpoints` and `dotnet-models`.**

> ⚠ Step 4 (update) — `UpdateInvoiceRequest` requires `OrderInformation60` (with a **required**
> `AmountDetails60`) on the wire even though you intend to correct only due date + customer. Whether
> the provider actually applies, ignores, or rejects a changed amount on update — and whether update
> of an already-sent/canceled invoice is refused — is **not documented** (no operation remarks in
> this SDK). Send the current order amount unchanged and treat an amount change via update as
> unsupported; expect legitimate refusals as typed 400s. **MUST load `dotnet-error-handling`** for
> reading those refusals. (`UNVERIFIED`: exact refusal semantics.)

> ⚠ Steps 5/5b (send vs publish) — the SDK exposes **two** issue-style actions: `PerformSendAction`
> (POST `.../delivery`) and `PerformPublishAction` (POST `.../publication`). Which one "issues to the
> shopper and yields a pay link" versus "makes the invoice live" is **not** documented (no remarks).
> Plan uses `PerformSendAction` as the send-to-shopper action; confirm against provider behaviour.
> (`UNVERIFIED`.)

> ⚠ Step 8 (error boundary) — every Invoices op is Case A typed (`SdkException<{Op}Error>`) with
> `TryGet…400/404/502Response1` + `TryGetRawError`; no op has a no-throw `…Result` variant, so a
> try/catch is mandatory. `TryGetRawError` is not a catch-all on the typed error. **MUST load
> `dotnet-error-handling`** before writing the boundary.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

This sheet deliberately does **not** carry these skills' contents (defaults, worked examples, the
parts a one-line note cannot hold). Load each before writing the code for its step.

| Skill | Governs |
|---|---|
| `dotnet-authentication` | The HTTP Signature hook — this SDK's only auth route; no credentials property (Step 1). **Always required here.** |
| `dotnet-client-initialization` | Client construction, `AddCyberSourceMergedSpecClient` DI, HttpClient ownership/lifetime (Step 1). |
| `dotnet-configuration-resilience` | Base-URL/server override for `Visa:BaseUrl`, retries/timeouts, POST-retry double-billing risk, pagination (Steps 1, 7). |
| `dotnet-calling-endpoints` | Named-argument calls, request/response envelope usage, cancellation (Steps 2–7). |
| `dotnet-models` | Building request records, required/nullable members, string amounts, wire-name mapping (Steps 2–4). |
| `dotnet-error-handling` | The try/catch boundary, reading status/body via `TryGet…` accessors (Steps 4, 8). **Always required here.** |
| `dotnet-testing` | Faking the `HttpClient` seam for the integration tests. |

**Two JSON-drift hazards for the error boundary — `System.Text.Json.JsonException` reaches it from
two directions that need opposite handling. MUST load `dotnet-error-handling` before writing that
boundary:**

- a drifted or malformed **2xx** body (a missing `required` member, or a field whose live type
  differs from the generated one) surfaces as a `JsonException` from deserialization, **not** as an
  `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

**Assumptions**
- "Send to shopper" is mapped to `PerformSendAction` (`.../delivery`); `PerformPublishAction`
  (`.../publication`) is the alternative. Distinction unverified (no operation remarks).
- Amounts/currency/line items and customer come from the eShop order; the plan carries the eShop
  order id as a `MerchantDefinedFieldValue` stamp (subject to the definition-id prerequisite below).
- USD billing → `AmountDetails60.Currency = "USD"`.
- Required-ness beyond the generated `!req` flags is **not verified**: this SDK's operations carry
  no remarks, so any optional field the provider actually insists on is unmarked anywhere and no
  compiler will catch it. Do not read the absence of a `!req` flag as "the provider accepts it
  omitted."

**Blockers**
1. **Reconciliation by date range is not supported by the SDK.** `GetAllInvoices` accepts only
   `offset`, `limit`, `status` — **there is no from/to date filter parameter** (map
   `operations/Invoices.md`). Requirement 6 (retrieve invoices raised in an ISO-8601 date-time
   range, server-side) cannot be met as stated. Options: page the full list and filter client-side
   on `Invoice1.CreatedDate` (a `string?`), or use a different reporting controller
   (`Reports`/`SearchTransactions` — out of your stated scope and unverified for invoicing). This
   needs a product/design decision before implementing capability 6.
2. **No server-side "ours vs not-ours" filter, and the list item carries no stamp.** You can stamp
   invoices we create via `MerchantDefinedFieldValues`, but `GetAllInvoices` cannot filter by it
   (only by `status`), and the list item `Invoice1` does **not** return merchant-defined fields at
   all — so distinguishing our invoices from the account's other invoices from the list payload is
   impossible without a `GetInvoice(id)` per row. Reconciliation must either GetInvoice each id
   (cost) or reconcile against eShop's own stored provider ids. Decide before implementing.
3. **Stamping has a prerequisite outside the given scope.** `MerchantDefinedFieldValue.DefinitionId`
   references a field definition that must pre-exist on the provider account, managed by the
   `MerchantDefinedFields`/`InvoiceSettings` controllers (not in scope 1–6). If stamping is required
   for reconciliation, provisioning that definition is an added step.
