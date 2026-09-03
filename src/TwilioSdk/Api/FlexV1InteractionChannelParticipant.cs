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

public sealed class FlexV1InteractionChannelParticipant
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1InteractionChannelParticipant(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add a Participant to a Channel.
    /// </summary>
    /// <param name="interactionSid">The Interaction Sid for the new Channel Participant.</param>
    /// <param name="channelSid">The Channel Sid for the new Channel Participant.</param>
    /// <param name="type"></param>
    /// <param name="mediaProperties"></param>
    /// <param name="routingProperties"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannelInteractionChannelParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a Participant to a Channel.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannelInteractionChannelParticipant> CreateInteractionChannelParticipant(string interactionSid,
        string channelSid,
        InteractionChannelParticipantEnumType type,
        object mediaProperties,
        object? routingProperties,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Participants"),
            [new TemplateParam("InteractionSid", interactionSid), new TemplateParam("ChannelSid", channelSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Type", type),
                    new Param("MediaProperties", mediaProperties),
                    new Param("RoutingProperties", routingProperties)]),
            JsonResponse.Create<FlexV1InteractionInteractionChannelInteractionChannelParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List all Participants for a Channel.
    /// </summary>
    /// <param name="interactionSid">The Interaction Sid for this channel.</param>
    /// <param name="channelSid">The Channel Sid for this Participant.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInteractionChannelParticipantResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all Participants for a Channel.
    /// </remarks>
    public Task<ListInteractionChannelParticipantResponse> ListInteractionChannelParticipant(string interactionSid,
        string channelSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Participants"),
            [new TemplateParam("InteractionSid", interactionSid), new TemplateParam("ChannelSid", channelSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInteractionChannelParticipantResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing Channel Participant.
    /// </summary>
    /// <param name="interactionSid">The Interaction Sid for this channel.</param>
    /// <param name="channelSid">The Channel Sid for this Participant.</param>
    /// <param name="sid">The unique string created by Twilio to identify an Interaction Channel resource.</param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannelInteractionChannelParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing Channel Participant.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannelInteractionChannelParticipant> UpdateInteractionChannelParticipant(string interactionSid,
        string channelSid,
        string sid,
        InteractionChannelParticipantEnumStatus status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Participants/{Sid}"),
            [new TemplateParam("InteractionSid", interactionSid),
                new TemplateParam("ChannelSid", channelSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status)]),
            JsonResponse.Create<FlexV1InteractionInteractionChannelInteractionChannelParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
