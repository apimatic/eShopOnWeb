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

namespace TwilioSdk.Api;

public sealed class Api20100401Credential
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Credential(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new credential resource.
    /// </summary>
    /// <param name="accountSid">The unique id of the Account that is responsible for this resource.</param>
    /// <param name="credentialListSid">The unique id that identifies the credential list to include the created credential.</param>
    /// <param name="username"></param>
    /// <param name="password"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipCredentialListSipCredential"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new credential resource.
    /// </remarks>
    public Task<ApiV2010AccountSipSipCredentialListSipCredential> CreateSipCredential(string accountSid,
        string credentialListSid,
        string username,
        string password,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/CredentialLists/{CredentialListSid}/Credentials.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CredentialListSid", credentialListSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Username", username), new Param("Password", password)]),
            JsonResponse.Create<ApiV2010AccountSipSipCredentialListSipCredential>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a credential resource.
    /// </summary>
    /// <param name="accountSid">The unique id of the Account that is responsible for this resource.</param>
    /// <param name="credentialListSid">The unique id that identifies the credential list that contains the desired credentials.</param>
    /// <param name="sid">The unique id that identifies the resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a credential resource.
    /// </remarks>
    public Task DeleteSipCredential(string accountSid,
        string credentialListSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/CredentialLists/{CredentialListSid}/Credentials/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CredentialListSid", credentialListSid),
                new TemplateParam("Sid", sid)],
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
    /// Fetch a single credential.
    /// </summary>
    /// <param name="accountSid">The unique id of the Account that is responsible for this resource.</param>
    /// <param name="credentialListSid">The unique id that identifies the credential list that contains the desired credential.</param>
    /// <param name="sid">The unique id that identifies the resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipCredentialListSipCredential"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a single credential.
    /// </remarks>
    public Task<ApiV2010AccountSipSipCredentialListSipCredential> FetchSipCredential(string accountSid,
        string credentialListSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/CredentialLists/{CredentialListSid}/Credentials/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CredentialListSid", credentialListSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountSipSipCredentialListSipCredential>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of credentials.
    /// </summary>
    /// <param name="accountSid">The unique id of the Account that is responsible for this resource.</param>
    /// <param name="credentialListSid">The unique id that identifies the credential list that contains the desired credentials.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSipCredentialResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of credentials.
    /// </remarks>
    public Task<ListSipCredentialResponse> ListSipCredential(string accountSid,
        string credentialListSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/CredentialLists/{CredentialListSid}/Credentials.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CredentialListSid", credentialListSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSipCredentialResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a credential resource.
    /// </summary>
    /// <param name="accountSid">The unique id of the Account that is responsible for this resource.</param>
    /// <param name="credentialListSid">The unique id that identifies the credential list that includes this credential.</param>
    /// <param name="sid">The unique id that identifies the resource to update.</param>
    /// <param name="password"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipCredentialListSipCredential"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a credential resource.
    /// </remarks>
    public Task<ApiV2010AccountSipSipCredentialListSipCredential> UpdateSipCredential(string accountSid,
        string credentialListSid,
        string sid,
        string? password,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/CredentialLists/{CredentialListSid}/Credentials/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CredentialListSid", credentialListSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Password", password)]),
            JsonResponse.Create<ApiV2010AccountSipSipCredentialListSipCredential>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
