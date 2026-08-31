# Visa/CyberSource .NET SDK — Invoicing integration plan (eShopOnWeb)

SDK: CyberSource Merged Spec · NuGet `APIMatic.VisaCyberSource` (install version-less) ·
root namespace `CyberSourceMergedSpec` · client `CyberSourceMergedSpecClient` · all invoicing
operations hang off `client.Invoices`. Map release: tag `v2.0.1`, spec stamp `bbc9181`.

This plan is a contract sheet. It describes the Visa call surface only. Persistence of invoice
ids, when the app calls each operation, and the app's own request contract are the implementer's
design, not settled here.

---

## 1. Scope & sequence

Every capability maps to one `client.Invoices` operation. Order of implementation:

1. **Create draft invoice** — `client.Invoices.CreateInvoice(createInvoiceRequest, ...)`.
   Keep it DRAFT by leaving `invoiceInformation.sendImmediately` at its default `false`
   (do NOT set it `true`). Read back `id` + `status` from the response.
2. **Retrieve invoice** — `client.Invoices.GetInvoice(id, ...)`. Read `status`, `invoiceHistory`
   (provider-owned state trail), and the payment URL `invoiceInformation.paymentLink`.
3. **Update draft invoice** — `client.Invoices.UpdateInvoice(id, updateInvoiceRequest, ...)`.
   PUT is a full replace: `invoiceInformation` AND `orderInformation` are both `!req`, so the
   order amount must be re-sent even though it is not being corrected.
4. **Send / issue invoice** — `client.Invoices.PerformSendAction(id, ...)`
   (`POST .../delivery`). Delivers to the customer; response carries the now-live
   `invoiceInformation.paymentLink`.
5. **Cancel / withdraw invoice** — `client.Invoices.PerformCancelAction(id, ...)`
   (`POST .../cancelation`). Response carries the resulting `status`.
6. **List invoices for reconciliation** — `client.Invoices.GetAllInvoices(offset, limit, status, ...)`.
   ⚠ See Blocker B1: this operation has **no date-range filter**. Reconciliation must page by
   `offset`/`limit` and filter by each item's `createdDate` client-side.

