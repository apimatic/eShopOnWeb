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

public sealed class Api20100401UserDefinedMessageSubscription
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401UserDefinedMessageSubscription(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Subscribe to User Defined Messages for a given Call SID.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that subscribed to the User Defined Messages.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the User Defined Messages subscription is associated with. This refers to the Call SID that is producing the user defined messages.</param>
    /// <param name="callback"></param>
    /// <param name="idempotencyKey"></param>
    /// <param name="method"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallUserDefinedMessageSubscription"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Subscribe to User Defined Messages for a given Call SID.
    /// </remarks>
    public Task<ApiV2010AccountCallUserDefinedMessageSubscription> CreateUserDefinedMessageSubscription(string accountSid,
        string callSid,
        string callback,
        string? idempotencyKey,
        Method3? method,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/UserDefinedMessageSubscriptions.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Callback", callback),
                    new Param("IdempotencyKey", idempotencyKey),
                    new Param("Method", method)]),
            JsonResponse.Create<ApiV2010AccountCallUserDefinedMessageSubscription>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific User Defined Message Subscription.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that subscribed to the User Defined Messages.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the User Defined Message Subscription is associated with. This refers to the Call SID that is producing the User Defined Messages.</param>
    /// <param name="sid">The SID that uniquely identifies this User Defined Message Subscription.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific User Defined Message Subscription.
    /// </remarks>
    public Task DeleteUserDefinedMessageSubscription(string accountSid,
        string callSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/UserDefinedMessageSubscriptions/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
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
}
