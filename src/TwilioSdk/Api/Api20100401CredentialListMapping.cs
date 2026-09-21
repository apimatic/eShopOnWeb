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

public sealed class Api20100401CredentialListMapping
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401CredentialListMapping(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a CredentialListMapping resource for an account.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="domainSid">A 34 character string that uniquely identifies the SIP Domain for which the CredentialList resource will be mapped.</param>
    /// <param name="credentialListSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipDomainSipCredentialListMapping"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a CredentialListMapping resource for an account.
    /// </remarks>
    public Task<ApiV2010AccountSipSipDomainSipCredentialListMapping> CreateSipCredentialListMapping(string accountSid,
        string domainSid,
        string credentialListSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains/{DomainSid}/CredentialListMappings.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("DomainSid", domainSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("CredentialListSid", credentialListSid)]),
            JsonResponse.Create<ApiV2010AccountSipSipDomainSipCredentialListMapping>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a CredentialListMapping resource from an account.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="domainSid">A 34 character string that uniquely identifies the SIP Domain that includes the resource to delete.</param>
    /// <param name="sid">A 34 character string that uniquely identifies the resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a CredentialListMapping resource from an account.
    /// </remarks>
    public Task DeleteSipCredentialListMapping(string accountSid,
        string domainSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains/{DomainSid}/CredentialListMappings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("DomainSid", domainSid),
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
    /// Fetch a single CredentialListMapping resource from an account.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="domainSid">A 34 character string that uniquely identifies the SIP Domain that includes the resource to fetch.</param>
    /// <param name="sid">A 34 character string that uniquely identifies the resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipDomainSipCredentialListMapping"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a single CredentialListMapping resource from an account.
    /// </remarks>
    public Task<ApiV2010AccountSipSipDomainSipCredentialListMapping> FetchSipCredentialListMapping(string accountSid,
        string domainSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains/{DomainSid}/CredentialListMappings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("DomainSid", domainSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountSipSipDomainSipCredentialListMapping>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Read multiple CredentialListMapping resources from an account.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="domainSid">A 34 character string that uniquely identifies the SIP Domain that includes the resource to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSipCredentialListMappingResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Read multiple CredentialListMapping resources from an account.
    /// </remarks>
    public Task<ListSipCredentialListMappingResponse> ListSipCredentialListMapping(string accountSid,
        string domainSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/Domains/{DomainSid}/CredentialListMappings.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("DomainSid", domainSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSipCredentialListMappingResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