Client + auth + base-URL wiring is a prerequisite for all six (section: Client construction).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C#
> identifier. The cancellation-token parameter really is named `ct`: in named arguments write
> `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it.** Namespaces
> in play here:
> - `CyberSourceMergedSpec` (root) — `CyberSourceMergedSpecClient`, `CyberSourceMergedSpecClientOptions`, `ServerOptions`
> - `CyberSourceMergedSpec.Servers` — `DefaultOptions` (`.Production.BaseUrl`), `ServerEnvironment`
> - `CyberSourceMergedSpec.Models` — every request/response record below and the `*Response1` error payloads
> - `CyberSourceMergedSpec.Errors` — `CreateInvoiceError`, `GetInvoiceError`, `UpdateInvoiceError`, `PerformSendActionError`, `PerformCancelActionError`, `GetAllInvoicesError`
> - `CyberSourceMergedSpec.Core.Exceptions` — `SdkException<T>`
> - `CyberSourceMergedSpec.Core.ErrorResponse` — `RawError`
> - `CyberSourceMergedSpec.Core.Configuration` — `RetryOptions`
>
> Two types configured side by side in the same options object routinely live in different
> child namespaces (`ServerOptions` at the root, `DefaultOptions`/`ServerEnvironment` under
> `.Servers`). Take each type's namespace from its own row, never from a neighbour.

### Operations

| # | Op (`client.Invoices.`) | Signature (params in order) | Request model | Response (envelope) | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 | `CreateInvoice` | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateInvoiceRequest` | `InvoicingV2InvoicesPost201Response` | Case A `SdkException<CreateInvoiceError>`; `TryGetInvoicingV2InvoicesPost400Response1(out …)` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError(out RawError)` [fallback] | none | operations/Invoices.md |
| 2 | `GetInvoice` | `GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesGet200Response` | Case A `SdkException<GetInvoiceError>`; `TryGetInvoicingV2InvoicesGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | operations/Invoices.md |
| 3 | `UpdateInvoice` | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `UpdateInvoiceRequest` | `InvoicingV2InvoicesPut200Response` | Case A `SdkException<UpdateInvoiceError>`; `TryGetInvoicingV2InvoicesPut400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | operations/Invoices.md |
| 4 | `PerformSendAction` | `PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesSend200Response` | Case A `SdkException<PerformSendActionError>`; `TryGetInvoicingV2InvoicesSend400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | operations/Invoices.md |
| 5 | `PerformCancelAction` | `PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (path `id`) | `InvoicingV2InvoicesCancel200Response` | Case A `SdkException<PerformCancelActionError>`; `TryGetInvoicingV2InvoicesCancel400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none | operations/Invoices.md |
| 6 | `GetAllInvoices` | `GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (query `offset`,`limit`,`status`) | `InvoicingV2InvoicesAllGet200Response` | Case A `SdkException<GetAllInvoicesError>`; `TryGetInvoicingV2InvoicesAllGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` [fallback] | none built-in; page manually via `offset`/`limit` | operations/Invoices.md |

Notes on op 6 params: `offset` and `limit` are non-nullable `int` (pass values). `status` is
`string?` with **no default → must be passed explicitly** (pass `null` to not filter by status).
Wire mapping: `offset←offset`, `limit←limit`, `status←status`.

Notes on op 4 vs sibling: `PerformSendAction` (`POST .../delivery`) is the "deliver to customer"
action selected here. A sibling `PerformPublishAction` (`POST .../publication`,
returns `InvoicingV2InvoicesPublish200Response`) also exists. The map carries **no operation
remarks** for either (see §Trap on required-ness), so the exact publish-vs-deliver semantic
boundary is `UNVERIFIED`; `/delivery` matches the brief's "deliver it to the customer" wording.

### Request models (field · wire · type · required)

**`CreateInvoiceRequest`** (`Models/CreateInvoiceRequest.cs`, records-3-Cr-Ex.md):
| Field (wire) | Type | Req? |
|---|---|---|
| `ClientReferenceInformation (clientReferenceInformation)` | `ClientReferenceInformation78?` | no |
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | no |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72?` | no |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation` | **!req** |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **!req** |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>?` | no |

**`InvoiceInformation`** (create; `Models/InvoiceInformation.cs`, records-5-In-Me.md):
| Field (wire) | Type | Req? |
|---|---|---|
| `InvoiceNumber (invoiceNumber)` | `string?` | no — merchant-settable invoice number |
| `Description (description)` | `string` | **!req** |
| `DueDate (dueDate)` | `DateTimeOffset` | **!req** — the calendar due date |
| `ExpirationDate (expirationDate)` | `DateTimeOffset?` | no |
| `SendImmediately (sendImmediately)` | `bool? = false` | no — **leave false/unset to keep DRAFT** |
| `AllowPartialPayments (allowPartialPayments)` | `bool? = false` | no |
| `DeliveryMode (deliveryMode)` | `string?` | no |

**`OrderInformation60`** (`Models/OrderInformation60.cs`, records-6-Me-Pa.md):
| Field (wire) | Type | Req? |
|---|---|---|
| `AmountDetails (amountDetails)` | `AmountDetails60` | **!req** |
| `LineItems (lineItems)` | `IReadOnlyList<LineItem17>?` | no |

**`AmountDetails60`** (`Models/AmountDetails60.cs`, records-1-Ac-Bi.md):
| Field (wire) | Type | Req? |
|---|---|---|
| `TotalAmount (totalAmount)` | `string` | **!req** — amount is a **string**, format the order total |
| `Currency (currency)` | `string` | **!req** — set `"USD"` |
| `DiscountAmount`/`DiscountPercent`/`SubAmount`/`MinimumPartialAmount` | `string?` | no |
| `TaxDetails (taxDetails)` | `TaxDetails13?` | no |
| `Freight (freight)` | `Freight?` | no |

