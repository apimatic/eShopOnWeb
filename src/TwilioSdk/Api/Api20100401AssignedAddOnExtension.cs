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

public sealed class Api20100401AssignedAddOnExtension
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401AssignedAddOnExtension(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch an instance of an Extension for the Assigned Add-on.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the resource to fetch.</param>
    /// <param name="resourceSid">The SID of the Phone Number to which the Add-on is assigned.</param>
    /// <param name="assignedAddOnSid">The SID that uniquely identifies the assigned Add-on installation.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IncomingPhoneNumberAssignedAddOnExtension"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of an Extension for the Assigned Add-on.
    /// </remarks>
    public Task<IncomingPhoneNumberAssignedAddOnExtension> FetchIncomingPhoneNumberAssignedAddOnExtension(string accountSid,
        string resourceSid,
        string assignedAddOnSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/{ResourceSid}/AssignedAddOns/{AssignedAddOnSid}/Extensions/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ResourceSid", resourceSid),
                new TemplateParam("AssignedAddOnSid", assignedAddOnSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IncomingPhoneNumberAssignedAddOnExtension>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of Extensions for the Assigned Add-on.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the resources to read.</param>
    /// <param name="resourceSid">The SID of the Phone Number to which the Add-on is assigned.</param>
    /// <param name="assignedAddOnSid">The SID that uniquely identifies the assigned Add-on installation.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListIncomingPhoneNumberAssignedAddOnExtensionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Extensions for the Assigned Add-on.
    /// </remarks>
    public Task<ListIncomingPhoneNumberAssignedAddOnExtensionResponse> ListIncomingPhoneNumberAssignedAddOnExtension(string accountSid,
        string resourceSid,
        string assignedAddOnSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/IncomingPhoneNumbers/{ResourceSid}/AssignedAddOns/{AssignedAddOnSid}/Extensions.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ResourceSid", resourceSid),
                new TemplateParam("AssignedAddOnSid", assignedAddOnSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListIncomingPhoneNumberAssignedAddOnExtensionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
