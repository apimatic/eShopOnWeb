# Visa / CyberSource .NET SDK — Customer Invoicing integration plan (eShopOnWeb)

Provider: **Visa** via the **CyberSource** platform. SDK: `APIMatic.VisaCyberSource`
(root namespace `CyberSourceMergedSpec`, client `CyberSourceMergedSpecClient`). Every fact below
was read this session from the bundled SDK map (map page cited per row). Scope is **C#/.NET only**.

All six capabilities are served by **`client.Invoices`** (source `Api/Invoices.cs`, 7 operations).
Amounts are **strings** throughout this SDK; currency is **"USD"** set explicitly; test/sandbox only.

---

## 1. Scope & sequence

| # | Step | Operation(s) |
|---|---|---|
| 0 | Register + construct the client (HttpClient owned by `IHttpClientFactory`); wire the HTTP-Signature auth hook from env vars **before** the client is constructed; pin the base URL from `Visa:BaseUrl`. | — (client setup) |
| 1 | Raise a **draft** invoice from an order (line items, amounts, USD, due date, customer, app identifier) — **not** auto-sent. | `CreateInvoice` |
| 2 | Get an invoice by provider id — status, history, payment link. | `GetInvoice` |
| 3 | Correct a still-draft invoice (due date + customer); handle refused transition when already sent/cancelled. | `UpdateInvoice` |
| 4 | Deliver the invoice to the customer. | `PerformSendAction` |
| 5 | Withdraw/cancel the invoice. | `PerformCancelAction` |
| 6 | List invoices for reconciliation (paged by offset/limit; distinguish eShop-originated). | `GetAllInvoices` |

> ⚠ Capability 6 as briefed (filter by a **creation date range**) is **not offered by the SDK** — see
> Blockers §5. And the app-owned **invoice number** you stamp at create is **absent from the list
> projection** — only `MerchantCustomerId` round-trips to the list. Both drive design decisions below.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it.** Operation
> methods hang off `client.Invoices` (controllers live in `CyberSourceMergedSpec.Api`, but you
> reach them through the client property, not by `new`). Request/response records and every nested
> model here live in **`CyberSourceMergedSpec.Models`**. Typed error classes (`CreateInvoiceError`,
> `UpdateInvoiceError`, …) live in **`CyberSourceMergedSpec.Errors`**. `SdkException<T>` is at
> `Core/Exceptions/…` ⇒ **`CyberSourceMergedSpec.Core.Exceptions`**; `RawError` is at
> `Core/ErrorResponse/…` ⇒ **`CyberSourceMergedSpec.Core.ErrorResponse`**. `ServerEnvironment` lives in
> **`CyberSourceMergedSpec.Servers`**; `RetryOptions` at `Core/Configuration/…` ⇒
> **`CyberSourceMergedSpec.Core.Configuration`**. Add a separate `using` per namespace — child
> namespaces are **not** imported transitively.
>
> **Model names are numerically disambiguated — copy the exact suffix from the row.** Create uses
> `InvoiceInformation` + `OrderInformation60`; Update uses `InvoiceInformation4` + `OrderInformation60`;
> every response's inner invoice block is `InvoiceInformation1`; the *list* projection uses different
> siblings again (`InvoiceInformation2`, `CustomerInformation2`, `OrderInformation62`, `AmountDetails62`).
> Picking the unsuffixed sibling because it reads better is a build break.

### 2a. Operations (all on `client.Invoices`)

