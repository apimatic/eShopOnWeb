# Visa / CyberSource Invoicing — Integration Plan & Contract Sheet

SDK: `APIMatic.VisaCyberSource` · root namespace `CyberSourceMergedSpec` · map release tag `v2.0.1`
(source commit `bbc9181`). Target: eShopOnWeb, customer invoicing (bill shoppers for orders),
USD, CyberSource **test** environment. All calls go through the SDK — it is the sole reference.

Every contract fact below cites the map page (or SDK source file) it came from. Anything the map
could not settle is labelled `UNVERIFIED` or raised in §5 (Assumptions & Blockers). **Read §5 first**
— two of the six requested capabilities (date-range reconciliation listing, and status-value
semantics) are only partially supported by the SDK and shape the design.

---

## 1. Scope & sequence

All seven invoice operations live on one controller, `client.Invoices` (source `Api/Invoices.cs`).
The six requested capabilities map onto them as follows:

| # | Capability | Operation | HTTP |
|---|---|---|---|
| 1 | Create a **draft** invoice (not yet delivered) | `CreateInvoice` | `POST /invoicing/v2/invoices` |
| 2 | Get an invoice by provider id (status, history, payment link) | `GetInvoice` | `GET /invoicing/v2/invoices/{id}` |
| 3 | Update / correct a draft invoice (due date, customer) | `UpdateInvoice` | `PUT /invoicing/v2/invoices/{id}` |
| 4 | Send / deliver ("issue") the invoice to the customer | `PerformSendAction` | `POST /invoicing/v2/invoices/{id}/delivery` |
| 5 | Cancel ("withdraw") the invoice | `PerformCancelAction` | `POST /invoicing/v2/invoices/{id}/cancelation` |
| 6 | List invoices (pagination + status filter) | `GetAllInvoices` | `GET /invoicing/v2/invoices` |

A seventh operation exists — `PerformPublishAction` (`POST …/{id}/publication`) — whose role vs.
`PerformSendAction` the map does not document (see §5, Blocker B4). Implement steps in the order
1 → 2 → 3 → 4 → 5, then 6 as an independent reconciliation path.

Suggested implementation steps:
1. **Client registration & base-URL/auth wiring** (uses no operation; see §"Client construction").
2. **Create draft** (`CreateInvoice`) — build request from an eShopOnWeb order.
3. **Read back** (`GetInvoice`) — surface status, history, payment link.
4. **Correct draft** (`UpdateInvoice`) — due date + customer only.
5. **Deliver** (`PerformSendAction`) then **Cancel** (`PerformCancelAction`) transitions.
6. **Reconciliation list** (`GetAllInvoices`) — paginate; date-range filtering is client-side (Blocker B1).
7. **Error boundary** around every call (see REQUIRED READING).

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

**Namespaces in play** (from `sdk-map.md` → *Namespaces by content type*):
`CyberSourceMergedSpec` (client, options, `Server`/`ServerOptions`) ·
`CyberSourceMergedSpec.Api` (the `Invoices` controller) ·
`CyberSourceMergedSpec.Models` (all request/response records below) ·
`CyberSourceMergedSpec.Models.Enums` (enums — **note: no invoice-status enum exists**, see §4) ·
`CyberSourceMergedSpec.Errors` (the `{Operation}Error` types) ·
`CyberSourceMergedSpec.Servers` (`ServerEnvironment`, `DefaultOptions`).

### 2.1 Operations table