**`LineItem17`** (`Models/LineItem17.cs`, records-5-In-Me.md):
| Field (wire) | Type | Req? |
|---|---|---|
| `ProductSku (productSku)` | `string?` | no |
| `ProductName (productName)` | `string?` | no — line item name |
| `Quantity (quantity)` | `int? = 1` | no |
| `UnitPrice (unitPrice)` | `string?` | no — **string**, format the unit price |
| `DiscountAmount`/`DiscountPercent`/`TaxAmount`/`TaxRate`/`TotalAmount` | `string?` | no |

**`CustomerInformation`** (`Models/CustomerInformation.cs`, records-3-Cr-Ex.md):
| Field (wire) | Type | Req? |
|---|---|---|
| `Name (name)` | `string?` | no |
| `Email (email)` | `string?` | no |
| `MerchantCustomerId (merchantCustomerId)` | `string?` | no |
| `Company (company)` | `Company6?` | no |

**`ClientReferenceInformation78`** (`Models/ClientReferenceInformation78.cs`, records-2-Bi-Cr.md):
only `Partner (partner): Partner38?`. **There is no code/reference-number field here** — you
cannot stamp the eShopOnWeb order id through `clientReferenceInformation`. Use
`invoiceInformation.invoiceNumber` (on create) to carry an order reference, and/or reconcile by
the returned invoice `id` (see reconciliation note below).

