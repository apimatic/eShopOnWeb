# Visa / CyberSource Invoicing — integration plan (eShopOnWeb, ASP.NET Core)

SDK: `APIMatic.VisaCyberSource` (`CyberSourceMergedSpec`), map release `v2.0.1` / commit `bbc9181`.
All operations live on `client.Invoices` (source `Api/Invoices.cs`). Every fact below is grounded in
the bundled SDK map or the SDK source; each row cites its page. This SDK has **no** `OneOf`/`AnyOf`
unions — every field is a plain record.

---

## 1. Scope & sequence

| # | Capability | Operation(s) | Notes |
|---|---|---|---|
| 1 | Raise a bill (create, draft) | `client.Invoices.CreateInvoice` | Keep draft by leaving `InvoiceInformation.SendImmediately` at its default `false` and **not** calling send. |
| 2 | Get a bill's state | `client.Invoices.GetInvoice` | Payment link + status history are on this response only. |
| 3 | Correct a bill (pre-send) | `client.Invoices.UpdateInvoice` | `PUT` replaces the body; amount block is **required by the model even though you are "not changing" it** — re-supply it (see §5). |
| 4 | Issue the bill (send to customer) | `client.Invoices.PerformSendAction` | `POST …/delivery`. This is the "send to customer" transition. (`PerformPublishAction` = `POST …/publication` is a *different*, undocumented action — see Blockers.) |
| 5 | Withdraw the bill (cancel) | `client.Invoices.PerformCancelAction` | `POST …/cancelation`. |
| 6 | List / reconcile | `client.Invoices.GetAllInvoices` | Returns ALL account invoices; only `offset`/`limit`/`status` filters — **no date-range params** (see §5, Blocker B). |

Client construction, DI, base-URL binding and auth are cross-cutting — see §4 and the trap notes.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it.** Take each
> `using` from the type's own row. Enums, server/client-config and error types live in *different*
> child namespaces from the `Models` records, and two types configured side by side (e.g.
> `ServerOptions` in the root namespace vs `DefaultOptions` in `.Servers`) routinely differ. Dropping
> a type to the root or to `.Models` makes the implementer guess the wrong `using` and the build breaks.

**Namespaces used below**
- Records (all request/response/nested models, and the `InvoicingV2…Response1` error payloads): `CyberSourceMergedSpec.Models`
- Error classes (`CreateInvoiceError`, `UpdateInvoiceError`, …): `CyberSourceMergedSpec.Errors`
- `SdkException<T>`: `CyberSourceMergedSpec.Core.Exceptions`
- `RawError`, `ApiError`: `CyberSourceMergedSpec.Core.ErrorResponse`
- Client & options (`CyberSourceMergedSpecClient`, `CyberSourceMergedSpecClientOptions`, `ServerOptions`): root `CyberSourceMergedSpec`
- `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.ProductionOptions`): `CyberSourceMergedSpec.Servers`

### 2a. Operations

Every operation below is **Case A (typed)** — `SdkException<{Operation}Error>` — with the same accessor
shape: `TryGet…400Response1` [400], `TryGet…404Response1` [404], `TryGet…502Response1` [502], and the
inherited `TryGetRawError(out RawError)` [fallback]. No operation has a no-throw `…Result` variant. No
operation paginates via a cursor. `RequestOptions? requestOptions = null` and `CancellationToken ct = default`
are the trailing params on every signature (omitted below for brevity — pass `ct:` by name if used).

