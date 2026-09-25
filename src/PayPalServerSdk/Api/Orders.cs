using System;
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
using PayPalServerSdk.Requests.Orders;

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
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OrderAuthorizeResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="AuthorizeOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Authorizes payment for an order. To successfully authorize payment for an order, the buyer must first approve the order or a valid payment_source must be provided in the request. A buyer can approve the order upon being redirected to the rel:approve URL that was returned in the HATEOAS links in the create order response. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<OrderAuthorizeResponse> AuthorizeOrder(AuthorizeOrderRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/authorize"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Client-Metadata-Id", request.PayPalClientMetadataId),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<OrderAuthorizeResponse>(),
            AuthorizeOrderError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Capture payment for order
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CaptureOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Captures payment for an order. To successfully capture payment for an order, the buyer must first approve the order or a valid payment_source must be provided in the request. A buyer can approve the order upon being redirected to the rel:approve URL that was returned in the HATEOAS links in the create order response. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<Order> CaptureOrder(CaptureOrderRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/capture"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Client-Metadata-Id", request.PayPalClientMetadataId),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<Order>(),
            CaptureOrderError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Confirm the Order
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ConfirmOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Payer confirms their intent to pay for the the Order with the given payment source.
    /// </remarks>
    public Task<Order> ConfirmOrder(ConfirmOrderOperationRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/confirm-payment-source"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("PayPal-Client-Metadata-Id", request.PayPalClientMetadataId),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<Order>(),
            ConfirmOrderError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Create order
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CreateOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an order. Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see checkout or multiparty checkout. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<Order> CreateOrder(CreateOrderRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders"),
            [],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("PayPal-Partner-Attribution-Id", request.PayPalPartnerAttributionId),
                new HeaderParam("PayPal-Client-Metadata-Id", request.PayPalClientMetadataId),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<Order>(),
            CreateOrderError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Add tracking information for an Order.
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CreateOrderTrackingError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Adds tracking information for an Order.
    /// </remarks>
    public Task<Order> CreateOrderTracking(CreateOrderTrackingRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/track"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<Order>(),
            CreateOrderTrackingError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Show order details
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Order"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for an order, by ID. Note: For error handling and troubleshooting, see Orders v2 errors.
    /// </remarks>
    public Task<Order> GetOrder(GetOrderRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}"),
            [new TemplateParam("id", request.Id)],
            [new Param("fields", request.Fields)],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<Order>(),
            GetOrderError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Update order
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="PatchOrderError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates an order with a <c>CREATED</c> or <c>APPROVED</c> status. You cannot update an order with the <c>COMPLETED</c> status.&lt;br/&gt;&lt;br/&gt;To make an update, you must provide a <c>reference_id</c>. If you omit this value with an order that contains only one purchase unit, PayPal sets the value to <c>default</c> which enables you to use the path: &lt;code&gt;\"/purchase_units/@reference_id=='default'/{attribute-or-object}\"&lt;/code&gt;. Merchants and partners can add Level 2 and 3 data to payments to reduce risk and payment processing costs. For more information about processing payments, see &lt;a href="https://developer.paypal.com/docs/checkout/advanced/processing/"&gt;checkout&lt;/a&gt; or &lt;a href="https://developer.paypal.com/docs/multiparty/checkout/advanced/processing/"&gt;multiparty checkout&lt;/a&gt;.&lt;blockquote&gt;&lt;strong&gt;Note:&lt;/strong&gt; For error handling and troubleshooting, see &lt;a href="https://developer.paypal.com/api/rest/reference/orders/v2/errors/#patch-order"&gt;Orders v2 errors&lt;/a&gt;.&lt;/blockquote&gt;Patchable attributes or objects:&lt;br/&gt;&lt;br/&gt;&lt;table&gt;&lt;thead&gt;&lt;th&gt;Attribute&lt;/th&gt;&lt;th&gt;Op&lt;/th&gt;&lt;th&gt;Notes&lt;/th&gt;&lt;/thead&gt;&lt;tbody&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;intent&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;payer&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;Using replace op for &lt;code&gt;payer&lt;/code&gt; will replace the whole &lt;code&gt;payer&lt;/code&gt; object with the value sent in request.&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].custom_id&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].description&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payee.email&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.name&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.email_address&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.phone_number&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.options&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.address&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].shipping.type&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].soft_descriptor&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].amount&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].items&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].invoice_id&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction.disbursement_mode&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace&lt;/td&gt;&lt;td&gt;By default, &lt;code&gt;disbursement_mode&lt;/code&gt; is &lt;code&gt;INSTANT&lt;/code&gt;.&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction.payee_receivable_fx_rate_id&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].payment_instruction.platform_fees&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].supplementary_data.airline&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;purchase_units[].supplementary_data.card&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add, remove&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;tr&gt;&lt;td&gt;&lt;code&gt;application_context.client_configuration&lt;/code&gt;&lt;/td&gt;&lt;td&gt;replace, add&lt;/td&gt;&lt;td&gt;&lt;/td&gt;&lt;/tr&gt;&lt;/tbody&gt;&lt;/table&gt;
    /// </remarks>
    public Task PatchOrder(PatchOrderRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            PatchOrderError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Update or cancel tracking information for an order
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="UpdateOrderTrackingError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates or cancels the tracking information for a PayPal order, by ID. Updatable attributes or objects: Attribute Op Notes items replace Using replace op for items will replace the entire items object with the value sent in request. notify_payer replace, add status replace Only patching status to CANCELLED is currently supported.
    /// </remarks>
    public Task UpdateOrderTracking(UpdateOrderTrackingRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/checkout/orders/{id}/trackers/{tracker_id}"),
            [new TemplateParam("id", request.Id), new TemplateParam("tracker_id", request.TrackerId)],
            [],
            [new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            UpdateOrderTrackingError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);
}
