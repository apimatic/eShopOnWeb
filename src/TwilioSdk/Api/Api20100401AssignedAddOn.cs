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

public sealed class Api20100401AssignedAddOn
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401AssignedAddOn(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Assign an Add-on installation to the Number specified.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="resourceSid">The SID of the Phone Number to assign the Add-on.</param>
    /// <param name="installedAddOnSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberAssignedAddOn"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Assign an Add-on installation to the Number specified.
    /// </remarks>
    public Task<ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberAssignedAddOn> CreateIncomingPhoneNumberAssignedAddOn(string accountSid,
        string resourceSid,
        string installedAddOnSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/{ResourceSid}/AssignedAddOns.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("ResourceSid", resourceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("InstalledAddOnSid", installedAddOnSid)]),
            JsonResponse.Create<ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberAssignedAddOn>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove the assignment of an Add-on installation from the Number specified.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the resources to delete.</param>
    /// <param name="resourceSid">The SID of the Phone Number to which the Add-on is assigned.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove the assignment of an Add-on installation from the Number specified.
    /// </remarks>
    public Task DeleteIncomingPhoneNumberAssignedAddOn(string accountSid,
        string resourceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/{ResourceSid}/AssignedAddOns/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ResourceSid", resourceSid),
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
    /// Fetch an instance of an Add-on installation currently assigned to this Number.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the resource to fetch.</param>
    /// <param name="resourceSid">The SID of the Phone Number to which the Add-on is assigned.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberAssignedAddOn"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of an Add-on installation currently assigned to this Number.
    /// </remarks>
    public Task<ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberAssignedAddOn> FetchIncomingPhoneNumberAssignedAddOn(string accountSid,
        string resourceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/{ResourceSid}/AssignedAddOns/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ResourceSid", resourceSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountIncomingPhoneNumberIncomingPhoneNumberAssignedAddOn>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of Add-on installations currently assigned to this Number.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the resources to read.</param>
    /// <param name="resourceSid">The SID of the Phone Number to which the Add-on is assigned.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListIncomingPhoneNumberAssignedAddOnResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Add-on installations currently assigned to this Number.
    /// </remarks>
    public Task<ListIncomingPhoneNumberAssignedAddOnResponse> ListIncomingPhoneNumberAssignedAddOn(string accountSid,
        string resourceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/{ResourceSid}/AssignedAddOns.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("ResourceSid", resourceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListIncomingPhoneNumberAssignedAddOnResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
