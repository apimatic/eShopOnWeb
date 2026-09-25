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
using PayPalServerSdk.Requests.Vault;

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
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentTokenResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CreatePaymentTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a Payment Token from the given payment source and adds it to the Vault of the associated customer.
    /// </remarks>
    public Task<PaymentTokenResponse> CreatePaymentToken(CreatePaymentTokenRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens"),
            [],
            [],
            [new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<PaymentTokenResponse>(),
            CreatePaymentTokenError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Create a setup token
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SetupTokenResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CreateSetupTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a Setup Token from the given payment source and adds it to the Vault of the associated customer.
    /// </remarks>
    public Task<SetupTokenResponse> CreateSetupToken(CreateSetupTokenRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/setup-tokens"),
            [],
            [],
            [new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<SetupTokenResponse>(),
            CreateSetupTokenError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Delete payment token
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="DeletePaymentTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete the payment token associated with the payment token id.
    /// </remarks>
    public Task DeletePaymentToken(DeletePaymentTokenRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens/{id}"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeletePaymentTokenError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Retrieve a payment token
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaymentTokenResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetPaymentTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a readable representation of vaulted payment source associated with the payment token id.
    /// </remarks>
    public Task<PaymentTokenResponse> GetPaymentToken(GetPaymentTokenRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens/{id}"),
            [new TemplateParam("id", request.Id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PaymentTokenResponse>(),
            GetPaymentTokenError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Retrieve a setup token
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SetupTokenResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetSetupTokenError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a readable representation of temporarily vaulted payment source associated with the setup token id.
    /// </remarks>
    public Task<SetupTokenResponse> GetSetupToken(GetSetupTokenRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/setup-tokens/{id}"),
            [new TemplateParam("id", request.Id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SetupTokenResponse>(),
            GetSetupTokenError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// List all payment tokens
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CustomerVaultPaymentTokensResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ListCustomerPaymentTokensError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns all payment tokens for a customer.
    /// </remarks>
    public Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(ListCustomerPaymentTokensRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v3/vault/payment-tokens"),
            [],
            [new Param("customer_id", request.CustomerId),
                new Param("page_size", request.PageSize),
                new Param("page", request.Page),
                new Param("total_required", request.TotalRequired)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CustomerVaultPaymentTokensResponse>(),
            ListCustomerPaymentTokensError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);
}