**`UpdateInvoiceRequest`** (`Models/UpdateInvoiceRequest.cs`, records-11-To-We.md):
| Field (wire) | Type | Req? |
|---|---|---|
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | no — updatable customer details |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72?` | no |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation4` | **!req** |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **!req** — must re-send the amount |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>?` | no |

**`InvoiceInformation4`** (update; `Models/InvoiceInformation4.cs`, records-5-In-Me.md): same as
`InvoiceInformation` **minus `invoiceNumber`** — `Description (description): string !req`,
`DueDate (dueDate): DateTimeOffset !req`, `ExpirationDate: DateTimeOffset?`,
`SendImmediately: bool? = false`, `AllowPartialPayments: bool? = false`, `DeliveryMode: string?`.
So the update changes the **due date** via `dueDate` and the **customer details** via the
top-level `customerInformation`; both `invoiceInformation` and `orderInformation` are required
on the wire regardless.

### Response models (fields the integration reads)

**Create/Get/Update/Send/Cancel** all share this envelope shape (fields vary slightly):
`Links (_links): Links251?` · `Id (id): string?` · `SubmitTimeUtc (submitTimeUtc): string?` ·
`Status (status): string?` · `CustomerInformation: CustomerInformation?` ·
`ProcessingInformation: ProcessingInformation72?` · `InvoiceInformation: InvoiceInformation1?` ·
`OrderInformation: OrderInformation61?`. Source rows all in records-5-In-Me.md.

Read targets:
- **Invoice id** → top-level `Id (id)`.
- **Status** → top-level `Status (status)` — a provider-owned **`string?`**, not an SDK enum (see §Enums).
- **Payment link / URL** → `InvoiceInformation.PaymentLink (paymentLink): string?` on
  `InvoiceInformation1`. (`Links251` = `Self`/`Update`/`Deliver`/`Cancel` HATEOAS action links,
  NOT the customer payment URL — do not read the pay link from `_links`.)
- **State trail (Get only)** → `InvoicingV2InvoicesGet200Response.InvoiceHistory (invoiceHistory):
  IReadOnlyList<InvoiceHistory>?` (Post201/Put200 do not carry it).

**`InvoiceInformation1`** (`Models/InvoiceInformation1.cs`, records-5-In-Me.md):
`InvoiceNumber: string?` · `Description: string?` · `DueDate: DateTimeOffset?` ·
`ExpirationDate: DateTimeOffset?` · `AllowPartialPayments: bool? = false` ·
**`PaymentLink (paymentLink): string?`** · `DeliveryMode: string?` ·
`CustomLabels: IReadOnlyList<CustomLabel>?`.

**`InvoicingV2InvoicesAllGet200Response`** (list; records-5-In-Me.md):
`Links (_links): Links251?` · `SubmitTimeUtc (submitTimeUtc): string?` ·
`TotalInvoices (totalInvoices): int?` · `Invoices (invoices): IReadOnlyList<Invoice1>?`.

**`Invoice1`** (list item; `Models/Invoice1.cs`, records-4-Fe-In.md):
`Links (_links): Links251?` · **`Id (id): string?`** · **`Status (status): string?`** ·
**`CreatedDate (createdDate): string?`** · `CustomerInformation: CustomerInformation2?` ·
`InvoiceInformation: InvoiceInformation2?` · `OrderInformation: OrderInformation62?`.
For reconciliation, the three bolded fields are what §6 needs. Note `Invoice1` exposes neither
`invoiceNumber` (its `InvoiceInformation2` carries only `dueDate`/`expirationDate`) nor a client
reference, so **line up list items against our records by the stored invoice `id`**, not by any
merchant reference.

### Typed error payloads (all Invoicing `*Response1` shapes)

Every `TryGet…400/404Response1` yields a record with
`SubmitTimeUtc: string?` · `Status (status): string?` · `Reason (reason): string?` ·
`Message (message): string?` · `Details (details): IReadOnlyList<Detail>?`. The `502Response1`
shapes are the same minus `Details`. Surface `Reason`/`Message` to the user; the HTTP status is
on the accessor that matched (400 vs 404 vs 502) or via `TryGetRawError(out RawError).StatusCode`.
Source: records-5-In-Me.md.

### Enums

**None are used by this integration.** `status`, `currency`, and `deliveryMode` are all plain
`string` on the wire. `currency` is set to the literal `"USD"`. The set of `status` string
literals the provider returns (e.g. draft/sent/paid/canceled wording) is **not modelled in the
SDK** and is `UNVERIFIED` — compare defensively, do not hard-code an assumed spelling.

### Client construction, auth, base URL

**Construction** — `new CyberSourceMergedSpecClient(httpClient, options)` where `options` is a
`CyberSourceMergedSpecClientOptions` (root namespace). DI alternative:
`services.AddCyberSourceMergedSpecClient(o => { … })`. HttpClient lifetime/factory concerns →
see Trap T1.

**Base URL** — bind config key `Visa:BaseUrl` and set it verbatim as the base address:
`options.Server.Default.Production.BaseUrl = <Visa:BaseUrl value>`
(`ServerOptions.Default` → `DefaultOptions.Production` → `ProductionOptions.BaseUrl`). The only
`ServerEnvironment` member is `ServerEnvironment.Production`, and its **default `BaseUrl` is the
sandbox host `https://apitest.cybersource.com/`** — so an unset `Visa:BaseUrl` silently points at
apitest, not an error. Bind the key explicitly for every environment.
Source: SDK source `Servers/ServerOptions.cs`, `Servers/DefaultOptions.cs`. Wiring semantics → Trap T2.

**Authentication** — there is **no credentials property** on the options. Auth is an opt-in HTTP
Signature `SdkHook` appended inside the client constructor, only when
`VisaHttpSignatureConfigResolver.Resolve()` returns non-null. That resolver reads **four**
environment variables, once, at construction — see Blocker B2 (the brief listed only three) and
Trap T3. Source: SDK source `CyberSourceMergedSpecClient.cs`,
`Core/Experimental/VisaHttpSignature/VisaHttpSignatureConfigResolver.cs`.

---

## 3. Trap notes (name the hazard; load the skill before coding that step)

⚠ **T1 — Client & DI setup.** The SDK client wraps an `HttpClient` whose handler pipeline must be
long-lived and reused, not rebuilt per request; getting the lifetime wrong is not visible in the
constructor signature. **MUST load `dotnet-client-initialization`** before writing
`new CyberSourceMergedSpecClient(...)` or `AddCyberSourceMergedSpecClient`.