| Capability | Method signature (params in order) | Request model | Response envelope | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| 1. Create/raise | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateInvoiceRequest` | `InvoicingV2InvoicesPost201Response` | Case A `SdkException<CreateInvoiceError>` · `TryGetInvoicingV2InvoicesPost400Response1(out …)` [400] · `…Post404Response1` [404] · `…Post502Response1` [502] · `TryGetRawError(out RawError)` [fallback] | none | operations/Invoices.md |
| 2. Get by id | `GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesGet200Response` | Case A `SdkException<GetInvoiceError>` · `…Get400Response1` [400] · `…Get404Response1` [404] · `…Get502Response1` [502] · `TryGetRawError` | none | operations/Invoices.md |
| 3. Update (draft) | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `UpdateInvoiceRequest` | `InvoicingV2InvoicesPut200Response` | Case A `SdkException<UpdateInvoiceError>` · `…Put400Response1` [400] · `…Put404Response1` [404] · `…Put502Response1` [502] · `TryGetRawError` | none | operations/Invoices.md |
| 4. Deliver/send | `PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesSend200Response` | Case A `SdkException<PerformSendActionError>` · `…Send400Response1` [400] · `…Send404Response1` [404] · `…Send502Response1` [502] · `TryGetRawError` | none | operations/Invoices.md |
| 5. Cancel/withdraw | `PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesCancel200Response` | Case A `SdkException<PerformCancelActionError>` · `…Cancel400Response1` [400] · `…Cancel404Response1` [404] · `…Cancel502Response1` [502] · `TryGetRawError` | none | operations/Invoices.md |
| 6. List | `GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (query `offset`, `limit`, `status`) | `InvoicingV2InvoicesAllGet200Response` | Case A `SdkException<GetAllInvoicesError>` · `…AllGet400Response1` [400] · `…AllGet404Response1` [404] · `…AllGet502Response1` [502] · `TryGetRawError` | manual (`offset`/`limit`; no auto-pager) | operations/Invoices.md |

Notes on the operation rows:
- `HTTP verbs/paths`: Create `POST /invoicing/v2/invoices`; Get `GET …/{id}`; Update `PUT …/{id}`;
  Send `POST …/{id}/delivery`; Cancel `POST …/{id}/cancelation`; List `GET …/invoices`.
- `status` on `GetAllInvoices` is **`string?` with no C# default → pass it explicitly** (pass `null`
  for "all statuses"). Call list/search ops with **named arguments** (see trap notes).
- **Every operation is throw-only — there is no `…Result` no-throw variant anywhere in this SDK.**
- A **sibling action `PerformSendAction` has a twin `PerformPublishAction`**
  (`POST …/{id}/publication`, returns `InvoicingV2InvoicesPublish200Response`, Case A). The map
  carries **no prose distinguishing "publication" from "delivery"** (this SDK has zero operation
  `<remarks>`). For "send to the customer" this plan uses **`PerformSendAction` (delivery)**; whether
  publication is a required predecessor or an alternative is `UNVERIFIED` (source has no note; only
  live traffic settles it) — code the send path, and if the provider rejects delivery pending
  publication, add the publish call then. Source: operations/Invoices.md.

### 2b. Request models — fields (`C#Name (wire_name): type, required?`)

**`CreateInvoiceRequest`** (Models/CreateInvoiceRequest.cs) — records-3-Cr-Ex:
- `ClientReferenceInformation (clientReferenceInformation): ClientReferenceInformation78?` — optional
- `CustomerInformation (customerInformation): CustomerInformation?` — optional *per generated flag* (needed in practice — see below)
- `ProcessingInformation (processingInformation): ProcessingInformation72?` — optional (not needed for scope)
- `InvoiceInformation (invoiceInformation): InvoiceInformation` — **!req**
- `OrderInformation (orderInformation): OrderInformation60` — **!req**
- `MerchantDefinedFieldValues (merchantDefinedFieldValues): IReadOnlyList<MerchantDefinedFieldValue>?` — optional

**`InvoiceInformation`** (create req) — records-5-In-Me:
- `InvoiceNumber (invoiceNumber): string?` — optional — **app-owned identifier you stamp here** (readable at Get-by-id/create response; **NOT in the list projection** — see §5)
- `Description (description): string` — **!req**
- `DueDate (dueDate): DateTimeOffset` — **!req** — the calendar due date (type is `DateTimeOffset`; see trap on wire format)
- `ExpirationDate (expirationDate): DateTimeOffset?` — optional
- `SendImmediately (sendImmediately): bool? = false` — **leave `false`/`null` to keep the invoice a DRAFT (not auto-delivered)**; `true` would send on create
- `AllowPartialPayments (allowPartialPayments): bool? = false` — optional
- `DeliveryMode (deliveryMode): string?` — optional