| Capability | Method signature (params in order) | Request model + key fields | Response type + fields the integration reads | Error case + accessors + payload type | Pagination | Source |
|---|---|---|---|---|---|---|
| **1. Create draft** | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateInvoiceRequest` — `InvoiceInformation (invoiceInformation): InvoiceInformation !req`, `OrderInformation (orderInformation): OrderInformation60 !req`, `CustomerInformation (customerInformation): CustomerInformation?`, `ClientReferenceInformation (clientReferenceInformation): ClientReferenceInformation78?`, `ProcessingInformation (processingInformation): ProcessingInformation72?`, `MerchantDefinedFieldValues (merchantDefinedFieldValues): IReadOnlyList<MerchantDefinedFieldValue>?` | `InvoicingV2InvoicesPost201Response` — read `Id (id): string?`, `Status (status): string?`, `InvoiceInformation (InvoiceInformation1?)` → `PaymentLink (paymentLink): string?` / `InvoiceNumber` / `DueDate`, `OrderInformation (OrderInformation61?)` → `AmountDetails (AmountDetails61?)` → `TotalAmount`,`Currency` + `LineItems (IReadOnlyList<LineItem17>?)`, `Links (Links251?)`, `SubmitTimeUtc (submitTimeUtc): string?` (no top-level createdDate) | Case A `SdkException<CreateInvoiceError>` · `TryGetInvoicingV2InvoicesPost400Response1(out …)` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError(out RawError)` [fallback]. Payload types e.g. `InvoicingV2InvoicesPost400Response1` (fields `SubmitTimeUtc`,`Status`,`Reason`,`Message`,`Details: IReadOnlyList<Detail>?`) | none | `operations/Invoices.md`; models `records-3-Cr-Ex.md`, `records-5-In-Me.md` |
| **2. Get by id** | `GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path param `id` (the provider invoice id from create) | `InvoicingV2InvoicesGet200Response` — `Id`, `Status (string?)`, `InvoiceInformation (InvoiceInformation1?)` → `PaymentLink`, `OrderInformation (OrderInformation61?)`, `InvoiceHistory (invoiceHistory): IReadOnlyList<InvoiceHistory>?` (each: `Event (event): string?`, `Date (date): DateTimeOffset?`, `TransactionDetails`) — this is "how it got there", `MerchantDefinedFieldValuesWithDefinition (…)?`, `Links (Links251?)` | Case A `SdkException<GetInvoiceError>` · `TryGetInvoicingV2InvoicesGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | `operations/Invoices.md`; `records-5-In-Me.md` |
| **3. Update draft** | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `UpdateInvoiceRequest` — `InvoiceInformation (invoiceInformation): InvoiceInformation4 !req`, `OrderInformation (orderInformation): OrderInformation60 !req`, `CustomerInformation (customerInformation): CustomerInformation?`, `ProcessingInformation (…): ProcessingInformation72?`, `MerchantDefinedFieldValues (…)?`. **Updatable fields (map-visible): `InvoiceInformation4` = `Description !req`, `DueDate !req`, `ExpirationDate?`, `SendImmediately? = false`, `AllowPartialPayments? = false`, `DeliveryMode?`; `CustomerInformation` = `Name?`,`Email?`,`MerchantCustomerId?`,`Company (Company6?)`.** Note `OrderInformation60` (amount) is `!req` on the update body too — see §5 A2. | `InvoicingV2InvoicesPut200Response` — same shape as Get (Id, Status, InvoiceInformation1, OrderInformation61, MerchantDefined…) | Case A `SdkException<UpdateInvoiceError>` · `TryGetInvoicingV2InvoicesPut400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | `operations/Invoices.md`; `records-11-To-We.md`, `records-5-In-Me.md`, `records-3-Cr-Ex.md` |
| **4. Send / deliver** | `PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path param `id` only — **no request body** | `InvoicingV2InvoicesSend200Response` — `Id`, `Status`, `InvoiceInformation (InvoiceInformation1?)` → `PaymentLink (paymentLink): string?` (the customer pay URL), `OrderInformation`, `Links (Links251?)` | Case A `SdkException<PerformSendActionError>` · `TryGetInvoicingV2InvoicesSend400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | `operations/Invoices.md`; `records-5-In-Me.md` |
| **5. Cancel** | `PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path param `id` only — **no request body** | `InvoicingV2InvoicesCancel200Response` — `Id`, `Status (string?)`, `InvoiceInformation`, `OrderInformation`, `Links` | Case A `SdkException<PerformCancelActionError>` · `TryGetInvoicingV2InvoicesCancel400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | `operations/Invoices.md`; `records-5-In-Me.md` |
| **6. List** | `GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query params `offset` (int), `limit` (int), `status` (string?, nullable **but no C# default → must pass explicitly**, pass `null` for all statuses). **No date-range params — see Blocker B1.** | `InvoicingV2InvoicesAllGet200Response` — `TotalInvoices (totalInvoices): int?`, `Invoices (invoices): IReadOnlyList<Invoice1>?`, `Links (Links251?)`, `SubmitTimeUtc`. Each `Invoice1`: `Id (id): string?`, `Status (status): string?`, `CreatedDate (createdDate): string?`, `CustomerInformation (CustomerInformation2?)` = `Name?`,`MerchantCustomerId?`, `InvoiceInformation (InvoiceInformation2?)` = `DueDate?`,`ExpirationDate?` **(no invoiceNumber)**, `OrderInformation (OrderInformation62?)` → `AmountDetails (AmountDetails62?)` = `TotalAmount?`,`Currency?` | Case A `SdkException<GetAllInvoicesError>` · `TryGetInvoicingV2InvoicesAllGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | offset/limit only (manual paging via `offset`/`limit`; no cursor helper) | `operations/Invoices.md`; `records-5-In-Me.md`, `records-4-Fe-In.md`, `records-6-Me-Pa.md`, `records-3-Cr-Ex.md` |

### 2.2 Request sub-model field lists (build from these)

All records below are in `CyberSourceMergedSpec.Models` (source: `records-*.md` as cited). Records
are immutable with `init` setters; `!req` = C# `required` (must be set in the initializer); a
trailing `?` = optional/nullable; `= x` = generated default. **All amounts and the currency are
`string`, not decimal** — format the order total/unit prices as strings; `Currency = "USD"`.

**`InvoiceInformation`** (Create; `records-5-In-Me.md`) — summary: "invoice-specific fields":
- `InvoiceNumber (invoiceNumber): string?`
- `Description (description): string !req`
- `DueDate (dueDate): DateTimeOffset !req`
- `ExpirationDate (expirationDate): DateTimeOffset?`
- `SendImmediately (sendImmediately): bool? = false`  ← **leave false/unset to keep the invoice a draft** (do not deliver on create)
- `AllowPartialPayments (allowPartialPayments): bool? = false`
- `DeliveryMode (deliveryMode): string?`  ← plain string, valid values UNVERIFIED (see §5 A3)

**`InvoiceInformation4`** (Update; `records-5-In-Me.md`) — summary: "updatable invoice information":
- `Description (description): string !req`, `DueDate (dueDate): DateTimeOffset !req`, `ExpirationDate?`, `SendImmediately? = false`, `AllowPartialPayments? = false`, `DeliveryMode (deliveryMode): string?`

**`OrderInformation60`** (Create + Update; `records-6-Me-Pa.md`):
- `AmountDetails (amountDetails): AmountDetails60 !req`
- `LineItems (lineItems): IReadOnlyList<LineItem17>?`

**`AmountDetails60`** (`records-1-Ac-Bi.md`):
- `TotalAmount (totalAmount): string !req`, `Currency (currency): string !req` (= `"USD"`), `DiscountAmount?`, `DiscountPercent?`, `SubAmount?`, `MinimumPartialAmount?`, `TaxDetails (TaxDetails13?)`, `Freight (Freight?)`

**`LineItem17`** (`records-5-In-Me.md`) — summary "Line item from the order":
- `ProductSku (productSku): string?`, `ProductName (productName): string?`, `Quantity (quantity): int? = 1`, `UnitPrice (unitPrice): string?`, `DiscountAmount?`, `DiscountPercent?`, `TaxAmount?`, `TaxRate?`, `TotalAmount (totalAmount): string?`
  - Maps the brief's "name, quantity, unit amount": `ProductName`, `Quantity`, `UnitPrice`.

**`CustomerInformation`** (Create + Update customer details; `records-3-Cr-Ex.md`):
- `Name (name): string?`, `Email (email): string?`, `MerchantCustomerId (merchantCustomerId): string?`, `Company (company): Company6?`
  - `MerchantCustomerId` is the one identifying field that also comes back in the **list** items — see §5 A4 for using it to tell "our" invoices apart.

**`ClientReferenceInformation78`** (Create only; `records-2-Bi-Cr.md`):
- `Partner (partner): Partner38?` — **only a `Partner` sub-object; no merchant reference-code / `code` field.** Cannot be used to tag our invoices with an order id.

### 2.3 Response reading — payment link & created date

- **Payment link / customer pay URL**: `response.InvoiceInformation?.PaymentLink` (`InvoiceInformation1.PaymentLink (paymentLink): string?`). Present on the Get, Post(201), Send, Publish, Cancel, and Put responses. Whether it is populated before delivery is UNVERIFIED (§5 A5) — read best-effort, treat `null` as "not yet available".
- **`Links251`** (`records-5-In-Me.md`) carries HATEOAS action links `Self`, `Update`, `Deliver`, `Cancel` — **not** the pay URL; the pay URL is `InvoiceInformation1.PaymentLink`.
- **Created date**: only exposed on **list** items (`Invoice1.CreatedDate (createdDate): string?`, a **string**). The single-invoice responses expose `SubmitTimeUtc (submitTimeUtc): string?` (the response timestamp), not a stored created date. Neither is a `DateTimeOffset` — parse defensively.

---

## 3. Client construction, base URL, environment, auth

Confirmed from SDK source (`ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`):

- **Base URL — set `Visa:BaseUrl` here, verbatim, so every call routes through it:**
  `options.Server.Default.Production.BaseUrl = <value bound from configuration key `Visa:BaseUrl`>`.
  Type path: `options.Server` (`CyberSourceMergedSpec.ServerOptions`) → `.Default` (`CyberSourceMergedSpec.Servers.DefaultOptions`) → `.Production` (nested `DefaultOptions.ProductionOptions`) → `.BaseUrl` (`string`). **Default when unset: `"https://apitest.cybersource.com/"`** (the sandbox host).
- **Environment:** `options.Environment = ServerEnvironment.Production` (`CyberSourceMergedSpec.Servers.ServerEnvironment`). ⚠ `Production` is the **only** member and is the default — and its default base URL is the **sandbox** host above. A call reaching the "wrong" environment is almost always the base-URL string, not the environment member. Because you are overriding `BaseUrl` from `Visa:BaseUrl` anyway, this is moot as long as the config value points at the test host.
- **Auth — there is NO credentials property (see the ⚠ box in REQUIRED READING).** `CyberSourceMergedSpecClientOptions` (source `CyberSourceMergedSpecClientOptions.cs`) exposes only `Environment`, `Retry`, `Logging`, `Server`, `Hooks`. The three env vars `VISA_MERCHANT_ID`, `VISA_KEY_ID`, `VISA_SECRET_KEY` drive the HTTP Signature `SdkHook`, which is read **once, inside the client constructor** — they must be set in the process environment **before** the client is constructed. Do not attempt to bind them to a client-options property; there isn't one.
- **Client:** `new CyberSourceMergedSpecClient(httpClient, options)` or DI `services.AddCyberSourceMergedSpecClient(o => { … })`. Controllers via `client.Invoices`.

Source cell: base-URL/env facts = SDK source `ServerOptions.cs` / `Servers/DefaultOptions.cs` / `Servers/ServerEnvironment.cs`; options-property list = `sdk-map.md` *client-options*.

---

## 4. Status transitions — how state is represented

- **There is no invoice-status enum in this SDK.** `enums.md` lists 12 enums; none is an invoice
  status. On **every** invoice response the state is a single field `Status (status): string?` — a
  **plain string**, not a typed enum (source: `records-5-In-Me.md`, `records-4-Fe-In.md`).
- **A single `Status` string field does carry the state** (draft vs delivered vs cancelled, etc.):
  yes, there is one status field per invoice, and the list items expose the same `Status` string.
  But **the literal string values are not documented anywhere in the map** — "DRAFT", "SENT",
  "PAID", "CANCELED", etc. cannot be confirmed from map or SDK source. See §5 Blocker B3 — treat
  every status value as `UNVERIFIED` and do not hard-code a value the SDK does not define.
- The **transition history** ("whatever the provider reports about how it got there") is
  `InvoicingV2InvoicesGet200Response.InvoiceHistory` — `IReadOnlyList<InvoiceHistory>`, each
  `Event (event): string?` + `Date (date): DateTimeOffset?`. `Event` is likewise a free string.
- **Draft vs delivered on create** is driven by `InvoiceInformation.SendImmediately (bool? = false)`:
  leave it false/unset to create a draft; the explicit **deliver** transition is `PerformSendAction`.
  `DeliveryMode (string?)` is a separate free-string field whose valid values are UNVERIFIED (§5 A3).

---

## 5. Assumptions & Blockers

**Blockers (planning is wrong until resolved):**

- **B1 — Date-range list filtering is NOT supported by the SDK.** `GetAllInvoices` takes only
  `offset` (int), `limit` (int), `status` (string?). There are **no from/to date-time query
  params** (source: `operations/Invoices.md`). The reconciliation "invoices created within a date
  range" requirement therefore **cannot be done server-side**. Options for the implementer to
  decide: (a) page through all invoices (`offset`/`limit` loop) and filter client-side on
  `Invoice1.CreatedDate` — but `CreatedDate` is a **string** of unverified format, so parse
  defensively; (b) narrow by `status` first to reduce volume. This is a capability gap, not a
  workaround I can invent around.

- **B2 — No way, via the LIST response, to reliably tell "our" invoices from pre-existing
  sandbox invoices by invoice number.** `Invoice1` (list item) exposes `Id`, `Status`,
  `CreatedDate`, `CustomerInformation2` (`Name`, `MerchantCustomerId`), `InvoiceInformation2`
  (`DueDate`, `ExpirationDate` — **no `InvoiceNumber`**), and amount. The only app-controllable
  identifier that survives into the list is `CustomerInformation.MerchantCustomerId`. See A4.

- **B3 — Invoice status string values are undocumented.** The state is a free `string`
  (`Status`), and neither the map nor the SDK source enumerates its values. Any code that branches
  on status (e.g. "is this a draft I may still update?") must treat the values as `UNVERIFIED` and
  cannot be validated at compile time. Confirm the actual values against live sandbox traffic /
  CyberSource docs before relying on them. Recommended defensive directive: branch on a small set
  of expected values, and treat unknown status strings as a non-fatal "unrecognised state" rather
  than assuming a transition is legal.

- **B4 — `PerformSendAction` (delivery) vs `PerformPublishAction` (publication) roles are not
  documented.** Both are POST action transitions on `{id}`; this SDK carries **no operation Notes**
  (no `<remarks>` on any operation), so the map cannot say which one "issues and yields the payment
  link". The map's only signal is `Links251`, whose HATEOAS members are `Self`,`Update`,`Deliver`,
  `Cancel` (no "publish") — so `PerformSendAction` (`/delivery`) is the mapped "deliver to
  customer" transition and is what this plan uses for capability 4. `PerformPublishAction`'s
  distinct effect is `UNVERIFIED`; confirm against live sandbox behaviour before wiring it.

**Assumptions (about intent — confirm if wrong):**

- **A1 — Draft on create:** capability 1 wants a non-delivered invoice, so build with
  `InvoiceInformation.SendImmediately = false` (the default) and do **not** call `PerformSendAction`
  until capability 4. `YOUR CALL — not in the map` whether you also set a specific `DeliveryMode`.
- **A2 — Amount on update:** the brief says amount is not correctable, but `UpdateInvoiceRequest`
  marks `OrderInformation (OrderInformation60) !req` — the update body still **requires** the order
  amount to be present. Re-send the same order amount you created with; changing only due date +
  customer means passing the unchanged `OrderInformation60`. Whether the provider actually accepts
  an amount change on update is `UNVERIFIED` (no operation Notes; §"How to ground" caveat).
- **A3 — `DeliveryMode` values** (string, e.g. email vs none) are `UNVERIFIED` — not in the map.
  `YOUR CALL — not in the map`, and confirm against CyberSource docs/live traffic.
- **A4 — Tagging "our" invoices:** set `CustomerInformation.MerchantCustomerId` to an
  app-scoped value (e.g. derived from the eShopOnWeb order/buyer id — `YOUR CALL — not in the map`,
  resolve from the app's own identity/order model) so reconciliation can filter list items on it.
  `InvoiceInformation.InvoiceNumber` can also be set on create but is **not returned by the list**
  operation (B2), so it only helps on per-id `GetInvoice`, not in bulk reconciliation.
- **A5 — Payment-link availability before delivery:** the `PaymentLink` field exists on all
  responses, but whether it is populated before `PerformSendAction` is `UNVERIFIED`. Directive:
  read `response.InvoiceInformation?.PaymentLink` best-effort and treat `null` as "not yet
  available; deliver first".
- **A6 — Required-ness beyond generated `required` flags is UNVERIFIED.** This SDK has no
  operation Notes, so no source marks which *optional* fields the provider actually insists on.
  This plan carries the fields the scope plainly needs (`InvoiceInformation`, `OrderInformation`,
  `CustomerInformation`); required-ness of anything else was **not** checked and cannot be from the
  map.

---

## 6. REQUIRED READING — load BEFORE implementation starts

These `dotnet-*` companion skills must be loaded before writing the corresponding code. This sheet
deliberately does **not** restate their contents — the trap notes below name the hazard and its
consequence only; the fix lives in the skill.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `CyberSourceMergedSpecClient`, `HttpClient` lifetime & DI. |
| `dotnet-authentication` | Step 1 — the HTTP Signature `SdkHook` and its three env vars (this SDK has **no** credentials property; see ⚠ below). |
| `dotnet-configuration-resilience` | Step 1 — base-URL/server selection, retries, timeouts, and manual pagination for `GetAllInvoices`. |
| `dotnet-calling-endpoints` | Steps 2–6 — call shape, named args for the list op's `offset`/`limit`/`status`, `ct`. |
| `dotnet-models` | Steps 2–6 — building the request records, `required` init members, `StringEnum<T>` handling, dropped-unmodeled-fields. |
| `dotnet-error-handling` | Step 7 — the try/catch boundary around every call (mandatory; see the two `JsonException` rows below). |
| `dotnet-testing` | Testing — faking the `HttpClient` seam for the integration layer. |

**Trap notes (name the hazard; the remedy is in the named skill):**

- ⚠ Step 1 (auth) — this SDK breaks the APIMatic pattern: there is **no credentials property** on
  the options object. Auth is an opt-in HTTP Signature `SdkHook` read from `VISA_MERCHANT_ID`,
  `VISA_KEY_ID`, `VISA_SECRET_KEY` **once, inside the client constructor**. If the switch/vars are
  unset, every request goes out **unsigned** and appears to work locally while failing signed calls.
  Naming the failure mode is not the fix. **MUST load `dotnet-authentication`** before wiring the client.
- ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and
  are **not** the timeout on the `HttpClient` you register; and whether a failed non-idempotent write
  (e.g. `CreateInvoice`, `PerformSendAction`) can be re-sent is governed by retry semantics you have
  not seen. **MUST load `dotnet-configuration-resilience`** before setting retries/timeouts/base URL.
- ⚠ Step 6 (list) — pagination is manual `offset`/`limit`; how to page safely and where the
  boundaries are is not visible in the signature. **MUST load `dotnet-configuration-resilience`.**
- ⚠ Steps 2–6 (models) — amounts and currency are `string`; `DateTimeOffset` `DueDate` is `required`;
  `StringEnum<T>` types are not C# enums; unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`.**
- ⚠ Step 7 (error boundary) — each operation is Case A typed (`SdkException<{Op}Error>`) with
  `TryGet…400/404/502Response1` accessors plus `TryGetRawError` fallback; there are **no** no-throw
  `…Result` variants. Reading status/body safely and ordering the catch ladder correctly is not
  something the signature shows. **MUST load `dotnet-error-handling`** before writing any try/catch.

**Two `JsonException` hazard rows — include verbatim; `System.Text.Json.JsonException` reaches the
boundary from two directions and they need opposite handling:**

- A drifted or malformed **2xx** body (a missing `required` member, or a field whose live type
  differs from the generated one) surfaces as a `JsonException` from deserialization, **not** as an
  `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.
