using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Request;
using PayPalServerSdk.Core.Response;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Api;

/// <summary>
/// Use the <c>/orders</c> resource to create, update, retrieve, authorize, capture and track orders.
/// </summary>
public sealed class Orders
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Orders(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Authorize payment for order
    /// </summary>
    /// <param name="id">The ID of the order for which to authorize.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalRequestId">The server stores keys for 6 hours. The API callers can request the times to up to 72 hours by speaking to their Account Manager. It is mandatory for all single-step create order calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id, PayPal.billing_agreement_id, etc).</param>
    /// <param name="payPalClientMetadataId"></param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OrderAuthorizeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="AuthorizeOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Authorizes payment for an order. To successfully authorize payment for an order, the buyer must first approve the order or a valid payment_source must be provided in the request. A buyer can approve the order upon being redirected to the rel:approve URL that was returned in the HATEOAS links in the create order response. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<OrderAuthorizeResponse> AuthorizeOrder(string id,
        string? payPalMockResponse,
        string? payPalRequestId,
        string? payPalClientMetadataId,
        string? payPalAuthAssertion,
        OrderAuthorizeRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/authorize"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Client-Metadata-Id", payPalClientMetadataId),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<OrderAuthorizeResponse>(),
            AuthorizeOrderErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Capture payment for order
    /// </summary>
    /// <param name="id">The ID of the order for which to capture a payment.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalRequestId">The server stores keys for 6 hours. The API callers can request the times to up to 72 hours by speaking to their Account Manager. It is mandatory for all single-step create order calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id, PayPal.billing_agreement_id, etc).</param>
    /// <param name="payPalClientMetadataId"></param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CaptureOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Captures payment for an order. To successfully capture payment for an order, the buyer must first approve the order or a valid payment_source must be provided in the request. A buyer can approve the order upon being redirected to the rel:approve URL that was returned in the HATEOAS links in the create order response. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<Order> CaptureOrder(string id,
        string? payPalMockResponse,
        string? payPalRequestId,
        string? payPalClientMetadataId,
        string? payPalAuthAssertion,
        OrderCaptureRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/capture"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Client-Metadata-Id", payPalClientMetadataId),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Order>(),
            CaptureOrderErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Confirm the Order
    /// </summary>
    /// <param name="id">The ID of the order for which the payer confirms their intent to pay.</param>
    /// <param name="payPalClientMetadataId"></param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ConfirmOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Payer confirms their intent to pay for the the Order with the given payment source.
    /// </remarks>
    public Task<Order> ConfirmOrder(string id,
        string? payPalClientMetadataId,
        string? payPalAuthAssertion,
        ConfirmOrderRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/confirm-payment-source"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("PayPal-Client-Metadata-Id", payPalClientMetadataId),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Order>(),
            ConfirmOrderErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Create order
    /// </summary>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalRequestId">The server stores keys for 6 hours. The API callers can request the times to up to 72 hours by speaking to their Account Manager. It is mandatory for all single-step create order calls (E.g. Create Order Request with payment source information like Card, PayPal.vault_id, PayPal.billing_agreement_id, etc).</param>
    /// <param name="payPalPartnerAttributionId"></param>
    /// <param name="payPalClientMetadataId"></param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an order. Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see checkout or multiparty checkout. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<Order> CreateOrder(string? payPalMockResponse,
        string? payPalRequestId,
        string? payPalPartnerAttributionId,
        string? payPalClientMetadataId,
        string? payPalAuthAssertion,
        OrderRequest body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders"),
            [],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("PayPal-Partner-Attribution-Id", payPalPartnerAttributionId),
                new HeaderParam("PayPal-Client-Metadata-Id", payPalClientMetadataId),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Order>(),
            CreateOrderErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Add tracking information for an Order.
    /// </summary>
    /// <param name="id">The ID of the order that the tracking information is associated with.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateOrderTrackingError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Adds tracking information for an Order.
    /// </remarks>
    public Task<Order> CreateOrderTracking(string id,
        string? payPalAuthAssertion,
        OrderTrackerRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/track"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Order>(),
            CreateOrderTrackingErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Show order details
    /// </summary>
    /// <param name="id">The ID of the order for which to show details.</param>
    /// <param name="fields">A comma-separated list of fields that should be returned for the order. Valid filter field is <c>payment_source</c>.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for an order, by ID. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<Order> GetOrder(string id,
        string? fields,
        string? payPalMockResponse,
        string? payPalAuthAssertion,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}"),
            [new TemplateParam("id", id)],
            [new Param("fields", fields)],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<Order>(),
            GetOrderErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Update order
    /// </summary>
    /// <param name="id">The ID of the order to update.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PatchOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates an order with a <c>CREATED</c> or <c>APPROVED</c> status. You cannot update an order with the <c>COMPLETED</c> status.&lt;br/&gt;&lt;br/&gt;To make an update, you must provide a <c>reference_id</c>. If you omit this value with an order that contains only one purchase unit, PayPal sets the value to <c>default</c> which enables you to use the path: &lt;code&gt;\"/purchase_units/@reference_id=='default'/{attribute-or-object}\"&lt;/code&gt;. Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see &lt;a href="https://developer.paypal.com/docs/checkout/advanced/processing/"&gt;checkout&lt;/a&gt; or &lt;a href="https://developer.paypal.com/docs/multiparty/checkout/advanced/processing/"&gt;multiparty checkout&lt;/a&gt;.&lt;blockquote&gt;&lt;strong&gt;Note:&lt;/strong&gt; For error handling and troubleshooting, see &lt;a href="https://developer.paypal.com/api/rest/reference/orders/v2/errors/#patch-order"&gt;Orders v2 errors&lt;/a&gt;.&lt;/blockquote&gt;Patchable attributes or objects:&lt;br/&gt;&lt;br/&gt;&lt;table&gt;&lt;thead&gt;&lt;th&gt;Attribute&lt;/th&gt;&lt;th&gt;Op&lt;/th&gt;&lt;th&gt;Notes&lt;/th&gt;&lt;/thead&gt;&lt;tbody&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;intent&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;payer&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;Using replace op for &lt;code&gt;payer&lt;/code&gt; will replace the whole &lt;code&gt;payer&lt;/code&gt; object with the value sent in request.&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].custom_id&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].description&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payee.email&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.name&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.email_address&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.phone_number&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.options&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.address&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.type&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].soft_descriptor&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].amount&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].items&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].invoice_id&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction.disbursement_mode&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;By default, &lt;code&gt;disbursement_mode&lt;/code&gt; is &lt;code&gt;INSTANT&lt;/code&gt;.&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction.payee_receivable_fx_rate_id&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction.platform_fees&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].supplementary_data.airline&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].supplementary_data.card&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;application_context.client_configuration&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;/tbody&gt;&lt;/table&gt;
    /// </remarks>
    public Task PatchOrder(string id,
        string? payPalMockResponse,
        string? payPalAuthAssertion,
        IReadOnlyList<Patch>? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(body),
            VoidResponse.Instance,
            PatchOrderErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Update or cancel tracking information for an order
    /// </summary>
    /// <param name="id">The ID of the order that the tracking information is associated with.</param>
    /// <param name="trackerId">The order tracking ID.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see PayPal-Auth-Assertion.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateOrderTrackingError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates or cancels the tracking information for a PayPal order, by ID. Updatable attributes or objects: Attribute Op Notes items replace Using replace op for items will replace the entire items object with the value sent in request. notify_payer replace, add status replace Only patching status to CANCELLED is currently supported.
    /// </remarks>
    public Task UpdateOrderTracking(string id,
        string trackerId,
        string? payPalAuthAssertion,
        IReadOnlyList<Patch>? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/trackers/{tracker_id}"),
            [new TemplateParam("id", id), new TemplateParam("tracker_id", trackerId)],
            [],
            [new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(body),
            VoidResponse.Instance,
            UpdateOrderTrackingErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);
}
