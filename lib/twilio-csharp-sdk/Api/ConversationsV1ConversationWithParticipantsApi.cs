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
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class ConversationsV1ConversationWithParticipantsApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1ConversationWithParticipantsApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new conversation with the list of participants in your account's default service
    /// </summary>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="uniqueName"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="attributes"></param>
    /// <param name="state"></param>
    /// <param name="timersInactive"></param>
    /// <param name="timersClosed"></param>
    /// <param name="bindingsEmailAddress"></param>
    /// <param name="bindingsEmailName"></param>
    /// <param name="participant"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationWithParticipants"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new conversation with the list of participants in your account's default service
    /// </remarks>
    public Task<ConversationsV1ConversationWithParticipants> CreateConversationWithParticipants(Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        string? uniqueName,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? messagingServiceSid,
        string? attributes,
        ConversationWithParticipantsEnumState? state,
        string? timersInactive,
        string? timersClosed,
        string? bindingsEmailAddress,
        string? bindingsEmailName,
        IReadOnlyList<string>? participant,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/ConversationWithParticipants"),
            [],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("UniqueName", uniqueName),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("MessagingServiceSid", messagingServiceSid),
                    new Param("Attributes", attributes),
                    new Param("State", state),
                    new Param("Timers.Inactive", timersInactive),
                    new Param("Timers.Closed", timersClosed),
                    new Param("Bindings.Email.Address", bindingsEmailAddress),
                    new Param("Bindings.Email.Name", bindingsEmailName),
                    new Param("Participant", participant)]),
            JsonResponse.Create<ConversationsV1ConversationWithParticipants>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Create a new conversation with the list of participants in your account's default service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="uniqueName"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="attributes"></param>
    /// <param name="state"></param>
    /// <param name="timersInactive"></param>
    /// <param name="timersClosed"></param>
    /// <param name="bindingsEmailAddress"></param>
    /// <param name="bindingsEmailName"></param>
    /// <param name="participant"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationWithParticipants"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new conversation with the list of participants in your account's default service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationWithParticipants> CreateServiceConversationWithParticipants(string chatServiceSid,
        Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        string? uniqueName,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? messagingServiceSid,
        string? attributes,
        ServiceConversationWithParticipantsEnumState? state,
        string? timersInactive,
        string? timersClosed,
        string? bindingsEmailAddress,
        string? bindingsEmailName,
        IReadOnlyList<string>? participant,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/ConversationWithParticipants"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("UniqueName", uniqueName),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("MessagingServiceSid", messagingServiceSid),
                    new Param("Attributes", attributes),
                    new Param("State", state),
                    new Param("Timers.Inactive", timersInactive),
                    new Param("Timers.Closed", timersClosed),
                    new Param("Bindings.Email.Address", bindingsEmailAddress),
                    new Param("Bindings.Email.Name", bindingsEmailName),
                    new Param("Participant", participant)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversationWithParticipants>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
