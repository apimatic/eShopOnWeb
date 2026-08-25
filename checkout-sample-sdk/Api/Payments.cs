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

namespace PayPalServerSdk.Api;

/// <summary>
/// Use the <c>/payments</c> resource to authorize, capture, void authorizations, and retrieve captures.
/// </summary>
public sealed class Payments
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Payments(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Capture authorized payment
    /// </summary>
    /// <param name="authorizationId">The PayPal-generated ID for the authorized payment to capture.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalRequestId">The server stores keys for 45 days.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CapturedPayment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CaptureAuthorizedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Captures an authorized payment, by ID.
    /// </remarks>
    public Task<CapturedPayment> CaptureAuthorizedPayment(string authorizationId,
        string? payPalMockResponse,
        string? payPalRequestId,
        string? payPalAuthAssertion,
        CaptureRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}/capture"),
            [new TemplateParam("authorization_id", authorizationId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CapturedPayment>(),
            CaptureAuthorizedPaymentErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Show details for authorized payment
    /// </summary>
    /// <param name="authorizationId">The ID of the authorized payment for which to show details.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentAuthorization"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetAuthorizedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for an authorized payment, by ID.
    /// </remarks>
    public Task<PaymentAuthorization> GetAuthorizedPayment(string authorizationId,
        string? payPalMockResponse,
        string? payPalAuthAssertion,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}"),
            [new TemplateParam("authorization_id", authorizationId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PaymentAuthorization>(),
            GetAuthorizedPaymentErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Show captured payment details
    /// </summary>
    /// <param name="captureId">The PayPal-generated ID for the captured payment for which to show details.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CapturedPayment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetCapturedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a captured payment, by ID.
    /// </remarks>
    public Task<CapturedPayment> GetCapturedPayment(string captureId,
        string? payPalMockResponse,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/captures/{capture_id}"),
            [new TemplateParam("capture_id", captureId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CapturedPayment>(),
            GetCapturedPaymentErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Show refund details
    /// </summary>
    /// <param name="refundId">The PayPal-generated ID for the refund for which to show details.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Refund"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetRefundError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a refund, by ID.
    /// </remarks>
    public Task<Refund> GetRefund(string refundId,
        string? payPalMockResponse,
        string? payPalAuthAssertion,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/refunds/{refund_id}"),
            [new TemplateParam("refund_id", refundId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<Refund>(),
            GetRefundErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Reauthorize authorized payment
    /// </summary>
    /// <param name="authorizationId">The PayPal-generated ID for the authorized payment to reauthorize.</param>
    /// <param name="payPalRequestId">The server stores keys for 45 days.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentAuthorization"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReauthorizePaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Reauthorizes an authorized PayPal account payment, by ID. To ensure that funds are still available, reauthorize a payment after its initial three-day honor period expires. Within the 29-day authorization period, you can issue multiple re-authorizations after the honor period expires. If 30 days have transpired since the date of the original authorization, you must create an authorized payment instead of reauthorizing the original authorized payment. A reauthorized payment itself has a new honor period of three days. You can reauthorize an authorized payment from 4 to 29 days after the 3-day honor period. The allowed amount depends on context and geography, for example in US it is up to 115% of the original authorized amount, not to exceed an increase of $75 USD. Supports only the <c>amount</c> request parameter.
    /// </remarks>
    public Task<PaymentAuthorization> ReauthorizePayment(string authorizationId,
        string? payPalRequestId,
        string? payPalAuthAssertion,
        ReauthorizeRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}/reauthorize"),
            [new TemplateParam("authorization_id", authorizationId)],
            [],
            [new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<PaymentAuthorization>(),
            ReauthorizePaymentErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Refund captured payment
    /// </summary>
    /// <param name="captureId">The PayPal-generated ID for the captured payment to refund.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalRequestId">The server stores keys for 45 days.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Refund"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RefundCapturedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Refunds a captured payment, by ID. For a full refund, include an empty payload in the JSON request body. For a partial refund, include an amount object in the JSON request body.
    /// </remarks>
    public Task<Refund> RefundCapturedPayment(string captureId,
        string? payPalMockResponse,
        string? payPalRequestId,
        string? payPalAuthAssertion,
        RefundRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/captures/{capture_id}/refund"),
            [new TemplateParam("capture_id", captureId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Refund>(),
            RefundCapturedPaymentErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Void authorized payment
    /// </summary>
    /// <param name="authorizationId">The PayPal-generated ID for the authorized payment to void.</param>
    /// <param name="payPalMockResponse">PayPal's REST API uses a request header to invoke negative testing in the sandbox. This header configures the sandbox into a negative testing state for transactions that include the merchant.</param>
    /// <param name="payPalAuthAssertion">An API-caller-provided JSON Web Token (JWT) assertion that identifies the merchant. For details, see <see href="/docs/api/reference/api-requests/#paypal-auth-assertion">PayPal-Auth-Assertion</see>. Note:For three party transactions in which a partner is managing the API calls on behalf of a merchant, the partner must identify the merchant using either a PayPal-Auth-Assertion header or an access token with target_subject.</param>
    /// <param name="payPalRequestId">The server stores keys for 45 days.</param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentAuthorization"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="VoidPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Voids, or cancels, an authorized payment, by ID. You cannot void an authorized payment that has been fully captured.
    /// </remarks>
    public Task<PaymentAuthorization> VoidPayment(string authorizationId,
        string? payPalMockResponse,
        string? payPalAuthAssertion,
        string? payPalRequestId,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}/void"),
            [new TemplateParam("authorization_id", authorizationId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", payPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", payPalAuthAssertion),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Prefer", prefer),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<PaymentAuthorization>(),
            VoidPaymentErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);
}
