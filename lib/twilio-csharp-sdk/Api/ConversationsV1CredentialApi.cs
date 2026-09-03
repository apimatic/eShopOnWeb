using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class ConversationsV1CredentialApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1CredentialApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add a new push notification credential to your account
    /// </summary>
    /// <param name="type"></param>
    /// <param name="friendlyName"></param>
    /// <param name="certificate"></param>
    /// <param name="privateKey"></param>
    /// <param name="sandbox"></param>
    /// <param name="apiKey"></param>
    /// <param name="secret"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Credential"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new push notification credential to your account
    /// </remarks>
    public Task<ConversationsV1Credential> CreateCredential(CredentialEnumPushType type,
        string? friendlyName,
        string? certificate,
        string? privateKey,
        bool? sandbox,
        string? apiKey,
        string? secret,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Credentials"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Type", type),
                    new Param("FriendlyName", friendlyName),
                    new Param("Certificate", certificate),
                    new Param("PrivateKey", privateKey),
                    new Param("Sandbox", sandbox),
                    new Param("ApiKey", apiKey),
                    new Param("Secret", secret)]),
            JsonResponse.Create<ConversationsV1Credential>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove a push notification credential from your account
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a push notification credential from your account
    /// </remarks>
    public Task DeleteCredential(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Credentials/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a push notification credential from your account
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Credential"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a push notification credential from your account
    /// </remarks>
    public Task<ConversationsV1Credential> FetchCredential(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Credentials/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1Credential>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all push notification credentials on your account
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCredentialResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all push notification credentials on your account
    /// </remarks>
    public Task<ListCredentialResponse> ListCredential(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Credentials"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCredentialResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing push notification credential on your account
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="type"></param>
    /// <param name="friendlyName"></param>
    /// <param name="certificate"></param>
    /// <param name="privateKey"></param>
    /// <param name="sandbox"></param>
    /// <param name="apiKey"></param>
    /// <param name="secret"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Credential"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing push notification credential on your account
    /// </remarks>
    public Task<ConversationsV1Credential> UpdateCredential(string sid,
        CredentialEnumPushType? type,
        string? friendlyName,
        string? certificate,
        string? privateKey,
        bool? sandbox,
        string? apiKey,
        string? secret,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Credentials/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Type", type),
                    new Param("FriendlyName", friendlyName),
                    new Param("Certificate", certificate),
                    new Param("PrivateKey", privateKey),
                    new Param("Sandbox", sandbox),
                    new Param("ApiKey", apiKey),
                    new Param("Secret", secret)]),
            JsonResponse.Create<ConversationsV1Credential>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
