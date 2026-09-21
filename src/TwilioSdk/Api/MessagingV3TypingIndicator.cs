using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Errors;
using TwilioSdk.Models;
using TwilioSdk.Models.OneOf;

namespace TwilioSdk.Api;

/// <summary>
/// Send typing indicators to OTT channel recipients (WhatsApp, Apple Messages for Business).
/// </summary>
public sealed class MessagingV3TypingIndicator
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV3TypingIndicator(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Send a typing indicator
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="V2IndicatorsTypingJsonResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateV3TypingIndicatorError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Send a typing indicator to notify the recipient that you are composing a message. Supported channels: WhatsApp, Apple Messages for Business. The request body varies by channel — use the <c>channel</c> field as the discriminator.
    /// </remarks>
    public Task<V2IndicatorsTypingJsonResponse> CreateV3TypingIndicator(TypingIndicatorRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v3/Indicators/Typing.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<V2IndicatorsTypingJsonResponse>(),
            CreateV3TypingIndicatorErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