| Op | Signature (params in order) | Request model | Returns (envelope) | Error type + payload | Source |
|---|---|---|---|---|---|
| **CreateInvoice** | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, …)` | `CreateInvoiceRequest` | `InvoicingV2InvoicesPost201Response` | `CreateInvoiceError`; payloads `InvoicingV2InvoicesPost400Response1` / `…404Response1` / `…502Response1` | operations/Invoices.md |
| **GetInvoice** | `GetInvoice(string id, …)` | — | `InvoicingV2InvoicesGet200Response` | `GetInvoiceError`; `…Get400/404/502Response1` | operations/Invoices.md |
| **UpdateInvoice** | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, …)` | `UpdateInvoiceRequest` | `InvoicingV2InvoicesPut200Response` | `UpdateInvoiceError`; `…Put400/404/502Response1` | operations/Invoices.md |
| **PerformSendAction** | `PerformSendAction(string id, …)` | — (no body) | `InvoicingV2InvoicesSend200Response` | `PerformSendActionError`; `…Send400/404/502Response1` | operations/Invoices.md |
| **PerformCancelAction** | `PerformCancelAction(string id, …)` | — (no body) | `InvoicingV2InvoicesCancel200Response` | `PerformCancelActionError`; `…Cancel400/404/502Response1` | operations/Invoices.md |
| **GetAllInvoices** | `GetAllInvoices(int offset, int limit, string? status, …)` | — | `InvoicingV2InvoicesAllGet200Response` | `GetAllInvoicesError`; `…AllGet400/404/502Response1` | operations/Invoices.md |
| **PerformPublishAction** | `PerformPublishAction(string id, …)` | — (no body) | `InvoicingV2InvoicesPublish200Response` | `PerformPublishActionError`; `…Publish400/404/502Response1` | operations/Invoices.md |