**`OrderInformation60`** (create + update req) — records-6-Me-Pa:
- `AmountDetails (amountDetails): AmountDetails60` — **!req**
- `LineItems (lineItems): IReadOnlyList<LineItem17>?` — optional (carry the order's line items here)

**`AmountDetails60`** — records-1-Ac-Bi:
- `TotalAmount (totalAmount): string` — **!req** (string, e.g. `"129.98"`)
- `Currency (currency): string` — **!req** — set **`"USD"`** explicitly
- `DiscountAmount`, `DiscountPercent`, `SubAmount`, `MinimumPartialAmount` `: string?`; `TaxDetails: TaxDetails13?`; `Freight: Freight?` — all optional

**`LineItem17`** (each order line) — records-5-In-Me — **all fields nullable/optional**:
- `ProductSku (productSku): string?`, `ProductName (productName): string?`, `Quantity (quantity): int? = 1`,
  `UnitPrice (unitPrice): string?`, `DiscountAmount`, `DiscountPercent`, `TaxAmount`, `TaxRate`,
  `TotalAmount` `: string?`. Map order item description→`ProductName`, unit price→`UnitPrice` (string), qty→`Quantity`.

**`CustomerInformation`** (create + update req; also on responses) — records-3-Cr-Ex:
- `Name (name): string?` — customer name (test fixture)
- `Email (email): string?` — customer email — **required in practice to deliver by email** (generated flag says optional; unverified — see §5)
- `MerchantCustomerId (merchantCustomerId): string?` — **the one app-owned id that round-trips to the list** (read back as `Invoice1.CustomerInformation.MerchantCustomerId`)
- `Company (company): Company6?` — optional

**`MerchantDefinedFieldValue`** (stamp, optional) — records-5-In-Me:
- `DefinitionId (definitionId): long?`, `Value (value): string?` — **NOT present on the list projection `Invoice1`**, so do not rely on it for reconciliation filtering.

**`UpdateInvoiceRequest`** (Models/UpdateInvoiceRequest.cs) — records-11-To-We:
- `CustomerInformation (customerInformation): CustomerInformation?` — optional (set to change customer details)
- `ProcessingInformation (processingInformation): ProcessingInformation72?` — optional
- `InvoiceInformation (invoiceInformation): InvoiceInformation4` — **!req**
- `OrderInformation (orderInformation): OrderInformation60` — **!req** ← see trap: this is a **PUT full-replace**, amount must be re-sent even when "unchanged"
- `MerchantDefinedFieldValues (merchantDefinedFieldValues): IReadOnlyList<MerchantDefinedFieldValue>?` — optional

**`InvoiceInformation4`** (update req) — records-11-To-We:
- `Description (description): string` — **!req**
- `DueDate (dueDate): DateTimeOffset` — **!req** (the field capability 3 changes)
- `ExpirationDate (expirationDate): DateTimeOffset?`; `SendImmediately (sendImmediately): bool? = false`;
  `AllowPartialPayments (allowPartialPayments): bool? = false`; `DeliveryMode (deliveryMode): string?` — optional
- **Note: `InvoiceInformation4` has no `InvoiceNumber` field** — the invoice number is set only at create.

### 2c. Response envelopes — the fields the integration reads

Create/Get/Update/Send/Cancel/Publish responses share this shape (records-5-In-Me). Read one level
**into the envelope** — the payload is not wrapped in a single field, it is the envelope's own properties:

| Field | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | the provider invoice id |
| `Status (status)` | `string?` | **plain string, NOT an enum** — values (e.g. draft/sent/paid/cancelled) are **not modeled anywhere in the SDK**; `UNVERIFIED` (see §5) |
| `SubmitTimeUtc (submitTimeUtc)` | `string?` | |
| `CustomerInformation` | `CustomerInformation?` | Name/Email/MerchantCustomerId/Company |
| `InvoiceInformation` | `InvoiceInformation1?` | **payment link lives here** (see below) |
| `OrderInformation` | `OrderInformation61?` | `AmountDetails: AmountDetails61?` (+ `LineItems`) |
| `Links (_links)` | `Links251?` | HATEOAS: `Self?`, `Update?`, `Deliver?`, `Cancel?` — **not** the customer pay URL |

- **Payment link / customer pay URL**: `response.InvoiceInformation.PaymentLink` —
  `InvoiceInformation1.PaymentLink (paymentLink): string?` (records-5-In-Me). `null` until the invoice
  is delivered. `InvoiceInformation1` also carries `InvoiceNumber (invoiceNumber): string?` so you can
  read your stamp back **on Get-by-id and on the create response** (but not on list — §5).
- **Get-by-id only** (`InvoicingV2InvoicesGet200Response`) additionally carries:
  `InvoiceHistory (invoiceHistory): IReadOnlyList<InvoiceHistory>?` — **"how it got there"**; each
  `InvoiceHistory` = `Event (event): string?`, `Date (date): DateTimeOffset?`,
  `TransactionDetails (transactionDetails): TransactionDetails?`. Also
  `MerchantDefinedFieldValuesWithDefinition (…): IReadOnlyList<MerchantDefinedFieldValuesWithDefinition>?`
  (`Create`/`Update` responses carry the MDF list too, but **not** the history). Source: records-5-In-Me.

### 2d. List response — `InvoicingV2InvoicesAllGet200Response` (records-5-In-Me)

- `Links (_links): Links251?`, `SubmitTimeUtc (submitTimeUtc): string?`,
  `TotalInvoices (totalInvoices): int?` (use to drive paging), `Invoices (invoices): IReadOnlyList<Invoice1>?`
- **`Invoice1`** (records-4-Fe-In) — the per-invoice projection, **thinner than the full record**:
  - `Id (id): string?`
  - `Status (status): string?`
  - `CreatedDate (createdDate): string?` — creation timestamp (client-side filtering only — §5)
  - `CustomerInformation (customerInformation): CustomerInformation2?` → **`Name (name): string?`,
    `MerchantCustomerId (merchantCustomerId): string?`** (no email/company at list time)
  - `InvoiceInformation (invoiceInformation): InvoiceInformation2?` → **only** `DueDate (dueDate): DateTimeOffset?`,
    `ExpirationDate (expirationDate): DateTimeOffset?` — **no `invoiceNumber`, no description**
  - `OrderInformation (orderInformation): OrderInformation62?` → `AmountDetails (amountDetails): AmountDetails62?`
    → `TotalAmount (totalAmount): string?`, `Currency (currency): string?`
- **Reconciliation identity**: the only app-owned value that both sets at create and reads back at
  **list** time is **`CustomerInformation.MerchantCustomerId`** (round-trips as
  `Invoice1.CustomerInformation.MerchantCustomerId`). The `invoiceNumber` you stamp and the
  `MerchantDefinedFieldValues` you attach are **not** in `Invoice1`. See §5 for the design consequence.

### 2e. Enums

**None apply to invoicing.** The SDK has 12 enums (enums.md) and **none is an invoice-status enum**.
`Status` (response) and the `status` list filter are plain `string`. There is therefore **no
map-sourced list of legal status values** — treat status strings as opaque and compare defensively.

### 2f. Client construction, auth, base URL (facts; wiring → REQUIRED READING)

- **Constructor**: `new CyberSourceMergedSpecClient(HttpClient httpClient, CyberSourceMergedSpecClientOptions options)`.
  DI: `services.AddCyberSourceMergedSpecClient(o => { … })` (`ServiceCollectionExtensions.cs`). The
  `HttpClient` is a constructor argument — its lifetime/ownership is yours to get right (trap below).
  Source: sdk-map.md *Getting a client*.
- **Options** (`CyberSourceMergedSpecClientOptions`): `Environment: ServerEnvironment`,
  `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`,
  `Hooks: IReadOnlyList<SdkHook>`. **There is NO credentials property** (sdk-map.md *Servers & auth*).
- **Auth** — this SDK declares **no security scheme**; nothing is wired into `IAuthScheme`. Every
  request is signed by an **opt-in HTTP-Signature `SdkHook`** appended at construction when its
  environment variables resolve, **read once inside the constructor**. The three secrets you named
  (`VISA_MERCHANT_ID`, `VISA_KEY_ID`, `VISA_SECRET_KEY`) feed this hook; the hook reads a **fixed set
  of env-var names it defines itself, plus an opt-in switch** — trap below. Source: sdk-map.md
  *Servers & auth* (hand-edited note) + `dotnet-authentication`.
- **Base URL** — `options.Environment` has **exactly one member, `ServerEnvironment.Production`
  (`CyberSourceMergedSpec.Servers`), whose default base URL is the apitest sandbox host** — so
  selecting an environment does **not** honor `Visa:BaseUrl`. To force `Visa:BaseUrl` verbatim on
  **every** call, set the explicit base-URL override via `options.Server` / the `Servers` layer
  (sdk-map.md: "Base-URL templates and override points live under `Servers/` and `options.Server`").
  Trap below; exact override property → `dotnet-configuration-resilience`.

---

## 3. Trap notes (name the hazard; load the skill — do not implement from these lines)

> ⚠ Step 0 (client & DI) — the `HttpClient` handed to the constructor must be **long-lived and
> reused** (via `IHttpClientFactory`), not rebuilt per request; the SDK client wrapper's lifetime is
> a separate decision. Getting this wrong exhausts sockets or drops the signature hook. **MUST load
> `dotnet-client-initialization`.**

> ⚠ Step 0 (auth) — the HTTP-Signature hook reads its env vars **once, inside the constructor**, so
> they must be present **before** the client is built; the hook expects **specific env-var names it
> defines plus an opt-in enable switch**, and if the app's `VISA_*` user-secret names differ from
> those, or the switch is unset, **every request goes out unsigned while appearing to work locally**.
> There is no credentials property to inspect. Do not assume the three names you have are the four the
> hook reads. **MUST load `dotnet-authentication`.**

> ⚠ Step 0 (base URL) — do **not** rely on `ServerEnvironment.Production` to reach `Visa:BaseUrl`:
> its default host is the sandbox, and the SDK's retry `Timeout` and base-URL override do **not** mean
> what their names suggest. Whether `Visa:BaseUrl` is honored on every call depends on where you set
> the override and what `Timeout` actually bounds. **MUST load `dotnet-configuration-resilience`.**

> ⚠ Step 1 (create body) — request models are records with `init`-only setters; `required` members
> must be set in the initializer; amounts are **strings**; whether an unmodeled JSON field you add is
> even sent, and how `DateTimeOffset dueDate` lands on the wire, are model-layer behaviors. **MUST
> load `dotnet-models`.**

> ⚠ Step 3 (update) — `UpdateInvoice` is a **PUT full-replace**: `UpdateInvoiceRequest` marks both
> `InvoiceInformation4` and `OrderInformation60` (which contains the **required** `AmountDetails60`)
> as required, so "partial update" still forces you to **re-send the amount and description** even
> though the task only changes due date + customer. Re-send the current values; do not send an empty
> order. (Contract fact from records-11/records-6/records-1.)

> ⚠ Steps 3/4/5 (refused transitions) — a state-machine refusal (update/send/cancel on an invoice
> that is already sent or cancelled) surfaces as `SdkException<{Operation}Error>`, but **which HTTP
> status the provider returns for a refusal is not documented** (no operation notes; only 400/404/502
> shapes are typed). Reading the status/`Reason`/`Message` to map it to a 409-style response, and the
> difference between the typed accessors and `TryGetRawError`, is the error boundary's job. **MUST
> load `dotnet-error-handling`.**

> ⚠ Step 6 (list) — call `GetAllInvoices` with **named arguments** (`offset:`, `limit:`, `status:`) —
> `status` has no C# default and mis-binds positionally; page manually with `offset`/`limit` against
> `TotalInvoices` (there is no auto-pager). What retries on a transport failure for a `GET` vs the
> non-idempotent writes is a resilience concern. **MUST load `dotnet-configuration-resilience`** and
> **`dotnet-calling-endpoints`.**

---

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — construction, options/builder, `HttpClient` ownership & lifetime, DI registration |
| `dotnet-authentication` | Step 0 — the HTTP-Signature `SdkHook`, the exact env-var names + opt-in switch, constructor-time read (this SDK has **no** credentials property; unlike every other APIMatic .NET SDK) |
| `dotnet-configuration-resilience` | Step 0 & Step 6 — base-URL/server override so `Visa:BaseUrl` is honored on every call, retry/timeout semantics, pagination |
| `dotnet-calling-endpoints` | Steps 1–6 — invoking operations, named arguments, async/cancellation, request/response envelope shapes |
| `dotnet-models` | Steps 1 & 3 — building request records, `required`/nullability, `DateTimeOffset` wire form, dropped unmodeled fields |
| `dotnet-error-handling` | Steps 1–6 — the try/catch boundary, Case A/B mechanics, reading status/body safely |
| `dotnet-testing` | Tests — the `HttpClient` seam, covering error/edge paths |

**Two `System.Text.Json.JsonException` hazards to bake into the error boundary now (not a later
revision — the boundary is written early):**
- A drifted or malformed **2xx** body (a missing `required` member, or a field whose live type
  differs from the generated one) surfaces as a `JsonException` from deserialization, **not** as an
  `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- "Deliver to customer" = `PerformSendAction` (`…/delivery`). The twin `PerformPublishAction`
  (`…/publication`) is treated as out of the send path unless the provider rejects delivery pending
  publication (its semantics are undocumented — `UNVERIFIED`).
- Draft/not-sent state is achieved by leaving `InvoiceInformation.SendImmediately = false` (the
  documented default) and delivering later via `PerformSendAction`.
- Customer name/email are invented test fixtures placed on `CustomerInformation.Name` / `.Email`.
- USD-only, sandbox/test environment.

**Blockers (resolve before or during implementation — the plan is constrained until then)**
1. **No creation-date-range filter on the list operation.** `GetAllInvoices` accepts **only**
   `offset`, `limit`, `status` — there is **no `from`/`to` (createdDate) query parameter**. A
   provider-side date-range reconciliation query as briefed is **not possible with this SDK**. The
   only date available is `Invoice1.CreatedDate (createdDate): string?`, so a date range can be applied
   **client-side after paging the full set** (page by `offset`/`limit` against `TotalInvoices`). Decide
   whether client-side date filtering over the whole account is acceptable, or whether reconciliation
   must be driven from eShop's own stored provider-ids instead. (operations/Invoices.md; records-4-Fe-In)
2. **The app-owned invoice number does not appear in the list projection.** You can stamp
   `InvoiceInformation.InvoiceNumber` at create and read it back via `GetInvoice(id)` (it is on
   `InvoiceInformation1`), but `Invoice1.InvoiceInformation` is `InvoiceInformation2`, which carries
   **only** `DueDate`/`ExpirationDate` — **no `invoiceNumber`**. `MerchantDefinedFieldValues` is
   likewise absent from `Invoice1`. The **only** app-settable field that round-trips to the list is
   **`CustomerInformation.MerchantCustomerId`**. Design consequence to decide (**YOUR CALL — not in
   the map**): to distinguish eShop-originated invoices at list time you must either (a) overload
   `MerchantCustomerId` with an app-owned marker/token and filter on it after paging, or (b) list ids
   then call `GetInvoice(id)` per row to read `invoiceNumber` (N extra calls). Recommend stamping
   **both** `InvoiceNumber` (for detail/audit) and an app marker in `MerchantCustomerId` (for list
   filtering) at create. (records-4-Fe-In; records-3-Cr-Ex; records-5-In-Me)

**UNVERIFIED (only live traffic can confirm — code defensively, do not hard-code)**
- **Invoice status string values.** `Status` is an un-enumerated `string`; the exact tokens for
  draft/sent/paid/cancelled are not in the map. Directive: compare case-insensitively, do not switch on
  guessed constants, and fall back to surfacing the raw status. (enums.md; records-5-In-Me)
- **`DateTimeOffset dueDate` wire format.** The field is a `DateTimeOffset`; whether the SDK serializes
  it as a calendar date (`yyyy-MM-dd`) or a full offset-datetime, and whether CyberSource accepts that
  form for `dueDate`, is not settled by the map. Directive: construct the due date at UTC midnight
  (`new DateTimeOffset(date, TimeSpan.Zero)`) and, if the provider rejects it, confirm the accepted
  format against live traffic before adjusting. (records-5-In-Me; records-11-To-We)
- **Which HTTP status a refused state transition returns** (update/send/cancel on an already-sent or
  cancelled invoice). Only 400/404/502 shapes are typed and no note maps refusals to a status.
  Directive: in the `catch (SdkException<{Op}Error>)` block try the typed 400 accessor, read
  `Reason`/`Message`; also handle `TryGetRawError` and inspect `StatusCode`; map a refusal to a
  409-style "no longer possible" response best-effort, falling back to the generic error message.
  (operations/Invoices.md; records-5-In-Me)
- **Required-ness beyond the generated `required` flags.** With no operation `<remarks>` in this SDK,
  the provider may insist on fields the generated model marks optional — most notably
  `CustomerInformation` / `CustomerInformation.Email` for delivery. Carry the fields the scope plainly
  needs; do not assume the generated flags are the provider's full requirement set. (operations/Invoices.md)
