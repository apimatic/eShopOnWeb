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
using Twilio.Errors;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class VerifyV2Verification
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2Verification(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Verification using a Service
    /// </summary>
    /// <param name="serviceSid">The SID of the verification <see href="https://www.twilio.com/docs/verify/api/service">Service</see> to create the resource under.</param>
    /// <param name="to"></param>
    /// <param name="channel"></param>
    /// <param name="customFriendlyName"></param>
    /// <param name="customMessage"></param>
    /// <param name="sendDigits"></param>
    /// <param name="locale"></param>
    /// <param name="customCode"></param>
    /// <param name="amount"></param>
    /// <param name="payee"></param>
    /// <param name="rateLimits"></param>
    /// <param name="channelConfiguration"></param>
    /// <param name="appHash"></param>
    /// <param name="templateSid"></param>
    /// <param name="templateCustomSubstitutions"></param>
    /// <param name="deviceIp"></param>
    /// <param name="enableSnaClientToken"></param>
    /// <param name="riskCheck"></param>
    /// <param name="tags"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceVerification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateVerificationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Verification using a Service
    /// </remarks>
    public Task<VerifyV2ServiceVerification> CreateVerification(string serviceSid,
        string to,
        string channel,
        string? customFriendlyName,
        string? customMessage,
        string? sendDigits,
        string? locale,
        string? customCode,
        string? amount,
        string? payee,
        object? rateLimits,
        object? channelConfiguration,
        string? appHash,
        string? templateSid,
        string? templateCustomSubstitutions,
        string? deviceIp,
        bool? enableSnaClientToken,
        MessageEnumRiskCheck? riskCheck,
        string? tags,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Verifications"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("To", to),
                    new Param("Channel", channel),
                    new Param("CustomFriendlyName", customFriendlyName),
                    new Param("CustomMessage", customMessage),
                    new Param("SendDigits", sendDigits),
                    new Param("Locale", locale),
                    new Param("CustomCode", customCode),
                    new Param("Amount", amount),
                    new Param("Payee", payee),
                    new Param("RateLimits", rateLimits),
                    new Param("ChannelConfiguration", channelConfiguration),
                    new Param("AppHash", appHash),
                    new Param("TemplateSid", templateSid),
                    new Param("TemplateCustomSubstitutions", templateCustomSubstitutions),
                    new Param("DeviceIp", deviceIp),
                    new Param("EnableSnaClientToken", enableSnaClientToken),
                    new Param("RiskCheck", riskCheck),
                    new Param("Tags", tags)]),
            JsonResponse.Create<VerifyV2ServiceVerification>(),
            CreateVerificationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a specific Verification
    /// </summary>
    /// <param name="serviceSid">The SID of the verification <see href="https://www.twilio.com/docs/verify/api/service">Service</see> to fetch the resource from.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Verification resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceVerification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Verification
    /// </remarks>
    public Task<VerifyV2ServiceVerification> FetchVerification(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Verifications/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2ServiceVerification>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a Verification status
    /// </summary>
    /// <param name="serviceSid">The SID of the verification <see href="https://www.twilio.com/docs/verify/api/service">Service</see> to update the resource from.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Verification resource to update.</param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceVerification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a Verification status
    /// </remarks>
    public Task<VerifyV2ServiceVerification> UpdateVerification(string serviceSid,
        string sid,
        VerificationEnumStatus status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Verifications/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status)]),
            JsonResponse.Create<VerifyV2ServiceVerification>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