`GetAllInvoices` query params (wire ← C#): `offset ← offset`, `limit ← limit`, `status ← status`.
`status` is nullable **with no default → you MUST pass it explicitly** (pass `null` for "no status filter").

### 2b. Request models

`!req` = C# `required` (must be set in the object initializer). A trailing `?` = nullable/optional.
Field shown as `CSharpName (wire_name): Type`.

**`CreateInvoiceRequest`** — source `records-3-Cr-Ex.md`
- `ClientReferenceInformation (clientReferenceInformation): ClientReferenceInformation78?` — optional order/code ref (NOT returned in the list projection — see reconciliation note)
- `CustomerInformation (customerInformation): CustomerInformation?`
- `ProcessingInformation (processingInformation): ProcessingInformation72?`
- `InvoiceInformation (invoiceInformation): InvoiceInformation` **!req**
- `OrderInformation (orderInformation): OrderInformation60` **!req**
- `MerchantDefinedFieldValues (merchantDefinedFieldValues): IReadOnlyList<MerchantDefinedFieldValue>?`

**`InvoiceInformation`** (create's invoice block) — source `records-5-In-Me.md`
- `InvoiceNumber (invoiceNumber): string?`
- `Description (description): string` **!req**
- `DueDate (dueDate): DateTimeOffset` **!req**  ← the calendar due date (serialized as a date-time)
- `ExpirationDate (expirationDate): DateTimeOffset?`
- `SendImmediately (sendImmediately): bool? = false`  ← **the draft/send flag.** Leave `false`/unset to keep the invoice a draft; `true` sends on create.
- `AllowPartialPayments (allowPartialPayments): bool? = false`
- `DeliveryMode (deliveryMode): string?`

**`OrderInformation60`** (create's order block, **!req**) — source `records-6-Me-Pa.md`
- `AmountDetails (amountDetails): AmountDetails60` **!req**
- `LineItems (lineItems): IReadOnlyList<LineItem17>?`

**`AmountDetails60`** — source `records-1-Ac-Bi.md`
- `TotalAmount (totalAmount): string` **!req**  ← amount as a **string**, e.g. `"49.99"`
- `Currency (currency): string` **!req**  ← plain string, set `"USD"` (no enum exists)
- `DiscountAmount?`, `DiscountPercent?`, `SubAmount?`, `MinimumPartialAmount?` — all `string?`
- `TaxDetails (taxDetails): TaxDetails13?`, `Freight (freight): Freight?`

**`LineItem17`** (one per catalog line) — source `records-5-In-Me.md`
- `ProductSku (productSku): string?`, `ProductName (productName): string?`
- `Quantity (quantity): int? = 1`
- `UnitPrice (unitPrice): string?`  ← string
- `DiscountAmount?`, `DiscountPercent?`, `TaxAmount?`, `TaxRate?`, `TotalAmount?` — all `string?`

**`CustomerInformation`** (name/email) — source `records-3-Cr-Ex.md`
- `Name (name): string?`
- `Email (email): string?`
- `MerchantCustomerId (merchantCustomerId): string?`  ← **your eShop-side id; survives into the list projection — use it to reconcile (see note below)**
- `Company (company): Company6?`

**`UpdateInvoiceRequest`** — source `records-11-To-We.md`
- `CustomerInformation (customerInformation): CustomerInformation?`
- `ProcessingInformation (processingInformation): ProcessingInformation72?`
- `InvoiceInformation (invoiceInformation): InvoiceInformation4` **!req**  (note: `InvoiceInformation4`, NOT `InvoiceInformation`)
- `OrderInformation (orderInformation): OrderInformation60` **!req**  ← **amount block is required even though "amount is not changed here" — you must re-send it (Blocker A).**
- `MerchantDefinedFieldValues (merchantDefinedFieldValues): IReadOnlyList<MerchantDefinedFieldValue>?`

**`InvoiceInformation4`** (update's invoice block, **!req**) — source `records-5-In-Me.md`
- `Description (description): string` **!req**
- `DueDate (dueDate): DateTimeOffset` **!req**  ← the correctable due date
- `ExpirationDate (expirationDate): DateTimeOffset?`
- `SendImmediately (sendImmediately): bool? = false`
- `AllowPartialPayments (allowPartialPayments): bool? = false`
- `DeliveryMode (deliveryMode): string?`
- (no `InvoiceNumber` field — differs from the create block)

### 2c. Response models — the fields the integration reads

All response records live in `CyberSourceMergedSpec.Models`. **`Status` is a plain `string?` on every
response — there is NO status enum (see §5, Blocker C for the value list).**

**`InvoicingV2InvoicesPost201Response`** (create result) — source `records-5-In-Me.md`
- `Id (id): string?`  ← **the created invoice's provider id** (return this)
- `Status (status): string?`  ← draft/state string (return this)
- `SubmitTimeUtc (submitTimeUtc): string?`
- `Links (_links): Links251?`, `CustomerInformation?`, `ProcessingInformation72?`
- `InvoiceInformation (invoiceInformation): InvoiceInformation1?`, `OrderInformation (orderInformation): OrderInformation61?`
- `MerchantDefinedFieldValuesWithDefinition (…): IReadOnlyList<MerchantDefinedFieldValuesWithDefinition>?`

**`InvoicingV2InvoicesGet200Response`** (get-by-id) — source `records-5-In-Me.md` — same fields as Post201 **plus**:
- `InvoiceHistory (invoiceHistory): IReadOnlyList<InvoiceHistory>?`  ← **the "how it reached this state" history**
- `InvoiceInformation (invoiceInformation): InvoiceInformation1?`  ← carries `PaymentLink` (below)

**`InvoiceInformation1`** (response invoice block) — source `records-5-In-Me.md`
- `PaymentLink (paymentLink): string?`  ← **THE customer-facing pay link**
- `InvoiceNumber?`, `Description?`, `DueDate (DateTimeOffset)?`, `ExpirationDate?`, `AllowPartialPayments? = false`, `DeliveryMode?`, `CustomLabels?`

**`Links251`** (HATEOAS action links; each sub-type is `{ Href (href): string?, Method (method): string? }`) — source `records-5-In-Me.md`
- `Self (self): Self?`, `Update (update): Update?`, `Deliver (deliver): Deliver?`, `Cancel (cancel): Cancel?`
- These are action endpoints for this invoice, **not** the customer pay page — the customer pay URL is `InvoiceInformation1.PaymentLink` above. (`Self`→`records-10-Ri-To.md`, `Update`→`records-11-To-We.md`, `Deliver`→`records-3-Cr-Ex.md`, `Cancel`→`records-2-Bi-Cr.md`.)

**`InvoiceHistory`** (status-history entry) — source `records-5-In-Me.md`
- `Event (event): string?`, `Date (date): DateTimeOffset?`, `TransactionDetails (transactionDetails): TransactionDetails?`

**`OrderInformation61`** (response order block) — source `records-6-Me-Pa.md`
- `AmountDetails (amountDetails): AmountDetails61?`, `LineItems (lineItems): IReadOnlyList<LineItem17>?`

**`AmountDetails61`** — source `records-1-Ac-Bi.md`
- `TotalAmount?`, `Currency?`, `BalanceAmount?` (+ discount/sub/min/tax/freight, all optional)

**`InvoicingV2InvoicesPut200Response`** — same shape as Post201 (id/status/links/customer/invoiceInformation1/orderInformation61/merchantDefinedFieldValuesWithDefinition). Source `records-5-In-Me.md`.

**`InvoicingV2InvoicesSend200Response` / `…Publish200Response` / `…Cancel200Response`** — source `records-5-In-Me.md`
- `Links (Links251)?`, `Id?`, `SubmitTimeUtc?`, `Status?`, `CustomerInformation?`, `ProcessingInformation72?`, `InvoiceInformation (InvoiceInformation1)?`, `OrderInformation (OrderInformation61)?`
- Read `Status` to confirm the transition; `InvoiceInformation1.PaymentLink` is available after send.

**`InvoicingV2InvoicesAllGet200Response`** (list/reconcile) — source `records-5-In-Me.md`
- `TotalInvoices (totalInvoices): int?`  ← use for paging math against `offset`/`limit`
- `Invoices (invoices): IReadOnlyList<Invoice1>?`
- `Links (Links251)?`, `SubmitTimeUtc?`

**`Invoice1`** (one list row — the reconciliation projection) — source `records-4-Fe-In.md`
- `Id (id): string?`, `Status (status): string?`, `CreatedDate (createdDate): string?`  ← **created date is a `string`, not `DateTimeOffset`, on list rows**
- `CustomerInformation (customerInformation): CustomerInformation2?`
- `InvoiceInformation (invoiceInformation): InvoiceInformation2?`
- `OrderInformation (orderInformation): OrderInformation62?`
- `Links (Links251)?`

**`CustomerInformation2`** (list projection — note: **no `Email`**) — source `records-3-Cr-Ex.md`
- `Name (name): string?`, `MerchantCustomerId (merchantCustomerId): string?`  ← the reconciliation discriminator

**`InvoiceInformation2`** (list projection) — source `records-5-In-Me.md`: `DueDate (DateTimeOffset)?`, `ExpirationDate?` only.

**`OrderInformation62`** (list projection) — source `records-6-Me-Pa.md`: `AmountDetails (AmountDetails62)?` only.
**`AmountDetails62`** — source `records-1-Ac-Bi.md`: `TotalAmount (string)?`, `Currency (string)?` only.

### 2d. Error payload records (Case A `out` types)

Every `InvoicingV2…Response1` payload (Post/Get/Put/Send/Cancel/Publish/AllGet 400/404) has the same
shape and lives in `CyberSourceMergedSpec.Models` (source `records-5-In-Me.md`):
`SubmitTimeUtc (string)?`, `Status (status): string?`, `Reason (reason): string?`, `Message (message): string?`,
`Details (details): IReadOnlyList<Detail>?`. The `502Response1` variants drop `Details`. Read `Reason` /
`Message` for the provider's human-readable cause (this is where a "transition refused" text appears).

### 2e. Enum tables

**None apply.** Invoice `Status`, `Currency`, `DeliveryMode`, and the `GetAllInvoices` `status` filter are
all plain `string` in this SDK — the map's `enums.md` (12 enums) contains **no** invoice-status,
currency, or collection-method enum. Set `Currency = "USD"` directly. Source: `enums.md`.

### 2f. Client construction, base URL, auth, DI (grounded facts)

- **Client / options** (source: SDK `CyberSourceMergedSpecClient.cs`, `CyberSourceMergedSpecClientOptions.cs`; map `sdk-map.md`):
  Constructor `new CyberSourceMergedSpecClient(HttpClient httpClient, CyberSourceMergedSpecClientOptions options)`.
  Options members: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Logging`, `Server` (`ServerOptions`), `Hooks`.
- **DI**: `services.AddCyberSourceMergedSpecClient(o => { /* set options */ })` (extension on `IServiceCollection`;
  registers `HttpClient` via `AddHttpClient()`/`IHttpClientFactory` and the client as a singleton). Source: SDK `ServiceCollectionExtensions.cs`.
- **Environment**: `ServerEnvironment` has exactly one member, `ServerEnvironment.Production` (default), whose
  **default base URL is the SANDBOX host `https://apitest.cybersource.com/`** — the name says "production"
  but the URL is sandbox. Source: SDK `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`.
- **Base URL binding (`Visa:BaseUrl`)** — to route every call through a configured URL **verbatim**, set the
  server override (there is no "custom environment" enum member; you override the URL on the single Production
  node). Grounded in SDK source (`ServerOptions.cs`, `Servers/DefaultOptions.cs`):
  ```csharp
  o.Server = new ServerOptions {                                   // CyberSourceMergedSpec
      Default = new DefaultOptions {                               // CyberSourceMergedSpec.Servers
          Production = new DefaultOptions.ProductionOptions {      // nested
              BaseUrl = configuredVisaBaseUrl                      // used verbatim as the URL template base
          }
      }
  };
  ```
  Bind `configuredVisaBaseUrl` from configuration key `Visa:BaseUrl`; if unset, the SDK default is the
  sandbox host above. (See the resilience trap note for what else `options.Server`/retry/timeout affect.)

---

## 3. Reconciliation guidance (capability 6)

- `GetAllInvoices` returns **ALL** invoices on the account (`TotalInvoices` + `Invoices`); there is **no
  merchant-tag/owner filter parameter** — only `status`. Confirmed: the list is account-wide, so eShop's own
  invoices are interleaved with other activity. Source: operations/Invoices.md.
- The only per-row discriminator that both (a) you can set at create time and (b) survives into the
  `Invoice1` list projection is **`CustomerInformation.MerchantCustomerId`** (create) →
  `CustomerInformation2.MerchantCustomerId` (list). `ClientReferenceInformation78` and
  `MerchantDefinedFieldValues` are **not** present on `Invoice1`, so they cannot be used to line up list rows.
  Set `MerchantCustomerId` to a stable eShop identifier at create time and match on it during reconciliation.
- Page with `offset`/`limit` against `TotalInvoices`. `Invoice1.CreatedDate` is a **string** — parse it
  yourself for any date-range narrowing (see Blocker B: the API has no from/to filter).

---

## 4. Trap notes (load the named skill before coding that step)

> ⚠ **Step: client & DI registration.** `AddCyberSourceMergedSpecClient` registers an `HttpClient` — whether
> that handler pipeline is reused correctly across requests (vs rebuilt per call) and what the client's own
> lifetime should be is not visible in the signature. **MUST load `dotnet-client-initialization`** before
> wiring the client into DI.

> ⚠ **Step: authentication.** This SDK has **no credentials property**. Every request is signed by an opt-in
> HTTP Signature `SdkHook` resolved **once inside the client constructor** from environment variables — the
> enable switch is `APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE` (must equal exactly `"true"`) plus
> `VISA_MERCHANT_ID`, `VISA_KEY_ID`, `VISA_SECRET_KEY`. If the switch is not `"true"` the hook is **not added
> and every request goes out UNSIGNED while appearing to work locally**; if the switch is `"true"` but any of
> the three values is missing/blank the constructor **throws** `VisaHttpSignatureConfigurationError`. Because
> the values are read at construction, they must be set **before** the client is built. How to wire this
> safely (DI ordering, where the env must be populated) — **MUST load `dotnet-authentication`.**

> ⚠ **Step: building request payloads / reading responses.** Enums here are `StringEnum<T>` not C# enums, and
> unmodeled JSON fields are dropped on deserialize — how that bites when you map SDK models onto eShop domain
> types is not visible in a field list. **MUST load `dotnet-models`** before constructing `CreateInvoiceRequest`/
> `UpdateInvoiceRequest` or mapping responses.

> ⚠ **Step: base URL / retries / timeouts.** `options.Server` overrides the URL (shown in §2f), but which
> calls retry, what `Retry.Timeout` actually bounds (per-attempt vs whole call), and whether a non-idempotent
> write (`CreateInvoice`, `PerformSendAction`) can be re-sent on a transport failure are NOT answerable from
> the option names. **MUST load `dotnet-configuration-resilience`** before tuning the client — this matters
> directly here because re-sending a create/send is a double-bill hazard.

> ⚠ **Step: error boundary (updating a sent invoice, etc.).** The provider legitimately refuses updates once
> an invoice is sent/canceled; that refusal surfaces as `SdkException<UpdateInvoiceError>`, read via
> `TryGetInvoicingV2InvoicesPut400Response1(out …)` then `TryGetRawError(out …)` fallback, with the human cause
> in the payload's `Reason`/`Message`. **MUST load `dotnet-error-handling`** before writing any try/catch —
> see REQUIRED READING for two `JsonException` hazards that an SDK-exception-only ladder misses.

---

## 5. Assumptions & Blockers

**Assumptions (ordinary design choices I made):**
- "Draft" = create with `InvoiceInformation.SendImmediately` left at its default `false` and no send call; "issue" = `PerformSendAction` (`…/delivery`). Grounded in the model/route names.
- Reconciliation keys on `CustomerInformation.MerchantCustomerId` because it is the only create-time value that appears on the `Invoice1` list projection (§3).
- Amounts are passed as strings (the SDK types are `string`), formatted to 2 decimals, currency literal `"USD"`.

**Blockers / gaps the map does not resolve — resolve before or during implementation:**

- **Blocker A — Update requires the amount block.** `UpdateInvoiceRequest.OrderInformation` is
  `OrderInformation60` **!req** (and its `AmountDetails60.TotalAmount`/`Currency` are `!req`), so a "change
  due date / customer only" update must still re-send the full amount block. The requirement said "amount is
  NOT changed here" — the contract forces you to re-supply it unchanged. Decide where the source-of-truth
  amount comes from (re-read from the stored order) so the resend is exactly the original. Source: `records-11-To-We.md`, `records-1-Ac-Bi.md`.
- **Blocker B — No date-range filter on the list.** `GetAllInvoices` exposes only `offset`, `limit`, `status`
  — there are **no from/to date-time parameters** anywhere in the signature. The requested ISO-8601 date-range
  reconciliation must be done **client-side**: page the full account list and filter on `Invoice1.CreatedDate`
  (a `string` you parse). Confirm this is acceptable for the account's invoice volume. Source: operations/Invoices.md.
- **Blocker C — Invoice status values are undocumented (`UNVERIFIED`).** `Status` is a plain `string?` on
  every response and there is no status enum in the SDK, so the exact literals for draft / created / sent /
  paid / canceled and their meanings are **not in the map or the SDK source** (only live traffic or CyberSource
  product docs can confirm them). Directive: do **not** hard-code a status string as a control-flow gate that
  silently mishandles an unrecognized value — compare defensively and treat unknown statuses as "unrecognized,
  surface for review" rather than assuming a state. Label `UNVERIFIED`.
- **Blocker D — `PerformPublishAction` vs `PerformSendAction` (`UNVERIFIED`).** Two transitions exist:
  `…/delivery` (`PerformSendAction`) and `…/publication` (`PerformPublishAction`). The map carries **no Notes**
  for any operation, so the precise semantic difference (and whether "publish" is a prerequisite of "send") is
  not documented. This plan maps "send to customer" to `PerformSendAction`. If the first live send is rejected
  for a state reason, revisit whether `PerformPublishAction` must precede it. Label `UNVERIFIED`.
- **General (`UNVERIFIED`) — required-ness beyond the generated `required` flags.** This SDK's operation rows
  carry **no** provider `<remarks>`/Notes, so which *optional* fields the provider actually insists on
  (e.g. whether `CustomerInformation.Email` is mandatory for a deliverable invoice) is not marked anywhere and
  no compiler catches it. Carry the fields the scope plainly needs (customer name+email, line items, amount,
  due date); treat any 400 on create/send as possibly a missing-but-optional-in-C# field and read `Reason`/`Message`.

---

## 6. REQUIRED READING — load BEFORE implementation starts

This sheet deliberately does **not** carry these skills' contents; load each before writing the step it governs.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Building/registering the client, `HttpClient` lifetime, DI. |
| `dotnet-authentication` | The HTTP Signature hook + env vars (this SDK is unlike every other APIMatic .NET SDK — always required). |
| `dotnet-calling-endpoints` | Calling `client.Invoices.*`, named args, `ct:`, response envelopes. |
| `dotnet-models` | Building `CreateInvoiceRequest`/`UpdateInvoiceRequest`, `StringEnum`, dropped unmodeled fields. |
| `dotnet-configuration-resilience` | Base-URL override, retries, timeout semantics, list paging — double-bill hazard on create/send. |
| `dotnet-error-handling` | The try/catch boundary, Case-A accessors, transition-refused handling (always required). |
| `dotnet-testing` | Testing the integration (fake the `HttpClient` seam). |

**Two `JsonException` hazards for the error boundary — load `dotnet-error-handling` before writing it:**
- A drifted or malformed **2xx** body (a missing `required` member, or a field whose live type differs from
  the generated one) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an
  `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that
  can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.
