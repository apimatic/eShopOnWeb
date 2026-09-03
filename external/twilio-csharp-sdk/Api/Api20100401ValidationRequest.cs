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

public sealed class Api20100401ValidationRequest
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401ValidationRequest(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// An OutgoingCallerId resource represents a single verified number that may be used as a caller ID when making outgoing calls via the REST API and within the TwiML <c>&lt;Dial&gt;</c> verb.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for the new caller ID resource.</param>
    /// <param name="phoneNumber"></param>
    /// <param name="friendlyName"></param>
    /// <param name="callDelay"></param>
    /// <param name="extension"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountValidationRequest"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountValidationRequest> CreateValidationRequest(string accountSid,
        string phoneNumber,
        string? friendlyName,
        int? callDelay,
        string? extension,
        string? statusCallback,
        StatusCallbackMethod15? statusCallbackMethod,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/OutgoingCallerIds.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PhoneNumber", phoneNumber),
                    new Param("FriendlyName", friendlyName),
                    new Param("CallDelay", callDelay),
                    new Param("Extension", extension),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod)]),
            JsonResponse.Create<ApiV2010AccountValidationRequest>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
