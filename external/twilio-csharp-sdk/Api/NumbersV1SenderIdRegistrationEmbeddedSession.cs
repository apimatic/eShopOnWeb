using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Errors;
using Twilio.Models;

namespace Twilio.Api;

public sealed class NumbersV1SenderIdRegistrationEmbeddedSession
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV1SenderIdRegistrationEmbeddedSession(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Embedded Session
    /// </summary>
    /// <param name="bundleSid">The unique identifier of the registration (BU-prefixed).</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV1CreateEmbeddedSessionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSenderIdRegistrationEmbeddedSessionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a new embedded Persona inquiry session for an existing registration in DRAFT or TWILIO_REJECTED status. Use this to resume an incomplete registration or resubmit a rejected one.
    /// </remarks>
    public Task<NumbersV1CreateEmbeddedSessionResponse> CreateSenderIdRegistrationEmbeddedSession(string bundleSid,
        NumbersV1CreateEmbeddedSessionRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/SenderIdRegistrations/{BundleSid}/EmbeddedSessions"),
            [new TemplateParam("BundleSid", bundleSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<NumbersV1CreateEmbeddedSessionResponse>(),
            CreateSenderIdRegistrationEmbeddedSessionErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
