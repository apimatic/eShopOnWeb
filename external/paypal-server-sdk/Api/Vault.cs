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
/// Use the <c>/vault</c> resource to create, retrieve, and delete payment and setup tokens.
/// </summary>
public sealed class Vault
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Vault(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create payment token for a given payment source
    /// </summary>
    /// <param name="payPalRequestId">The server stores keys for 3 hours.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentTokenResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreatePaymentTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a Payment Token from the given payment source and adds it to the Vault of the associated customer.
    /// </remarks>
    public Task<PaymentTokenResponse> CreatePaymentToken(string? payPalRequestId,
        PaymentTokenRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens"),
            [],
            [],
            [new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<PaymentTokenResponse>(),
            CreatePaymentTokenErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Create a setup token
    /// </summary>
    /// <param name="payPalRequestId">The server stores keys for 3 hours.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SetupTokenResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSetupTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a Setup Token from the given payment source and adds it to the Vault of the associated customer.
    /// </remarks>
    public Task<SetupTokenResponse> CreateSetupToken(string? payPalRequestId,
        SetupTokenRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/setup-tokens"),
            [],
            [],
            [new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SetupTokenResponse>(),
            CreateSetupTokenErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Delete payment token
    /// </summary>
    /// <param name="id">ID of the payment token.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeletePaymentTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete the payment token associated with the payment token id.
    /// </remarks>
    public Task DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens/{id}"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeletePaymentTokenErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a payment token
    /// </summary>
    /// <param name="id">ID of the payment token.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentTokenResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetPaymentTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a readable representation of vaulted payment source associated with the payment token id.
    /// </remarks>
    public Task<PaymentTokenResponse> GetPaymentToken(string id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens/{id}"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PaymentTokenResponse>(),
            GetPaymentTokenErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a setup token
    /// </summary>
    /// <param name="id">ID of the setup token.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SetupTokenResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetSetupTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a readable representation of temporarily vaulted payment source associated with the setup token id.
    /// </remarks>
    public Task<SetupTokenResponse> GetSetupToken(string id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/setup-tokens/{id}"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SetupTokenResponse>(),
            GetSetupTokenErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// List all payment tokens
    /// </summary>
    /// <param name="customerId">A unique identifier representing a specific customer in merchant's/partner's system or records.</param>
    /// <param name="pageSize">A non-negative, non-zero integer indicating the maximum number of results to return at one time.</param>
    /// <param name="page">A non-negative, non-zero integer representing the page of the results.</param>
    /// <param name="totalRequired">A boolean indicating total number of items (total_items) and pages (total_pages) are expected to be returned in the response.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CustomerVaultPaymentTokensResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListCustomerPaymentTokensError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns all payment tokens for a customer.
    /// </remarks>
    public Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(string customerId,
        int? pageSize = 5,
        int? page = 1,
        bool? totalRequired = false,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens"),
            [],
            [new Param("customer_id", customerId),
                new Param("page_size", pageSize),
                new Param("page", page),
                new Param("total_required", totalRequired)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CustomerVaultPaymentTokensResponse>(),
            ListCustomerPaymentTokensErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);
}
