# Reference

> Source: [PayPalClient](PayPalClient.cs)

## Orders

> Source: [Orders](Api/Orders.cs)

<details>
<summary><code>Task&lt;OrderAuthorizeResponse&gt; AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Authorizes payment for an order. To successfully authorize payment for an order, the buyer must first approve the order or a valid payment_source must be provided in the request. A buyer can approve the order upon being redirected to the rel:approve URL that was returned in the HATEOAS links in the create order response. Note: For error handling and troubleshooting, see Orders v2 errors.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Orders.AuthorizeOrder(id,
        payPalMockResponse,
        payPalRequestId,
        payPalClientMetadataId,
        payPalAuthAssertion,
        body);
    // TODO: Handle 'response' of type OrderAuthorizeResponse
}
catch (SdkException<AuthorizeOrderError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the order for which to authorize. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 6 hours. The API callers can request the times to up to 72 hours by speaking to their Account Manager. It is mandatory for all single-step create order calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id, PayPal.billing_agreement_id, etc). |
| <code>payPalClientMetadataId</code> | <code>string?</code> | - |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |
| <code>body</code> | <code>[OrderAuthorizeRequest?](Models/OrderAuthorizeRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[OrderAuthorizeResponse](Models/OrderAuthorizeResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[AuthorizeOrderError](Errors/AuthorizeOrderError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Order&gt; CaptureOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderCaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Captures payment for an order. To successfully capture payment for an order, the buyer must first approve the order or a valid payment_source must be provided in the request. A buyer can approve the order upon being redirected to the rel:approve URL that was returned in the HATEOAS links in the create order response. Note: For error handling and troubleshooting, see Orders v2 errors.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Orders.CaptureOrder(id,
        payPalMockResponse,
        payPalRequestId,
        payPalClientMetadataId,
        payPalAuthAssertion,
        body);
    // TODO: Handle 'response' of type Order
}
catch (SdkException<CaptureOrderError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the order for which to capture a payment. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 6 hours. The API callers can request the times to up to 72 hours by speaking to their Account Manager. It is mandatory for all single-step create order calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id, PayPal.billing_agreement_id, etc). |
| <code>payPalClientMetadataId</code> | <code>string?</code> | - |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |
| <code>body</code> | <code>[OrderCaptureRequest?](Models/OrderCaptureRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Order](Models/Order.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CaptureOrderError](Errors/CaptureOrderError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Order&gt; ConfirmOrder(string id, string? payPalClientMetadataId, string? payPalAuthAssertion, ConfirmOrderRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Payer confirms their intent to pay for the the Order with the given payment source.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Orders.ConfirmOrder(id, payPalClientMetadataId, payPalAuthAssertion, body);
    // TODO: Handle 'response' of type Order
}
catch (SdkException<ConfirmOrderError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the order for which the payer confirms their intent to pay. |
| <code>payPalClientMetadataId</code> | <code>string?</code> | - |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |
| <code>body</code> | <code>[ConfirmOrderRequest?](Models/ConfirmOrderRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Order](Models/Order.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ConfirmOrderError](Errors/ConfirmOrderError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Order&gt; CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Creates an order. Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see checkout or multiparty checkout. Note: For error handling and troubleshooting, see Orders v2 errors.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Orders.CreateOrder(payPalMockResponse,
        payPalRequestId,
        payPalPartnerAttributionId,
        payPalClientMetadataId,
        payPalAuthAssertion,
        body);
    // TODO: Handle 'response' of type Order
}
catch (SdkException<CreateOrderError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 6 hours. The API callers can request the times to up to 72 hours by speaking to their Account Manager. It is mandatory for all single-step create order calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id, PayPal.billing_agreement_id, etc). |
| <code>payPalPartnerAttributionId</code> | <code>string?</code> | - |
| <code>payPalClientMetadataId</code> | <code>string?</code> | - |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |
| <code>body</code> | <code>[OrderRequest](Models/OrderRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Order](Models/Order.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreateOrderError](Errors/CreateOrderError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Order&gt; CreateOrderTracking(string id, string? payPalAuthAssertion, OrderTrackerRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Adds tracking information for an Order.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Orders.CreateOrderTracking(id, payPalAuthAssertion, body);
    // TODO: Handle 'response' of type Order
}
catch (SdkException<CreateOrderTrackingError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the order that the tracking information is associated with. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |
| <code>body</code> | <code>[OrderTrackerRequest](Models/OrderTrackerRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Order](Models/Order.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreateOrderTrackingError](Errors/CreateOrderTrackingError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Order&gt; GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Shows details for an order, by ID. Note: For error handling and troubleshooting, see Orders v2 errors.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Orders.GetOrder(id, fields, payPalMockResponse, payPalAuthAssertion);
    // TODO: Handle 'response' of type Order
}
catch (SdkException<GetOrderError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the order for which to show details. |
| <code>fields</code> | <code>string?</code> | A comma-separated list of fields that should be returned for the order. Valid filter field is `payment_source`. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Order](Models/Order.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetOrderError](Errors/GetOrderError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task PatchOrder(string id, string? payPalMockResponse, string? payPalAuthAssertion, IReadOnlyList&lt;Patch&gt;? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Updates an order with a `CREATED` or `APPROVED` status. You cannot update an order with the `COMPLETED` status.<br/><br/>To make an update, you must provide a `reference_id`. If you omit this value with an order that contains only one purchase unit, PayPal sets the value to `default` which enables you to use the path: <code>\"/purchase_units/@reference_id=='default'/{attribute-or-object}\"</code>. Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see <a href="https://developer.paypal.com/docs/checkout/advanced/processing/">checkout</a> or <a href="https://developer.paypal.com/docs/multiparty/checkout/advanced/processing/">multiparty checkout</a>.<blockquote><strong>Note:</strong> For error handling and troubleshooting, see <a href="https://developer.paypal.com/api/rest/reference/orders/v2/errors/#patch-order">Orders v2 errors</a>.</blockquote>Patchable attributes or objects:<br/><br/><table><thead><th>Attribute</th><th>Op</th><th>Notes</th></thead><tbody><tr><td><code>intent</code></td><td>replace</td><td></td></tr><tr><td><code>payer</code></td><td>replace, add</td><td>Using replace op for <code>payer</code> will replace the whole <code>payer</code> object with the value sent in request.</td></tr><tr><td><code>purchase_units</code></td><td>replace, add</td><td></td></tr><tr><td><code>purchase_units[].custom_id</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>purchase_units[].description</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>purchase_units[].payee.email</code></td><td>replace</td><td></td></tr><tr><td><code>purchase_units[].shipping.name</code></td><td>replace, add</td><td></td></tr><tr><td><code>purchase_units[].shipping.email_address</code></td><td>replace, add</td><td></td></tr><tr><td><code>purchase_units[].shipping.phone_number</code></td><td>replace, add</td><td></td></tr><tr><td><code>purchase_units[].shipping.options</code></td><td>replace, add</td><td></td></tr><tr><td><code>purchase_units[].shipping.address</code></td><td>replace, add</td><td></td></tr><tr><td><code>purchase_units[].shipping.type</code></td><td>replace, add</td><td></td></tr><tr><td><code>purchase_units[].soft_descriptor</code></td><td>replace, remove</td><td></td></tr><tr><td><code>purchase_units[].amount</code></td><td>replace</td><td></td></tr><tr><td><code>purchase_units[].items</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>purchase_units[].invoice_id</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>purchase_units[].payment_instruction</code></td><td>replace</td><td></td></tr><tr><td><code>purchase_units[].payment_instruction.disbursement_mode</code></td><td>replace</td><td>By default, <code>disbursement_mode</code> is <code>INSTANT</code>.</td></tr><tr><td><code>purchase_units[].payment_instruction.payee_receivable_fx_rate_id</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>purchase_units[].payment_instruction.platform_fees</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>purchase_units[].supplementary_data.airline</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>purchase_units[].supplementary_data.card</code></td><td>replace, add, remove</td><td></td></tr><tr><td><code>application_context.client_configuration</code></td><td>replace, add</td><td></td></tr></tbody></table>

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Orders.PatchOrder(id, payPalMockResponse, payPalAuthAssertion, body);
}
catch (SdkException<PatchOrderError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the order to update. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |
| <code>body</code> | <code>IReadOnlyList&lt;[Patch](Models/Patch.cs)&gt;?</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[PatchOrderError](Errors/PatchOrderError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task UpdateOrderTracking(string id, string trackerId, string? payPalAuthAssertion, IReadOnlyList&lt;Patch&gt;? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Updates or cancels the tracking information for a PayPal order, by ID. Updatable attributes or objects: Attribute Op Notes items replace Using replace op for items will replace the entire items object with the value sent in request. notify_payer replace, add status replace Only patching status to CANCELLED is currently supported.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Orders.UpdateOrderTracking(id, trackerId, payPalAuthAssertion, body);
}
catch (SdkException<UpdateOrderTrackingError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the order that the tracking information is associated with. |
| <code>trackerId</code> | <code>string</code> | The order tracking ID. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion. |
| <code>body</code> | <code>IReadOnlyList&lt;[Patch](Models/Patch.cs)&gt;?</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[UpdateOrderTrackingError](Errors/UpdateOrderTrackingError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Payments

> Source: [Payments](Api/Payments.cs)

<details>
<summary><code>Task&lt;CapturedPayment&gt; CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Captures an authorized payment, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Payments.CaptureAuthorizedPayment(authorizationId,
        payPalMockResponse,
        payPalRequestId,
        payPalAuthAssertion,
        body);
    // TODO: Handle 'response' of type CapturedPayment
}
catch (SdkException<CaptureAuthorizedPaymentError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>authorizationId</code> | <code>string</code> | The PayPal-generated ID for the authorized payment to capture. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 45 days. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see [PayPal-Auth-Assertion](/docs/api/reference/api-requests/#paypal-auth-assertion). Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject. |
| <code>body</code> | <code>[CaptureRequest?](Models/CaptureRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CapturedPayment](Models/CapturedPayment.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CaptureAuthorizedPaymentError](Errors/CaptureAuthorizedPaymentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;PaymentAuthorization&gt; GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Shows details for an authorized payment, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Payments.GetAuthorizedPayment(authorizationId, payPalMockResponse, payPalAuthAssertion);
    // TODO: Handle 'response' of type PaymentAuthorization
}
catch (SdkException<GetAuthorizedPaymentError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>authorizationId</code> | <code>string</code> | The ID of the authorized payment for which to show details. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see [PayPal-Auth-Assertion](/docs/api/reference/api-requests/#paypal-auth-assertion). Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[PaymentAuthorization](Models/PaymentAuthorization.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetAuthorizedPaymentError](Errors/GetAuthorizedPaymentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CapturedPayment&gt; GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Shows details for a captured payment, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Payments.GetCapturedPayment(captureId, payPalMockResponse);
    // TODO: Handle 'response' of type CapturedPayment
}
catch (SdkException<GetCapturedPaymentError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>captureId</code> | <code>string</code> | The PayPal-generated ID for the captured payment for which to show details. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CapturedPayment](Models/CapturedPayment.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetCapturedPaymentError](Errors/GetCapturedPaymentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Refund&gt; GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Shows details for a refund, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Payments.GetRefund(refundId, payPalMockResponse, payPalAuthAssertion);
    // TODO: Handle 'response' of type Refund
}
catch (SdkException<GetRefundError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>refundId</code> | <code>string</code> | The PayPal-generated ID for the refund for which to show details. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see [PayPal-Auth-Assertion](/docs/api/reference/api-requests/#paypal-auth-assertion). Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Refund](Models/Refund.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetRefundError](Errors/GetRefundError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;PaymentAuthorization&gt; ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Reauthorizes an authorized PayPal account payment, by ID. To ensure that funds are still available, reauthorize a payment after its initial three-day honor period expires. Within the 29-day authorization period, you can issue multiple re-authorizations after the honor period expires. If 30 days have transpired since the date of the original authorization, you must create an authorized payment instead of reauthorizing the original authorized payment. A reauthorized payment itself has a new honor period of three days. You can reauthorize an authorized payment from 4 to 29 days after the 3-day honor period. The allowed amount depends on context and geography, for example in US it is up to 115% of the original authorized amount, not to exceed an increase of $75 USD. Supports only the `amount` request parameter.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Payments.ReauthorizePayment(authorizationId,
        payPalRequestId,
        payPalAuthAssertion,
        body);
    // TODO: Handle 'response' of type PaymentAuthorization
}
catch (SdkException<ReauthorizePaymentError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>authorizationId</code> | <code>string</code> | The PayPal-generated ID for the authorized payment to reauthorize. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 45 days. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see [PayPal-Auth-Assertion](/docs/api/reference/api-requests/#paypal-auth-assertion). Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject. |
| <code>body</code> | <code>[ReauthorizeRequest?](Models/ReauthorizeRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[PaymentAuthorization](Models/PaymentAuthorization.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ReauthorizePaymentError](Errors/ReauthorizePaymentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Refund&gt; RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Refunds a captured payment, by ID. For a full refund, include an empty payload in the JSON request body. For a partial refund, include an amount object in the JSON request body.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Payments.RefundCapturedPayment(captureId,
        payPalMockResponse,
        payPalRequestId,
        payPalAuthAssertion,
        body);
    // TODO: Handle 'response' of type Refund
}
catch (SdkException<RefundCapturedPaymentError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>captureId</code> | <code>string</code> | The PayPal-generated ID for the captured payment to refund. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 45 days. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see [PayPal-Auth-Assertion](/docs/api/reference/api-requests/#paypal-auth-assertion). Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject. |
| <code>body</code> | <code>[RefundRequest?](Models/RefundRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Refund](Models/Refund.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RefundCapturedPaymentError](Errors/RefundCapturedPaymentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;PaymentAuthorization&gt; VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Voids, or cancels, an authorized payment, by ID. You cannot void an authorized payment that has been fully captured.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Payments.VoidPayment(authorizationId,
        payPalMockResponse,
        payPalAuthAssertion,
        payPalRequestId);
    // TODO: Handle 'response' of type PaymentAuthorization
}
catch (SdkException<VoidPaymentError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>authorizationId</code> | <code>string</code> | The PayPal-generated ID for the authorized payment to void. |
| <code>payPalMockResponse</code> | <code>string?</code> | PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant. |
| <code>payPalAuthAssertion</code> | <code>string?</code> | An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see [PayPal-Auth-Assertion](/docs/api/reference/api-requests/#paypal-auth-assertion). Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 45 days. |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[PaymentAuthorization](Models/PaymentAuthorization.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[VoidPaymentError](Errors/VoidPaymentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Subscriptions

> Source: [Subscriptions](Api/Subscriptions.cs)

<details>
<summary><code>Task ActivateBillingPlan(string id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Activates a plan, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.ActivateBillingPlan(id);
}
catch (SdkException<ActivateBillingPlanError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the plan. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ActivateBillingPlanError](Errors/ActivateBillingPlanError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task ActivateSubscription(string id, ActivateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Activates the subscription.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.ActivateSubscription(id, body);
}
catch (SdkException<ActivateSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the subscription. |
| <code>body</code> | <code>[ActivateSubscriptionRequest?](Models/ActivateSubscriptionRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ActivateSubscriptionError](Errors/ActivateSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task CancelSubscription(string id, CancelSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Cancels the subscription.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.CancelSubscription(id, body);
}
catch (SdkException<CancelSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the subscription. |
| <code>body</code> | <code>[CancelSubscriptionRequest?](Models/CancelSubscriptionRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CancelSubscriptionError](Errors/CancelSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SubscriptionTransactionDetails&gt; CaptureSubscription(string id, string? payPalRequestId, CaptureSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Captures an authorized payment from the subscriber on the subscription.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.CaptureSubscription(id, payPalRequestId, body);
    // TODO: Handle 'response' of type SubscriptionTransactionDetails
}
catch (SdkException<CaptureSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the subscription. |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 72 hours. |
| <code>body</code> | <code>[CaptureSubscriptionRequest?](Models/CaptureSubscriptionRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SubscriptionTransactionDetails](Models/SubscriptionTransactionDetails.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CaptureSubscriptionError](Errors/CaptureSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;BillingPlan&gt; CreateBillingPlan(string? payPalRequestId, PlanRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Creates a plan that defines pricing and billing cycle details for subscriptions.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.CreateBillingPlan(payPalRequestId, body);
    // TODO: Handle 'response' of type BillingPlan
}
catch (SdkException<CreateBillingPlanError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 72 hours. |
| <code>body</code> | <code>[PlanRequest?](Models/PlanRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[BillingPlan](Models/BillingPlan.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreateBillingPlanError](Errors/CreateBillingPlanError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Subscription&gt; CreateSubscription(string? payPalRequestId, string? payPalClientMetadataId, CreateSubscriptionRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Creates a subscription.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.CreateSubscription(payPalRequestId, payPalClientMetadataId, body);
    // TODO: Handle 'response' of type Subscription
}
catch (SdkException<CreateSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 72 hours. |
| <code>payPalClientMetadataId</code> | <code>string?</code> | The PayPal Client Metadata Id(CMID) is used to provide device-specific information to PayPal's risk engine. This is crucial for transactions that require device-specific risk assessments. Merchants typically use the Paypal SDK that automatically submits the CMID or they use tools like Fraudnet JS for web or Magnes JS for mobile to generate the CMID on the frontend and then pass it to the API as part of the request headers. |
| <code>body</code> | <code>[CreateSubscriptionRequest?](Models/CreateSubscriptionRequest.cs)</code> | - |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Subscription](Models/Subscription.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreateSubscriptionError](Errors/CreateSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task DeactivateBillingPlan(string id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Deactivates a plan, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.DeactivateBillingPlan(id);
}
catch (SdkException<DeactivateBillingPlanError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the plan. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[DeactivateBillingPlanError](Errors/DeactivateBillingPlanError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;BillingPlan&gt; GetBillingPlan(string id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Shows details for a plan, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.GetBillingPlan(id);
    // TODO: Handle 'response' of type BillingPlan
}
catch (SdkException<GetBillingPlanError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the plan. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[BillingPlan](Models/BillingPlan.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetBillingPlanError](Errors/GetBillingPlanError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;Subscription&gt; GetSubscription(string id, string? fields, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Shows details for a subscription, by ID.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.GetSubscription(id, fields);
    // TODO: Handle 'response' of type Subscription
}
catch (SdkException<GetSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the subscription. |
| <code>fields</code> | <code>string?</code> | List of fields that are to be returned in the response. Possible value for fields are last_failed_payment and plan. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[Subscription](Models/Subscription.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetSubscriptionError](Errors/GetSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;PlanCollection&gt; ListBillingPlans(string? productId, int? pageSize = 10, int? page = 1, bool? totalRequired = false, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Lists billing plans.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.ListBillingPlans(productId);
    // TODO: Handle 'response' of type PlanCollection
}
catch (SdkException<ListBillingPlansError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>productId</code> | <code>string?</code> | Filters the response by a Product ID. |
| <code>pageSize</code> | <code>int?</code> | The number of items to return in the response.<br>**Default**: 10 |
| <code>page</code> | <code>int?</code> | A non-zero integer which is the start index of the entire list of items to return in the response. The combination of `page=1` and `page_size=20` returns the first 20 items. The combination of `page=2` and `page_size=20` returns the next 20 items.<br>**Default**: 1 |
| <code>totalRequired</code> | <code>bool?</code> | Indicates whether to show the total count in the response.<br>**Default**: false |
| <code>prefer</code> | <code>string?</code> | The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, name, description and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.<br>**Default**: "return=minimal" |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[PlanCollection](Models/PlanCollection.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ListBillingPlansError](Errors/ListBillingPlansError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;TransactionsList&gt; ListSubscriptionTransactions(string id, string startTime, string endTime, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Lists transactions for a subscription.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.ListSubscriptionTransactions(id, startTime, endTime);
    // TODO: Handle 'response' of type TransactionsList
}
catch (SdkException<ListSubscriptionTransactionsError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the subscription. |
| <code>startTime</code> | <code>string</code> | The start time of the range of transactions to list. |
| <code>endTime</code> | <code>string</code> | The end time of the range of transactions to list. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TransactionsList](Models/TransactionsList.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ListSubscriptionTransactionsError](Errors/ListSubscriptionTransactionsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SubscriptionCollection&gt; ListSubscriptions(string? planIds, string? statuses, string? createdAfter, string? createdBefore, string? statusUpdatedBefore, string? statusUpdatedAfter, string? filter, IReadOnlyList&lt;string&gt;? customerIds, int? pageSize = 10, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

List all subscriptions for merchant account.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.ListSubscriptions(planIds,
        statuses,
        createdAfter,
        createdBefore,
        statusUpdatedBefore,
        statusUpdatedAfter,
        filter,
        customerIds);
    // TODO: Handle 'response' of type SubscriptionCollection
}
catch (SdkException<ListSubscriptionsError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>planIds</code> | <code>string?</code> | Filters the response by list of plan IDs. Filter supports upto 70 plan IDs. URLs should not exceed a length of 2000 characters. |
| <code>statuses</code> | <code>string?</code> | Filters the response by list of subscription statuses. |
| <code>createdAfter</code> | <code>string?</code> | Filters the response by subscription creation start time for a range of subscriptions. |
| <code>createdBefore</code> | <code>string?</code> | Filters the response by subscription creation end time for a range of subscriptions. |
| <code>statusUpdatedBefore</code> | <code>string?</code> | Filters the response by status update start time for a range of subscriptions. |
| <code>statusUpdatedAfter</code> | <code>string?</code> | Filters the response by status update end time for a range of subscriptions. |
| <code>filter</code> | <code>string?</code> | Filter the response using complex expressions that could use comparison operators like ge, gt, le, lt and logical operators such as 'and' and 'or'. |
| <code>customerIds</code> | <code>IReadOnlyList&lt;string&gt;?</code> | Filters the response by comma separated vault customer IDs (FSS subscriptions only). |
| <code>pageSize</code> | <code>int?</code> | The number of items to return in the response.<br>**Default**: 10 |
| <code>page</code> | <code>int?</code> | A non-zero integer which is the start index of the entire list of items to return in the response. The combination of `page=1` and `page_size=20` returns the first 20 items. The combination of `page=2` and `page_size=20` returns the next 20 items.<br>**Default**: 1 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SubscriptionCollection](Models/SubscriptionCollection.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ListSubscriptionsError](Errors/ListSubscriptionsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task PatchBillingPlan(string id, IReadOnlyList&lt;Patch&gt;? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Updates a plan with the `CREATED` or `ACTIVE` status. For an `INACTIVE` plan, you can make only status updates. You can patch these attributes and objects: Attribute or object Operations description replace payment_preferences.auto_bill_outstanding replace taxes.percentage replace payment_preferences.payment_failure_threshold replace payment_preferences.setup_fee replace payment_preferences.setup_fee_failure_action replace name replace

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.PatchBillingPlan(id, body);
}
catch (SdkException<PatchBillingPlanError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the plan. |
| <code>body</code> | <code>IReadOnlyList&lt;[Patch](Models/Patch.cs)&gt;?</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[PatchBillingPlanError](Errors/PatchBillingPlanError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task PatchSubscription(string id, IReadOnlyList&lt;Patch&gt;? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Updates a subscription which could be in ACTIVE or SUSPENDED status. You can override plan level default attributes by providing customised values for plan path in the patch request. You cannot update attributes that have already completed (Example - trial cycles can’t be updated if completed). Once overridden, changes to plan resource will not impact subscription. Any price update will not impact billing cycles within next 10 days (Applicable only for subscriptions funded by PayPal account). Following are the fields eligible for patch. Attribute or object Operations billing_info.outstanding_balance replace custom_id add,replace plan.billing_cycles[@sequence==n]. pricing_scheme.fixed_price add,replace plan.billing_cycles[@sequence==n]. pricing_scheme.tiers replace plan.billing_cycles[@sequence==n]. total_cycles replace plan.payment_preferences. auto_bill_outstanding replace plan.payment_preferences. payment_failure_threshold replace plan.taxes.inclusive add,replace plan.taxes.percentage add,replace shipping_amount add,replace start_time replace subscriber.shipping_address add,replace subscriber.payment_source (for subscriptions funded by card payments) replace

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.PatchSubscription(id, body);
}
catch (SdkException<PatchSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID for the subscription. |
| <code>body</code> | <code>IReadOnlyList&lt;[Patch](Models/Patch.cs)&gt;?</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[PatchSubscriptionError](Errors/PatchSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ModifySubscriptionResponse&gt; ReviseSubscription(string id, ModifySubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Updates the quantity of the product or service in a subscription. You can also use this method to switch the plan and update the `shipping_amount`, `shipping_address` values for the subscription. This type of update requires the buyer's consent.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Subscriptions.ReviseSubscription(id, body);
    // TODO: Handle 'response' of type ModifySubscriptionResponse
}
catch (SdkException<ReviseSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the subscription. |
| <code>body</code> | <code>[ModifySubscriptionRequest?](Models/ModifySubscriptionRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ModifySubscriptionResponse](Models/ModifySubscriptionResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ReviseSubscriptionError](Errors/ReviseSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task SuspendSubscription(string id, SuspendSubscription? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Suspends the subscription.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.SuspendSubscription(id, body);
}
catch (SdkException<SuspendSubscriptionError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID of the subscription. |
| <code>body</code> | <code>[SuspendSubscription?](Models/SuspendSubscription.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[SuspendSubscriptionError](Errors/SuspendSubscriptionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task UpdateBillingPlanPricingSchemes(string id, UpdatePricingSchemesRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Updates pricing for a plan. For example, you can update a regular billing cycle from $5 per month to $7 per month.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Subscriptions.UpdateBillingPlanPricingSchemes(id, body);
}
catch (SdkException<UpdateBillingPlanPricingSchemesError> ex)
{
    if (ex.Error.TryGetSubscriptionError(out var error))
    {
        // TODO: Handle 'error' of type SubscriptionError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | The ID for the plan. |
| <code>body</code> | <code>[UpdatePricingSchemesRequest?](Models/UpdatePricingSchemesRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[UpdateBillingPlanPricingSchemesError](Errors/UpdateBillingPlanPricingSchemesError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## TransactionSearch

> Source: [TransactionSearch](Api/TransactionSearch.cs)

<details>
<summary><code>Task&lt;BalancesResponse&gt; SearchBalances(string? asOfTime, string? currencyCode, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

List all balances. Specify date time to list balances for that time that appear in the response. Notes: It takes a maximum of three hours for balances to appear in the list balances call. This call lists balances upto the previous three years.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.TransactionSearch.SearchBalances(asOfTime, currencyCode);
    // TODO: Handle 'response' of type BalancesResponse
}
catch (SdkException<SearchBalancesError> ex)
{
    if (ex.Error.TryGetDefaultError(out var error))
    {
        // TODO: Handle 'error' of type DefaultError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>asOfTime</code> | <code>string?</code> | List balances in the response at the date time provided, will return the last refreshed balance in the system when not provided. |
| <code>currencyCode</code> | <code>string?</code> | Filters the transactions in the response by a [three-character ISO-4217 currency code](https://developer.paypal.com/api/rest/reference/currency-codes/) for the PayPal transaction currency. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[BalancesResponse](Models/BalancesResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[SearchBalancesError](Errors/SearchBalancesError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SearchResponse&gt; SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Lists transactions. Specify one or more query parameters to filter the transaction that appear in the response. Notes: If you specify one or more optional query parameters, the ending_balance response field is empty. It takes a maximum of three hours for executed transactions to appear in the list transactions call. This call lists transaction for the previous three years.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.TransactionSearch.SearchTransactions(startDate,
        endDate,
        transactionId,
        transactionType,
        transactionStatus,
        transactionAmount,
        transactionCurrency,
        paymentInstrumentType,
        storeId,
        terminalId);
    // TODO: Handle 'response' of type SearchResponse
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>startDate</code> | <code>string</code> | Filters the transactions in the response by a start date and time, in [Internet date and time format](https://tools.ietf.org/html/rfc3339#section-5.6). Seconds are required. Fractional seconds are optional. |
| <code>endDate</code> | <code>string</code> | Filters the transactions in the response by an end date and time, in [Internet date and time format](https://tools.ietf.org/html/rfc3339#section-5.6). Seconds are required. Fractional seconds are optional. The maximum supported range is 31 days. |
| <code>transactionId</code> | <code>string?</code> | Filters the transactions in the response by a PayPal transaction ID. A valid transaction ID is 17 characters long, except for an order ID, which is 19 characters long. Note: A transaction ID is not unique in the reporting system. The response can list two transactions with the same ID. One transaction can be balance affecting while the other is non-balance affecting. |
| <code>transactionType</code> | <code>string?</code> | Filters the transactions in the response by a PayPal transaction event code. See [Transaction event codes](/docs/integration/direct/transaction-search/transaction-event-codes/). |
| <code>transactionStatus</code> | <code>string?</code> | Filters the transactions in the response by a PayPal transaction status code. Value is: Status code Description D PayPal or merchant rules denied the transaction. P The transaction is pending. The transaction was created but waits for another payment process to complete, such as an ACH transaction, before the status changes to S. S The transaction successfully completed without a denial and after any pending statuses. V A successful transaction was reversed and funds were refunded to the original sender. |
| <code>transactionAmount</code> | <code>string?</code> | Filters the transactions in the response by a gross transaction amount range. Specify the range as ` TO `, where ` ` is the lower limit of the gross PayPal transaction amount and ` ` is the upper limit of the gross transaction amount. Specify the amounts in lower denominations. For example, to search for transactions from $5.00 to $10.05, specify `[500 TO 1005]`. Note:The values must be URL encoded. |
| <code>transactionCurrency</code> | <code>string?</code> | Filters the transactions in the response by a [three-character ISO-4217 currency code](https://developer.paypal.com/api/rest/reference/currency-codes/) for the PayPal transaction currency. |
| <code>paymentInstrumentType</code> | <code>string?</code> | Filters the transactions in the response by a payment instrument type. Value is either: CREDITCARD. Returns a direct credit card transaction with a corresponding value. DEBITCARD. Returns a debit card transaction with a corresponding value. If you omit this parameter, the API does not apply this filter. |
| <code>storeId</code> | <code>string?</code> | Filters the transactions in the response by a store ID. |
| <code>terminalId</code> | <code>string?</code> | Filters the transactions in the response by a terminal ID. |
| <code>fields</code> | <code>string?</code> | Indicates which fields appear in the response. Value is a single field or a comma-separated list of fields. The transaction_info value returns only the transaction details in the response. To include all fields in the response, specify fields=all. Valid fields are: transaction_info. The transaction information. Includes the ID of the PayPal account of the payee, the PayPal-generated transaction ID, the PayPal-generated base ID, the PayPal reference ID type, the transaction event code, the date and time when the transaction was initiated and was last updated, the transaction amounts including the PayPal fee, any discounts, insurance, the transaction status, and other information about the transaction. payer_info. The payer information. Includes the PayPal customer account ID and the payer's email address, primary phone number, name, country code, address, and whether the payer is verified or unverified. shipping_info. The shipping information. Includes the recipient's name, the shipping method for this order, the shipping address for this order, and the secondary address associated with this order. auction_info. The auction information. Includes the name of the auction site, the auction site URL, the ID of the customer who makes the purchase in the auction, and the date and time when the auction closes. cart_info. The cart information. Includes an array of item details, whether the item amount or the shipping amount already includes tax, and the ID of the invoice for PayPal-generated invoices. incentive_info. An array of incentive detail objects. Each object includes the incentive, such as a special offer or coupon, the incentive amount, and the incentive program code that identifies a merchant loyalty or incentive program. store_info. The store information. Includes the ID of the merchant store and the terminal ID for the checkout stand in the merchant store.<br>**Default**: "transaction_info" |
| <code>balanceAffectingRecordsOnly</code> | <code>string?</code> | Indicates whether the response includes only balance-impacting transactions or all transactions. Value is either: Y. The default. The response includes only balance transactions. N. The response includes all transactions.<br>**Default**: "Y" |
| <code>pageSize</code> | <code>int?</code> | The number of items to return in the response. So, the combination of `page=1` and `page_size=20` returns the first 20 items. The combination of `page=2` and `page_size=20` returns the next 20 items.<br>**Default**: 100 |
| <code>page</code> | <code>int?</code> | The zero-relative start index of the entire list of items that are returned in the response. So, the combination of `page=1` and `page_size=20` returns the first 20 items.<br>**Default**: 1 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SearchResponse](Models/SearchResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Vault

> Source: [Vault](Api/Vault.cs)

<details>
<summary><code>Task&lt;PaymentTokenResponse&gt; CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Creates a Payment Token from the given payment source and adds it to the Vault of the associated customer.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Vault.CreatePaymentToken(payPalRequestId, body);
    // TODO: Handle 'response' of type PaymentTokenResponse
}
catch (SdkException<CreatePaymentTokenError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 3 hours. |
| <code>body</code> | <code>[PaymentTokenRequest](Models/PaymentTokenRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[PaymentTokenResponse](Models/PaymentTokenResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreatePaymentTokenError](Errors/CreatePaymentTokenError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SetupTokenResponse&gt; CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Creates a Setup Token from the given payment source and adds it to the Vault of the associated customer.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Vault.CreateSetupToken(payPalRequestId, body);
    // TODO: Handle 'response' of type SetupTokenResponse
}
catch (SdkException<CreateSetupTokenError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>payPalRequestId</code> | <code>string?</code> | The server stores keys for 3 hours. |
| <code>body</code> | <code>[SetupTokenRequest](Models/SetupTokenRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SetupTokenResponse](Models/SetupTokenResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreateSetupTokenError](Errors/CreateSetupTokenError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Delete the payment token associated with the payment token id.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    await client.Vault.DeletePaymentToken(id);
}
catch (SdkException<DeletePaymentTokenError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | ID of the payment token. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: No content

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[DeletePaymentTokenError](Errors/DeletePaymentTokenError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;PaymentTokenResponse&gt; GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Returns a readable representation of vaulted payment source associated with the payment token id.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Vault.GetPaymentToken(id);
    // TODO: Handle 'response' of type PaymentTokenResponse
}
catch (SdkException<GetPaymentTokenError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | ID of the payment token. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[PaymentTokenResponse](Models/PaymentTokenResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetPaymentTokenError](Errors/GetPaymentTokenError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SetupTokenResponse&gt; GetSetupToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Returns a readable representation of temporarily vaulted payment source associated with the setup token id.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Vault.GetSetupToken(id);
    // TODO: Handle 'response' of type SetupTokenResponse
}
catch (SdkException<GetSetupTokenError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | ID of the setup token. |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SetupTokenResponse](Models/SetupTokenResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetSetupTokenError](Errors/GetSetupTokenError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CustomerVaultPaymentTokensResponse&gt; ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Returns all payment tokens for a customer.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Vault.ListCustomerPaymentTokens(customerId);
    // TODO: Handle 'response' of type CustomerVaultPaymentTokensResponse
}
catch (SdkException<ListCustomerPaymentTokensError> ex)
{
    if (ex.Error.TryGetError(out var error))
    {
        // TODO: Handle 'error' of type Error
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>customerId</code> | <code>string</code> | A unique identifier representing a specific customer in merchant's/partner's system or records. |
| <code>pageSize</code> | <code>int?</code> | A non-negative, non-zero integer indicating the maximum number of results to return at one time.<br>**Default**: 5 |
| <code>page</code> | <code>int?</code> | A non-negative, non-zero integer representing the page of the results.<br>**Default**: 1 |
| <code>totalRequired</code> | <code>bool?</code> | A boolean indicating total number of items (total_items) and pages (total_pages) are expected to be returned in the response.<br>**Default**: false |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CustomerVaultPaymentTokensResponse](Models/CustomerVaultPaymentTokensResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ListCustomerPaymentTokensError](Errors/ListCustomerPaymentTokensError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

