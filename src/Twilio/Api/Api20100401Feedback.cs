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

public sealed class Api20100401Feedback
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Feedback(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Message Feedback to confirm a tracked user action was performed by the recipient of the associated Message
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> associated with the Message resource for which to create MessageFeedback.</param>
    /// <param name="messageSid">The SID of the Message resource for which to create MessageFeedback.</param>
    /// <param name="outcome"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountMessageMessageFeedback"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create Message Feedback to confirm a tracked user action was performed by the recipient of the associated Message
    /// </remarks>
    public Task<ApiV2010AccountMessageMessageFeedback> CreateMessageFeedback(string accountSid,
        string messageSid,
        MessageFeedbackEnumOutcome? outcome,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages/{MessageSid}/Feedback.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("MessageSid", messageSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Outcome", outcome)]),
            JsonResponse.Create<ApiV2010AccountMessageMessageFeedback>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
