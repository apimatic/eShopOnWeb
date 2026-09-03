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

public sealed class Api20100401Member
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Member(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch a specific member from the queue
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Member resource(s) to fetch.</param>
    /// <param name="queueSid">The SID of the Queue in which to find the members to fetch.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the resource(s) to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountQueueMember"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific member from the queue
    /// </remarks>
    public Task<ApiV2010AccountQueueMember> FetchMember(string accountSid,
        string queueSid,
        string callSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Queues/{QueueSid}/Members/{CallSid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("QueueSid", queueSid),
                new TemplateParam("CallSid", callSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountQueueMember>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve the members of the queue
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Member resource(s) to read.</param>
    /// <param name="queueSid">The SID of the Queue in which to find the members</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListMemberResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve the members of the queue
    /// </remarks>
    public Task<ListMemberResponse> ListMember(string accountSid,
        string queueSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Queues/{QueueSid}/Members.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("QueueSid", queueSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMemberResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Dequeue a member from a queue and have the member's call begin executing the TwiML document at that URL
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Member resource(s) to update.</param>
    /// <param name="queueSid">The SID of the Queue in which to find the members to update.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the resource(s) to update.</param>
    /// <param name="url"></param>
    /// <param name="method"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountQueueMember"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Dequeue a member from a queue and have the member's call begin executing the TwiML document at that URL
    /// </remarks>
    public Task<ApiV2010AccountQueueMember> UpdateMember(string accountSid,
        string queueSid,
        string callSid,
        string url,
        Method2? method,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Queues/{QueueSid}/Members/{CallSid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("QueueSid", queueSid),
                new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Url", url), new Param("Method", method)]),
            JsonResponse.Create<ApiV2010AccountQueueMember>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
