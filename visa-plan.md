# Visa/CyberSource invoicing integration — plan & contract sheet

SDK: `CyberSourceMergedSpec` (NuGet `APIMatic.VisaCyberSource`) · map release tag `v2.0.1`.
Target: customer invoicing on eShopOnWeb `src/PublicApi`, CyberSource TEST/sandbox. All Visa
traffic goes through `client.Invoices` (the `Invoices` controller).

---

## 1. Scope & sequence

| # | Capability | Operation(s) | Notes |
|---|---|---|---|
| 1 | Raise a bill (create draft invoice) | `client.Invoices.CreateInvoice(...)` | Keep `sendImmediately=false` (default) so it stays a draft/not-sent. |
| 2 | Get an invoice by id (status, history, payment link) | `client.Invoices.GetInvoice(id, ...)` | Payment link = `InvoiceInformation.PaymentLink`; history = `InvoiceHistory[]`. |
| 3 | Update/correct a draft (due date + customer) | `client.Invoices.UpdateInvoice(id, body, ...)` | Amount still `!req` in the body — see §Blockers. |
| 4 | Send (deliver to customer) | `client.Invoices.PerformSendAction(id, ...)` | POST `/delivery`. Makes the payment link available. |
| 5 | Cancel/withdraw | `client.Invoices.PerformCancelAction(id, ...)` | POST `/cancelation`. |
| 6 | List/reconcile over a date range | `client.Invoices.GetAllInvoices(offset, limit, status, ...)` | **No date-range param exists — see BLOCKER B1.** Page with offset/limit, filter by `CreatedDate` client-side. |

A 7th op exists, `PerformPublishAction` (POST `/publication`) → `InvoicingV2InvoicesPublish200Response`.
The map documents no `<remarks>` for it, so its semantics vs. `PerformSendAction` are undocumented;
capability 4 ("deliver/issue to the customer") maps to `PerformSendAction` (`/delivery`), not this one.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it.** For this
> integration: client/options → `CyberSourceMergedSpec`; controllers → `CyberSourceMergedSpec.Api`;
> all request/response/nested records → `CyberSourceMergedSpec.Models`; typed error classes
> (`CreateInvoiceError`, …) → `CyberSourceMergedSpec.Errors`; `ServerEnvironment` →
> `CyberSourceMergedSpec.Servers`. `SdkException<T>` is at `Core/Exceptions/` (path-implies
> `CyberSourceMergedSpec.Core.Exceptions`) and `RawError` at `Core/ErrorResponse/`
> (path-implies `CyberSourceMergedSpec.Core.ErrorResponse`) — confirm these two `using`s against
> `dotnet-error-handling` / the compiler, they are the only path-inferred namespaces here.

### 2.1 Operations

| Op | Method signature (params in order, verbatim) | Returns | Error case + accessors | Source |
|---|---|---|---|---|
| CreateInvoice | `CreateInvoice(CreateInvoiceRequest createInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `InvoicingV2InvoicesPost201Response` | Case A `SdkException<CreateInvoiceError>`; `TryGetInvoicingV2InvoicesPost400Response1(out …)` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError(out RawError)` [fallback] | operations/Invoices.md |
| GetInvoice | `GetInvoice(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `InvoicingV2InvoicesGet200Response` | Case A `SdkException<GetInvoiceError>`; `TryGetInvoicingV2InvoicesGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| UpdateInvoice | `UpdateInvoice(string id, UpdateInvoiceRequest updateInvoiceRequest, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `InvoicingV2InvoicesPut200Response` | Case A `SdkException<UpdateInvoiceError>`; `TryGetInvoicingV2InvoicesPut400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| PerformSendAction | `PerformSendAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `InvoicingV2InvoicesSend200Response` | Case A `SdkException<PerformSendActionError>`; `TryGetInvoicingV2InvoicesSend400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| PerformCancelAction | `PerformCancelAction(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `InvoicingV2InvoicesCancel200Response` | Case A `SdkException<PerformCancelActionError>`; `TryGetInvoicingV2InvoicesCancel400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | operations/Invoices.md |
| GetAllInvoices | `GetAllInvoices(int offset, int limit, string? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `InvoicingV2InvoicesAllGet200Response` | Case A `SdkException<GetAllInvoicesError>`; `TryGetInvoicingV2InvoicesAllGet400Response1` [400] · `…404Response1` [404] · `…502Response1` [502] · `TryGetRawError` | operations/Invoices.md |

- `status` on GetAllInvoices is nullable **with no default → must pass explicitly** (pass `null` for no filter). Call with named args (`ct:`). Query wire names: `offset`←offset, `limit`←limit, `status`←status.
- **No operation has a no-throw `…Result` variant** — every call is throw-only.
- **Pagination: "none" on every row** — there is no auto-pager. Page GetAllInvoices manually with `offset`/`limit`, using `TotalInvoices` (below) to know when the window is exhausted.
- These operation rows carry **no Notes/`<remarks>`** in the SDK, so required-beyond-`!req` fields and result-status semantics are NOT documented here — see the UNVERIFIED rows below.

### 2.2 Request models (fields as `CSharpName (wire_name): Type` — `!req` = generated required)

`CreateInvoiceRequest` (`Models/CreateInvoiceRequest.cs`, records-3-Cr-Ex.md):
| Field | Type | Req |
|---|---|---|
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation` | **!req** |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **!req** |
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | optional |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72?` | optional |
| `ClientReferenceInformation (clientReferenceInformation)` | `ClientReferenceInformation78?` | optional (only holds `Partner38` — not useful as a "mine" tag) |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>?` | optional |

