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

public sealed class Api20100401UserDefinedMessage
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401UserDefinedMessage(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new User Defined Message for the given Call SID.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created User Defined Message.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the User Defined Message is associated with.</param>
    /// <param name="content"></param>
    /// <param name="idempotencyKey"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallUserDefinedMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new User Defined Message for the given Call SID.
    /// </remarks>
    public Task<ApiV2010AccountCallUserDefinedMessage> CreateUserDefinedMessage(string accountSid,
        string callSid,
        string content,
        string? idempotencyKey,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/UserDefinedMessages.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Content", content),
                    new Param("IdempotencyKey", idempotencyKey)]),
            JsonResponse.Create<ApiV2010AccountCallUserDefinedMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
