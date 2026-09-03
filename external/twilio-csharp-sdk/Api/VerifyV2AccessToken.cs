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

public sealed class VerifyV2AccessToken
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2AccessToken(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new enrollment Access Token for the Entity
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="identity"></param>
    /// <param name="factorType"></param>
    /// <param name="factorFriendlyName"></param>
    /// <param name="ttl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceAccessToken"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new enrollment Access Token for the Entity
    /// </remarks>
    public Task<VerifyV2ServiceAccessToken> CreateAccessToken(string serviceSid,
        string identity,
        AccessTokenEnumFactorTypes factorType,
        string? factorFriendlyName,
        int? ttl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/AccessTokens"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Identity", identity),
                    new Param("FactorType", factorType),
                    new Param("FactorFriendlyName", factorFriendlyName),
                    new Param("Ttl", ttl)]),
            JsonResponse.Create<VerifyV2ServiceAccessToken>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch an Access Token for the Entity
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this Access Token.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceAccessToken"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an Access Token for the Entity
    /// </remarks>
    public Task<VerifyV2ServiceAccessToken> FetchAccessToken(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/AccessTokens/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2ServiceAccessToken>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
