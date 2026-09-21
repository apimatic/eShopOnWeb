using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class Api20100401Account
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Account(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Twilio Subaccount from the account making the request
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010Account"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Twilio Subaccount from the account making the request
    /// </remarks>
    public Task<ApiV2010Account> CreateAccount(string? friendlyName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName)]),
            JsonResponse.Create<ApiV2010Account>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch the account specified by the provided Account Sid
    /// </summary>
    /// <param name="sid">The Account Sid that uniquely identifies the account to fetch</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010Account"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the account specified by the provided Account Sid
    /// </remarks>
    public Task<ApiV2010Account> FetchAccount(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{Sid}.json"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010Account>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieves a collection of Accounts belonging to the account used to make the request
    /// </summary>
    /// <param name="friendlyName">Only return the Account resources with friendly names that exactly match this name.</param>
    /// <param name="status">Only return Account resources with the given status. Can be <c>closed</c>, <c>suspended</c> or <c>active</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListAccountResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a collection of Accounts belonging to the account used to make the request
    /// </remarks>
    public Task<ListAccountResponse> ListAccount(string? friendlyName,
        AccountEnumStatus? status,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts.json"),
            [],
            [new Param("FriendlyName", friendlyName),
                new Param("Status", status),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListAccountResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Modify the properties of a given Account
    /// </summary>
    /// <param name="sid">The Account Sid that uniquely identifies the account to update</param>
    /// <param name="friendlyName"></param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010Account"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Modify the properties of a given Account
    /// </remarks>
    public Task<ApiV2010Account> UpdateAccount(string sid,
        string? friendlyName,
        AccountEnumStatus? status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{Sid}.json"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName), new Param("Status", status)]),
            JsonResponse.Create<ApiV2010Account>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