`InvoiceInformation` (create; `Models/InvoiceInformation.cs`, records-5-In-Me.md):
| Field | Type | Req |
|---|---|---|
| `InvoiceNumber (invoiceNumber)` | `string?` | optional — **set this as your own key** (see reconciliation) |
| `Description (description)` | `string` | **!req** (task input list omitted it — you must supply one) |
| `DueDate (dueDate)` | `DateTimeOffset` | **!req** (task's "calendar date" — type is a full `DateTimeOffset`; date-only wire shape is UNVERIFIED) |
| `ExpirationDate (expirationDate)` | `DateTimeOffset?` | optional |
| `SendImmediately (sendImmediately)` | `bool? = false` | optional — **leave false/unset to keep the invoice a draft** |
| `AllowPartialPayments (allowPartialPayments)` | `bool? = false` | optional |
| `DeliveryMode (deliveryMode)` | `string?` | optional |

`OrderInformation60` (`Models/OrderInformation60.cs`, records-6-Me-Pa.md):
| Field | Type | Req |
|---|---|---|
| `AmountDetails (amountDetails)` | `AmountDetails60` | **!req** |
| `LineItems (lineItems)` | `IReadOnlyList<LineItem17>?` | optional |

`AmountDetails60` (`Models/AmountDetails60.cs`, records-1-Ac-Bi.md):
| Field | Type | Req |
|---|---|---|
| `TotalAmount (totalAmount)` | `string` | **!req** (amount is a string, not a number) |
| `Currency (currency)` | `string` | **!req** — set `"USD"` (no currency enum exists; plain string) |
| others: `DiscountAmount`, `DiscountPercent`, `SubAmount`, `MinimumPartialAmount`, `TaxDetails (TaxDetails13?)`, `Freight (Freight?)` | — | optional |

`LineItem17` (`Models/LineItem17.cs`, records-5-In-Me.md): `ProductSku (productSku): string?`, `ProductName (productName): string?`, `Quantity (quantity): int? = 1`, `UnitPrice (unitPrice): string?`, `DiscountAmount: string?`, `DiscountPercent: string?`, `TaxAmount: string?`, `TaxRate: string?`, `TotalAmount (totalAmount): string?`. (All amounts are strings; quantity is int.)

`CustomerInformation` (`Models/CustomerInformation.cs`, records-3-Cr-Ex.md): `Name (name): string?`, `Email (email): string?`, `MerchantCustomerId (merchantCustomerId): string?`, `Company (company): Company6?`. **Nothing is `!req`** — but delivery to the customer almost certainly needs `Name`+`Email` (provider rule, UNVERIFIED — no op Notes). `MerchantCustomerId` is the field that DOES come back in the list record (§2.4) — set it to your eShop customer/order id to identify your invoices.

`MerchantDefinedFieldValue` (`Models/MerchantDefinedFieldValue.cs`, records-5-In-Me.md): `DefinitionId (definitionId): long?`, `Value (value): string?`. (Not returned by the list op — see reconciliation note.)

`UpdateInvoiceRequest` (`Models/UpdateInvoiceRequest.cs`, records-11-To-We.md):
| Field | Type | Req |
|---|---|---|
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation4` | **!req** |
| `OrderInformation (orderInformation)` | `OrderInformation60` | **!req** |
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | optional |
| `ProcessingInformation (processingInformation)` | `ProcessingInformation72?` | optional |
| `MerchantDefinedFieldValues (merchantDefinedFieldValues)` | `IReadOnlyList<MerchantDefinedFieldValue>?` | optional |

`InvoiceInformation4` (update; `Models/InvoiceInformation4.cs`, records-5-In-Me.md): `Description (description): string !req`, `DueDate (dueDate): DateTimeOffset !req`, `ExpirationDate: DateTimeOffset?`, `SendImmediately: bool? = false`, `AllowPartialPayments: bool? = false`, `DeliveryMode: string?`. **Note there is no `InvoiceNumber` field on the update model** — the number is not editable via update.

> **Update caveat (BLOCKER B2):** `UpdateInvoiceRequest.OrderInformation (OrderInformation60)` and its `AmountDetails60.TotalAmount`/`Currency` are `!req`, so the SDK forces you to resend the amount on every update even though capability 3 says amount is not corrected here. Resend the current order amount unchanged. Whether the provider actually revalidates/allows an amount change on a draft is UNVERIFIED (no op Notes).

### 2.3 Response envelopes — these are FLAT records (no single-field wrapper); read fields directly.

`InvoicingV2InvoicesPost201Response` (CreateInvoice) / `…Get200Response` (GetInvoice) / `…Put200Response` (Update) / `…Send200Response` / `…Cancel200Response` / `…Publish200Response` share this top-level shape (records-5-In-Me.md):
| Field read | Type | Carries |
|---|---|---|
| `Id (id)` | `string?` | **provider invoice id** |
| `Status (status)` | `string?` | **invoice status** (plain string, no enum — see UNVERIFIED status note) |
| `SubmitTimeUtc (submitTimeUtc)` | `string?` | server submit time |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation1?` | invoice number + payment link (below) |
| `OrderInformation (orderInformation)` | `OrderInformation61?` | amounts (`AmountDetails61`) |
| `CustomerInformation (customerInformation)` | `CustomerInformation?` | name/email/merchantCustomerId |
| `Links (_links)` | `Links251?` | HATEOAS API links (`Self/Update/Deliver/Cancel`, each an href+method) — **not** the customer payment URL |

Extra on **GetInvoice** `InvoicingV2InvoicesGet200Response` only: `MerchantDefinedFieldValuesWithDefinition (…): IReadOnlyList<MerchantDefinedFieldValuesWithDefinition>?` and **`InvoiceHistory (invoiceHistory): IReadOnlyList<InvoiceHistory>?`** — the status/state history (capability 2). `InvoiceHistory` = `Event (event): string?`, `Date (date): DateTimeOffset?`, `TransactionDetails (transactionDetails): TransactionDetails?`. (CreateInvoice's 201 response carries `MerchantDefinedFieldValuesWithDefinition` but **not** `InvoiceHistory`.)

`InvoiceInformation1` (`Models/InvoiceInformation1.cs`, records-5-In-Me.md) — the response invoice block:
| Field | Type | Carries |
|---|---|---|
| `InvoiceNumber (invoiceNumber)` | `string?` | **invoice number** |
| `PaymentLink (paymentLink)` | `string?` | **the customer-facing / hosted payment URL** (capability 2 answer) |
| `DueDate (dueDate)` | `DateTimeOffset?` | due date |
| `ExpirationDate`, `AllowPartialPayments`, `Description`, `DeliveryMode`, `CustomLabels` | — | other |

- **Payment link accessor path:** `response.InvoiceInformation?.PaymentLink`. It is `string?` and (per capability 4/5) is only populated once the invoice is sent and null again after cancel — *when* exactly it is set/cleared is provider behaviour the map does not state → **UNVERIFIED**, code defensively (treat null as "not payable yet / no longer payable").
- Amounts on responses: `response.OrderInformation?.AmountDetails` is `AmountDetails61?` = `TotalAmount (totalAmount): string?`, `Currency (currency): string?`, plus `BalanceAmount (balanceAmount): string?` and the same optional breakdown fields as create.

### 2.4 List / reconcile — `InvoicingV2InvoicesAllGet200Response` (records-5-In-Me.md)

| Field | Type | Carries |
|---|---|---|
| `TotalInvoices (totalInvoices)` | `int?` | total in the (unfiltered-by-date) result — drives paging termination |
| `Invoices (invoices)` | `IReadOnlyList<Invoice1>?` | the page of records |
| `SubmitTimeUtc (submitTimeUtc)` | `string?` | — |
| `Links (_links)` | `Links251?` | HATEOAS links |

`Invoice1` (`Models/Invoice1.cs`, records-4-Fe-In.md) — each listed record:
| Field | Type | Carries |
|---|---|---|
| `Id (id)` | `string?` | provider invoice id |
| `Status (status)` | `string?` | status string |
| `CreatedDate (createdDate)` | `string?` | **created date** (string; filter your date window on THIS, client-side — see B1) |
| `CustomerInformation (customerInformation)` | `CustomerInformation2?` | `Name (name): string?`, `MerchantCustomerId (merchantCustomerId): string?` — **no email** |
| `InvoiceInformation (invoiceInformation)` | `InvoiceInformation2?` | **only `DueDate` + `ExpirationDate` — NO invoiceNumber** |
| `OrderInformation (orderInformation)` | `OrderInformation62?` | `AmountDetails (AmountDetails62?)` = `TotalAmount: string?`, `Currency: string?` |

> **Reconciliation — distinguishing YOUR invoices from foreign ones in the list (capability 6):**
> The list record `Invoice1` does **not** carry the invoice number (`InvoiceInformation2` has only
> due/expiration dates) and does **not** carry merchant-defined fields. The only "mine"-identifying
> field returned in the list is **`Invoice1.CustomerInformation.MerchantCustomerId`**. So: set
> `CustomerInformation.MerchantCustomerId` on CreateInvoice to a value you own (e.g. your eShop
> customer or order id), then filter the list by it. If you must reconcile by *invoice number*, the
> list won't give it to you — you'd need a `GetInvoice(id)` per row (N+1) to read
> `InvoiceInformation1.InvoiceNumber`. Prefer `MerchantCustomerId`. (records-4/5 as cited above.)

### 2.5 Status & currency are strings, not enums

`enums.md` has 12 enums; **none** is an invoice status or a currency. Both `Status` and `Currency`
are plain strings on the wire. Consequences:
- The **"draft" / "sent" / "canceled" state names are NOT in the map** (no enum, no op Notes). Capability 1's "draft" state, capability 4's post-send status, and capability 5's post-cancel status are provider wire strings only. Do **not** hardcode a status literal as if the map confirmed it → **UNVERIFIED**; extract `Status` best-effort and treat unknown values as pass-through. Control draft-ness at creation via `SendImmediately=false`, not by reading back a specific status string.
- Currency: set `"USD"` as a literal string; no enum validates it.

### 2.6 Client, auth, server config (facts)

- Client: `new CyberSourceMergedSpecClient(HttpClient httpClient, CyberSourceMergedSpecClientOptions options)`, or DI `services.AddCyberSourceMergedSpecClient(o => { … })`. Options members: `Environment (ServerEnvironment)`, `Retry (RetryOptions)`, `Logging`, `Server (ServerOptions)`, `Hooks (IReadOnlyList<SdkHook>)`. (sdk-map.md)
- **There is NO credentials property on `CyberSourceMergedSpecClientOptions`** (sdk-map.md *Servers & auth*). See trap T1.
- Environments: `options.Environment` has exactly one member, `ServerEnvironment.Production`, and its default base URL is the **sandbox/apitest** host (operations are stamped "Default (apitest)"). A call that seems to reach the "wrong" environment is usually this default, not a code bug. Applying `Visa:BaseUrl` as the base address is a config task — see trap T3. Source: sdk-map.md, operations/Invoices.md.

---

## 3. Trap notes (name the hazard; load the skill to resolve it)

- ⚠ **T1 — Authentication (Step 2, before the first call).** This SDK breaks the APIMatic pattern:
  no credentials property exists; every request is signed by an **opt-in HTTP Signature `SdkHook`**
  read **once inside the client constructor** from environment variables, including an enable
  **switch**. Your env vars (`VISA_MERCHANT_ID`, `VISA_KEY_ID`, `VISA_SECRET_KEY`) are part of a
  larger set (the skill documents the exact variable names and the switch); if the switch is unset
  every request goes out **unsigned** and still "works" locally until the server rejects it. Env
  vars must be set **before** the client is constructed. **MUST load `dotnet-authentication`.**
- ⚠ **T2 — Error boundary (before any try/catch).** Every op is Case A throw-only
  (`SdkException<{Op}Error>`); `TryGet…Response1` per status, `TryGetRawError` fallback; no
  `…Result` no-throw variants. How to read status + provider body safely, and the JsonException
  hazards below, are the skill's job. **MUST load `dotnet-error-handling`.**
- ⚠ **T3 — Base URL / server + resilience (client registration).** Whether `Visa:BaseUrl` is applied
  via `options.Server`/`ServerOptions` or the `HttpClient` base address, whether the retry/timeout
  options bound a whole call or a single attempt, and — critically — whether a non-idempotent
  write (CreateInvoice/Send/Cancel POSTs) can be **re-sent** on a transport failure, are all things
  the option names do not reveal. **MUST load `dotnet-configuration-resilience`.**
- ⚠ **T4 — Client lifetime (client & DI setup).** The `HttpClient`/handler pipeline must be
  long-lived and reused (not rebuilt per request). **MUST load `dotnet-client-initialization`.**
- ⚠ **T5 — Models (building requests).** Amounts are strings, enums are `StringEnum<T>` (none needed
  here), `DateTimeOffset` wire shape for `dueDate`, and unmodeled JSON fields are dropped on
  deserialize. **MUST load `dotnet-models`.**
- ⚠ **T6 — Manual pagination (reconcile step).** GetAllInvoices has no auto-pager; how to loop
  `offset`/`limit` to `TotalInvoices` without gaps/dupes, and how list calls bind named optional
  args, are the skill's job. **MUST load `dotnet-configuration-resilience`** (pagination section).
- ⚠ **T7 — Testing.** The `HttpClient` constructor arg is the test seam; match eShop's existing test
  framework. **MUST load `dotnet-testing`.**

---

## 4. REQUIRED READING (load BEFORE implementation; this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-authentication` | The HTTP Signature hook + env-var switch — this SDK's only auth route (T1). Always required here. |
| `dotnet-error-handling` | The try/catch boundary around every op, reading status/body, the JsonException rows below (T2). Always required. |
| `dotnet-client-initialization` | Client construction + DI registration + HttpClient lifetime (T4). |
| `dotnet-configuration-resilience` | Base-URL/server selection for `Visa:BaseUrl`, retries/timeouts on POST writes, manual pagination (T3, T6). |
| `dotnet-calling-endpoints` | Named-argument binding for optional params (e.g. `status`, `ct:`). |
| `dotnet-models` | Building request records, string amounts, `DateTimeOffset`, dropped-field behaviour (T5). |
| `dotnet-testing` | Faking the SDK at the HttpClient seam (T7). |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary — load
`dotnet-error-handling` before writing it:**
- A drifted or malformed **2xx** body (a missing `required` member, or a field whose live type
  differs from the generated one) surfaces as a `JsonException` from deserialization, **not** as an
  `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

**Assumptions**
- "Send/deliver to the customer" (capability 4) = `PerformSendAction` (POST `/delivery`), not
  `PerformPublishAction` (POST `/publication`), whose distinct semantics the map does not document.
- `CustomerInformation.MerchantCustomerId` will be set on create to an eShop-owned id, to identify
  your invoices in the list (see reconciliation note). If you instead rely on invoice number, accept
  the N+1 GetInvoice cost.
- Amounts/currency are passed as strings ("USD", "12.34") — the generated models are string-typed.

**Blockers / gaps (resolve before or during implementation)**
- **B1 — No server-side date-range filter for reconciliation (capability 6).** `GetAllInvoices` accepts
  only `offset`, `limit`, `status` — there is **no `from`/`to`/date parameter**, and it is the only
  invoice-list operation in the SDK. You cannot ask the provider for "invoices created between X and
  Y". You must page the whole result (optionally narrowed by `status`) and filter client-side on
  `Invoice1.CreatedDate` (a string). Confirm this matches the reconciliation requirement, or the
  requirement must change. Source: operations/Invoices.md, records-5/records-4.
- **B2 — Update forces re-sending the amount (capability 3).** `UpdateInvoiceRequest` marks
  `OrderInformation (OrderInformation60)` `!req`, whose `AmountDetails60.TotalAmount`/`Currency` are
  `!req`. The task says amount is not corrected here, but the SDK requires the field present. Resolve
  by resending the unchanged current order amount. Source: records-11-To-We.md, records-6/records-1.
- **B3 — "Already sent / canceled" update & cancel rejections are undocumented (capabilities 3 & 5).**
  These ops are Case A with 400/404/502 accessors, but with no operation Notes the map does not say
  which status/shape a "cannot update because sent" or "cannot cancel" business rejection uses. Treat
  as **UNVERIFIED**: catch `SdkException<UpdateInvoiceError>` / `SdkException<PerformCancelActionError>`,
  try the 400 accessor first, fall back to `TryGetRawError`, and surface the provider message
  best-effort rather than assuming a specific status. Source: operations/Invoices.md.
- **B4 — Invoice status/state string values are not in the map (capabilities 1, 4, 5).** `Status` is a
  plain string with no enum and no documented value set, so "draft", "sent", "canceled" literals are
  **UNVERIFIED**. Do not branch on hardcoded status strings as if confirmed; control draft-ness via
  `SendImmediately=false` and extract `Status` best-effort. Only live sandbox traffic can confirm the
  actual strings. Source: enums.md, records-5-In-Me.md.
