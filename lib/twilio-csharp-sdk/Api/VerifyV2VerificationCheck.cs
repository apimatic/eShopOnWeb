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

public sealed class VerifyV2VerificationCheck
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2VerificationCheck(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// challenge a specific Verification Check.
    /// </summary>
    /// <param name="serviceSid">The SID of the verification <see href="https://www.twilio.com/docs/verify/api/service">Service</see> to create the resource under.</param>
    /// <param name="code"></param>
    /// <param name="to"></param>
    /// <param name="verificationSid"></param>
    /// <param name="amount"></param>
    /// <param name="payee"></param>
    /// <param name="snaClientToken"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceVerificationCheck"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// challenge a specific Verification Check.
    /// </remarks>
    public Task<VerifyV2ServiceVerificationCheck> CreateVerificationCheck(string serviceSid,
        string? code,
        string? to,
        string? verificationSid,
        string? amount,
        string? payee,
        string? snaClientToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/VerificationCheck"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Code", code),
                    new Param("To", to),
                    new Param("VerificationSid", verificationSid),
                    new Param("Amount", amount),
                    new Param("Payee", payee),
                    new Param("SnaClientToken", snaClientToken)]),
            JsonResponse.Create<VerifyV2ServiceVerificationCheck>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
