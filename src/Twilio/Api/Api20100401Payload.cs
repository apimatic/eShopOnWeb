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

public sealed class Api20100401Payload
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Payload(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a payload from the result along with all associated Data
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording AddOnResult Payload resources to delete.</param>
    /// <param name="referenceSid">The SID of the recording to which the AddOnResult resource that contains the payloads to delete belongs.</param>
    /// <param name="addOnResultSid">The SID of the AddOnResult to which the payloads to delete belongs.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Recording AddOnResult Payload resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a payload from the result along with all associated Data
    /// </remarks>
    public Task DeleteRecordingAddOnResultPayload(string accountSid,
        string referenceSid,
        string addOnResultSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Recordings/{ReferenceSid}/AddOnResults/{AddOnResultSid}/Payloads/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ReferenceSid", referenceSid),
                new TemplateParam("AddOnResultSid", addOnResultSid),
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
    /// Fetch an instance of a result payload
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording AddOnResult Payload resource to fetch.</param>
    /// <param name="referenceSid">The SID of the recording to which the AddOnResult resource that contains the payload to fetch belongs.</param>
    /// <param name="addOnResultSid">The SID of the AddOnResult to which the payload to fetch belongs.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Recording AddOnResult Payload resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountRecordingRecordingAddOnResultRecordingAddOnResultPayload"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a result payload
    /// </remarks>
    public Task<ApiV2010AccountRecordingRecordingAddOnResultRecordingAddOnResultPayload> FetchRecordingAddOnResultPayload(string accountSid,
        string referenceSid,
        string addOnResultSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Recordings/{ReferenceSid}/AddOnResults/{AddOnResultSid}/Payloads/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ReferenceSid", referenceSid),
                new TemplateParam("AddOnResultSid", addOnResultSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountRecordingRecordingAddOnResultRecordingAddOnResultPayload>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of payloads belonging to the AddOnResult
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording AddOnResult Payload resources to read.</param>
    /// <param name="referenceSid">The SID of the recording to which the AddOnResult resource that contains the payloads to read belongs.</param>
    /// <param name="addOnResultSid">The SID of the AddOnResult to which the payloads to read belongs.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRecordingAddOnResultPayloadResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of payloads belonging to the AddOnResult
    /// </remarks>
    public Task<ListRecordingAddOnResultPayloadResponse> ListRecordingAddOnResultPayload(string accountSid,
        string referenceSid,
        string addOnResultSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Recordings/{ReferenceSid}/AddOnResults/{AddOnResultSid}/Payloads.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ReferenceSid", referenceSid),
                new TemplateParam("AddOnResultSid", addOnResultSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRecordingAddOnResultPayloadResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
