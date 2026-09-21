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

public sealed class SyncV1StreamMessage
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SyncV1StreamMessage(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Stream Message.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> to create the new Stream Message in.</param>
    /// <param name="streamSid">The SID of the Sync Stream to create the new Stream Message resource for.</param>
    /// <param name="data"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncStreamStreamMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Stream Message.
    /// </remarks>
    public Task<SyncV1ServiceSyncStreamStreamMessage> CreateStreamMessage(string serviceSid,
        string streamSid,
        object data,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Streams/{StreamSid}/Messages"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("StreamSid", streamSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Data", data)]),
            JsonResponse.Create<SyncV1ServiceSyncStreamStreamMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