⚠ **T2 — Base URL / server & resilience.** How `options.Server`/`Environment` interact, whether
`Visa:BaseUrl` fully overrides the template, what `RetryOptions.Timeout` actually bounds, and
which calls retry are not revealed by the option names — and a transport failure can re-send a
non-idempotent write (create/send/cancel). **MUST load `dotnet-configuration-resilience`** before
wiring the client, the base URL, retries, or paging op 6.

⚠ **T3 — Authentication.** With no credentials property, whether a request is signed at all hinges
on the env-var switch being read before construction; leaving it unset does not disable auth, it
sends every request **unsigned** while appearing to work. **MUST load `dotnet-authentication`**
before the first call. (The missing fourth env var is Blocker B2.)

⚠ **T4 — Building request models.** Amounts/prices are **strings** not decimals, `status` is a
plain string, enums elsewhere in this SDK are `StringEnum<T>` not C# enums, and JSON fields the
SDK does not model are dropped on deserialize. **MUST load `dotnet-models`** before constructing
`CreateInvoiceRequest`/`UpdateInvoiceRequest` or mapping responses onto domain types.

⚠ **T5 — Error boundary.** All six ops are Case A typed errors with per-status `TryGet…`
accessors and a `TryGetRawError` fallback; there is **no no-throw `…Result` variant**. How to
read status/body safely, and the two `JsonException` directions below, are not visible in a
signature. **MUST load `dotnet-error-handling`** before writing any try/catch. (See REQUIRED
READING for the two mandatory `JsonException` hazards.)

⚠ **T6 — Required-ness beyond the generated flags is unverified.** This SDK's operation rows
carry **no remarks/Notes**, so which optional fields the provider actually insists on for an
invoice to be accepted (rather than merely well-formed) is not documented anywhere and no compiler
catches it. The `!req` flags above are the *only* checked signal. Carry the fields the scope
plainly needs (description, dueDate, amount, currency, customer name/email); treat any
provider-imposed requirement beyond the `!req` flags as `UNVERIFIED` and code the error boundary
to surface `Reason`/`Message` rather than assuming success.

⚠ **T7 — Update-when-not-draft.** The brief expects the provider to refuse an update to an
already-sent/cancelled invoice. That surfaces as `SdkException<UpdateInvoiceError>`; the exact
status is `UNVERIFIED` (likely 400 — read via `TryGetInvoicingV2InvoicesPut400Response1`, else
404 via `…404Response1`, else `TryGetRawError`). Extract `Reason`/`Message` best-effort and fall
back to the generic error message. Same defensive pattern applies to cancel/send on a
non-updatable state.

---

