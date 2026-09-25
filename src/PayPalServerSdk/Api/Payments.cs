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
using PayPalServerSdk.Requests.Payments;

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
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CapturedPayment"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CaptureAuthorizedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Captures an authorized payment, by ID.
    /// </remarks>
    public Task<CapturedPayment> CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}/capture"),
            [new TemplateParam("authorization_id", request.AuthorizationId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<CapturedPayment>(),
            CaptureAuthorizedPaymentError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Show details for authorized payment
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentAuthorization"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetAuthorizedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for an authorized payment, by ID.
    /// </remarks>
    public Task<PaymentAuthorization> GetAuthorizedPayment(GetAuthorizedPaymentRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}"),
            [new TemplateParam("authorization_id", request.AuthorizationId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PaymentAuthorization>(),
            GetAuthorizedPaymentError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Show captured payment details
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CapturedPayment"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetCapturedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a captured payment, by ID.
    /// </remarks>
    public Task<CapturedPayment> GetCapturedPayment(GetCapturedPaymentRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/captures/{capture_id}"),
            [new TemplateParam("capture_id", request.CaptureId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CapturedPayment>(),
            GetCapturedPaymentError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Show refund details
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Refund"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetRefundError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a refund, by ID.
    /// </remarks>
    public Task<Refund> GetRefund(GetRefundRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/refunds/{refund_id}"),
            [new TemplateParam("refund_id", request.RefundId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<Refund>(),
            GetRefundError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Reauthorize authorized payment
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentAuthorization"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ReauthorizePaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Reauthorizes an authorized PayPal account payment, by ID. To ensure that funds are still available, reauthorize a payment after its initial three-day honor period expires. Within the 29-day authorization period, you can issue multiple re-authorizations after the honor period expires. If 30 days have transpired since the date of the original authorization, you must create an authorized payment instead of reauthorizing the original authorized payment. A reauthorized payment itself has a new honor period of three days. You can reauthorize an authorized payment from 4 to 29 days after the 3-day honor period. The allowed amount depends on context and geography, for example in US it is up to 115% of the original authorized amount, not to exceed an increase of $75 USD. Supports only the <c>amount</c> request parameter.
    /// </remarks>
    public Task<PaymentAuthorization> ReauthorizePayment(ReauthorizePaymentRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}/reauthorize"),
            [new TemplateParam("authorization_id", request.AuthorizationId)],
            [],
            [new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<PaymentAuthorization>(),
            ReauthorizePaymentError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Refund captured payment
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Refund"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="RefundCapturedPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Refunds a captured payment, by ID. For a full refund, include an empty payload in the JSON request body. For a partial refund, include an amount object in the JSON request body.
    /// </remarks>
    public Task<Refund> RefundCapturedPayment(RefundCapturedPaymentRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/captures/{capture_id}/refund"),
            [new TemplateParam("capture_id", request.CaptureId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<Refund>(),
            RefundCapturedPaymentError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Void authorized payment
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentAuthorization"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="VoidPaymentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Voids, or cancels, an authorized payment, by ID. You cannot void an authorized payment that has been fully captured.
    /// </remarks>
    public Task<PaymentAuthorization> VoidPayment(VoidPaymentRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v2/payments/authorizations/{authorization_id}/void"),
            [new TemplateParam("authorization_id", request.AuthorizationId)],
            [],
            [new HeaderParam("PayPal-Mock-Response", request.PayPalMockResponse),
                new HeaderParam("PayPal-Auth-Assertion", request.PayPalAuthAssertion),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<PaymentAuthorization>(),
            VoidPaymentError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);
}
