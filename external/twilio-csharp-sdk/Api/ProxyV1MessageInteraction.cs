using System;
using System.Collections.Generic;
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

public sealed class ProxyV1MessageInteraction
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProxyV1MessageInteraction(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new message Interaction to send directly from your system to one <see href="https://www.twilio.com/docs/proxy/api/participant">Participant</see>.  The <c>inbound</c> properties for the Interaction will always be empty.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> resource.</param>
    /// <param name="sessionSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/session">Session</see> resource.</param>
    /// <param name="participantSid">The SID of the <see href="https://www.twilio.com/docs/proxy/api/participant">Participant</see> resource.</param>
    /// <param name="body"></param>
    /// <param name="mediaUrl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServiceSessionParticipantMessageInteraction"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new message Interaction to send directly from your system to one <see href="https://www.twilio.com/docs/proxy/api/participant">Participant</see>.  The <c>inbound</c> properties for the Interaction will always be empty.
    /// </remarks>
    public Task<ProxyV1ServiceSessionParticipantMessageInteraction> CreateMessageInteraction(string serviceSid,
        string sessionSid,
        string participantSid,
        string? body,
        IReadOnlyList<string>? mediaUrl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{SessionSid}/Participants/{ParticipantSid}/MessageInteractions"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("SessionSid", sessionSid),
                new TemplateParam("ParticipantSid", participantSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Body", body), new Param("MediaUrl", mediaUrl)]),
            JsonResponse.Create<ProxyV1ServiceSessionParticipantMessageInteraction>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ProxyV1ServiceSessionParticipantMessageInteraction> FetchMessageInteraction(string serviceSid,
        string sessionSid,
        string participantSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{SessionSid}/Participants/{ParticipantSid}/MessageInteractions/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("SessionSid", sessionSid),
                new TemplateParam("ParticipantSid", participantSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProxyV1ServiceSessionParticipantMessageInteraction>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListMessageInteractionResponse> ListMessageInteraction(string serviceSid,
        string sessionSid,
        string participantSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{SessionSid}/Participants/{ParticipantSid}/MessageInteractions"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("SessionSid", sessionSid),
                new TemplateParam("ParticipantSid", participantSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMessageInteractionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