## 4. REQUIRED READING (load BEFORE implementation; this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Client construction, HttpClient lifetime, DI registration (T1) |
| `dotnet-authentication` | HTTP Signature hook + env vars — this SDK has NO credentials property (T3, B2) |
| `dotnet-configuration-resilience` | Base URL / server selection, retries/timeouts, paging op 6 (T2) |
| `dotnet-calling-endpoints` | Calling ops; pass op 6's optional `status` as a named arg (`status:`) |
| `dotnet-models` | String amounts, `StringEnum<T>`, dropped-field behaviour (T4) |
| `dotnet-error-handling` | The Case-A error boundary for all six ops (T5, T7) |
| `dotnet-testing` | Faking the HttpClient seam when testing the integration layer |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary** — it reaches the
boundary from two directions needing opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member, or a field whose live type
  differs from the generated one) surfaces as a `JsonException` from deserialization, **not** as an
  `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps
  every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller
  that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- A1: "Send / issue" (capability 4) = `PerformSendAction` (`POST .../delivery`), the deliver-to-
  customer action. `PerformPublishAction` (`.../publication`) is treated as a separate action not
  in scope. The publish-vs-deliver boundary is UNVERIFIED (no operation remarks in the map).
- A2: DRAFT-on-create is achieved by leaving `sendImmediately` at its `false` default; this is
  inferred from the field's presence/default, not from documented operation behaviour (UNVERIFIED).
- A3: Reconciliation (capability 6) lines list items up against eShopOnWeb records by the stored
  provider invoice `id`, because `Invoice1` exposes no invoice number or client reference.
- A4: eShopOnWeb persistence of the returned invoice `id`/`status`, and the app-side request
  contract for the invoicing endpoints, are the implementer's design (YOUR CALL — not in the map).

**Blockers**
- **B1 — No server-side date-range filter on the list operation.** Capability 6 asks for invoices
  "created between two date-times (from/to, ISO-8601)". `GetAllInvoices` exposes **only**
  `offset`, `limit`, `status` — there is no `from`/`to`/date parameter in the map. The date-range
  report is therefore not a single provider call: the integration must page through `offset`/`limit`
  and filter `Invoice1.CreatedDate` client-side (and remember the account carries invoices from
  other activity too). Confirm this approach is acceptable, or the "server-side date range"
  requirement cannot be met with this SDK. (Source: operations/Invoices.md — `GetAllInvoices` row.)
- **B2 — Authentication needs a FOURTH env var the brief omitted.** The signature hook is only
  installed when `VisaHttpSignatureConfigResolver.Resolve()` returns non-null, and that resolver
  first checks a **master switch** env var `APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE` — it must
  equal exactly the string `"true"`. If it is unset or any other value, `Resolve()` returns null,
  **no hook is added, and every request is sent unsigned** (the three credential vars
  `VISA_MERCHANT_ID` / `VISA_KEY_ID` / `VISA_SECRET_KEY` are read only after the switch passes).
  All four must be present in the process environment **before** the client is constructed. Deploy
  config must set `APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE=true` alongside the three credentials;
  wiring detail is in `dotnet-authentication`. (Source: SDK source
  `Core/Experimental/VisaHttpSignature/VisaHttpSignatureConfigResolver.cs`.)

---

## 6. Follow-up — `InvoiceHistory` field table + read-back name confirmations

**`InvoiceHistory`** (`Models/InvoiceHistory.cs`, records-5-In-Me.md) — all fields, all optional:
| C# property (wire) | Type | Req? |
|---|---|---|
| `Event (event)` | `string?` | optional |
| `Date (date)` | `DateTimeOffset?` | optional |
| `TransactionDetails (transactionDetails)` | `TransactionDetails?` | optional |

Nested **`TransactionDetails`** (`Models/TransactionDetails.cs`, records-11-To-We.md) — "only
returned when the invoice event is `payment`", all optional:
| C# property (wire) | Type | Req? |
|---|---|---|
| `TransactionId (transactionId)` | `string?` | optional |
| `Amount (amount)` | `string?` | optional — a **string** amount |

So map to your DTO: `Event`, `Date`, and (when present) `TransactionDetails.TransactionId` +
`TransactionDetails.Amount`. Every field is nullable — code the mapper defensively.

**Read-back name confirmations:**
2. `InvoicingV2InvoicesGet200Response` — **YES** to all: `Id (id): string?`,
   `Status (status): string?`, `InvoiceInformation (invoiceInformation): InvoiceInformation1?`,
   `InvoiceHistory (invoiceHistory): IReadOnlyList<InvoiceHistory>?`. (records-5-In-Me.md)
3. `InvoiceInformation1.PaymentLink (paymentLink): string?` — **YES**, correct. (records-5-In-Me.md)
4. `InvoicingV2InvoicesAllGet200Response` — **YES**: `Invoices (invoices): IReadOnlyList<Invoice1>?`,
   `TotalInvoices (totalInvoices): int?`. `Invoice1` — **YES**: `Id (id): string?`,
   `Status (status): string?`, `CreatedDate (createdDate): string?`. (records-5-In-Me.md;
   `Invoice1` in records-4-Fe-In.md)
