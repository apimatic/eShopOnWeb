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

namespace Twilio.Api;

public sealed class Api20100401SipIpAddress
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401SipIpAddress(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new IpAddress resource.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="ipAccessControlListSid">The IpAccessControlList Sid with which to associate the created IpAddress resource.</param>
    /// <param name="friendlyName"></param>
    /// <param name="ipAddress"></param>
    /// <param name="cidrPrefixLength"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipIpAccessControlListSipIpAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new IpAddress resource.
    /// </remarks>
    public Task<ApiV2010AccountSipSipIpAccessControlListSipIpAddress> CreateSipIpAddress(string accountSid,
        string ipAccessControlListSid,
        string friendlyName,
        string ipAddress,
        int? cidrPrefixLength,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/IpAccessControlLists/{IpAccessControlListSid}/IpAddresses.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("IpAccessControlListSid", ipAccessControlListSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("IpAddress", ipAddress),
                    new Param("CidrPrefixLength", cidrPrefixLength)]),
            JsonResponse.Create<ApiV2010AccountSipSipIpAccessControlListSipIpAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete an IpAddress resource.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="ipAccessControlListSid">The IpAccessControlList Sid that identifies the IpAddress resources to delete.</param>
    /// <param name="sid">A 34 character string that uniquely identifies the resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete an IpAddress resource.
    /// </remarks>
    public Task DeleteSipIpAddress(string accountSid,
        string ipAccessControlListSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/IpAccessControlLists/{IpAccessControlListSid}/IpAddresses/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("IpAccessControlListSid", ipAccessControlListSid),
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
    /// Read one IpAddress resource.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="ipAccessControlListSid">The IpAccessControlList Sid that identifies the IpAddress resources to fetch.</param>
    /// <param name="sid">A 34 character string that uniquely identifies the IpAddress resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipIpAccessControlListSipIpAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Read one IpAddress resource.
    /// </remarks>
    public Task<ApiV2010AccountSipSipIpAccessControlListSipIpAddress> FetchSipIpAddress(string accountSid,
        string ipAccessControlListSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/IpAccessControlLists/{IpAccessControlListSid}/IpAddresses/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("IpAccessControlListSid", ipAccessControlListSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountSipSipIpAccessControlListSipIpAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Read multiple IpAddress resources.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="ipAccessControlListSid">The IpAccessControlList Sid that identifies the IpAddress resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSipIpAddressResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Read multiple IpAddress resources.
    /// </remarks>
    public Task<ListSipIpAddressResponse> ListSipIpAddress(string accountSid,
        string ipAccessControlListSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/IpAccessControlLists/{IpAccessControlListSid}/IpAddresses.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("IpAccessControlListSid", ipAccessControlListSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSipIpAddressResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an IpAddress resource.
    /// </summary>
    /// <param name="accountSid">The unique id of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this resource.</param>
    /// <param name="ipAccessControlListSid">The IpAccessControlList Sid that identifies the IpAddress resources to update.</param>
    /// <param name="sid">A 34 character string that identifies the IpAddress resource to update.</param>
    /// <param name="ipAddress"></param>
    /// <param name="friendlyName"></param>
    /// <param name="cidrPrefixLength"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountSipSipIpAccessControlListSipIpAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an IpAddress resource.
    /// </remarks>
    public Task<ApiV2010AccountSipSipIpAccessControlListSipIpAddress> UpdateSipIpAddress(string accountSid,
        string ipAccessControlListSid,
        string sid,
        string? ipAddress,
        string? friendlyName,
        int? cidrPrefixLength,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/SIP/IpAccessControlLists/{IpAccessControlListSid}/IpAddresses/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("IpAccessControlListSid", ipAccessControlListSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("IpAddress", ipAddress),
                    new Param("FriendlyName", friendlyName),
                    new Param("CidrPrefixLength", cidrPrefixLength)]),
            JsonResponse.Create<ApiV2010AccountSipSipIpAccessControlListSipIpAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
