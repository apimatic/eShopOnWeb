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

public sealed class FlexV1InteractionChannelInvite
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1InteractionChannelInvite(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Invite an Agent or a TaskQueue to a Channel.
    /// </summary>
    /// <param name="interactionSid">The Interaction SID for this Channel.</param>
    /// <param name="channelSid">The Channel SID for this Invite.</param>
    /// <param name="routing"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannelInteractionChannelInvite"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Invite an Agent or a TaskQueue to a Channel.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannelInteractionChannelInvite> CreateInteractionChannelInvite(string interactionSid,
        string channelSid,
        object routing,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Invites"),
            [new TemplateParam("InteractionSid", interactionSid), new TemplateParam("ChannelSid", channelSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Routing", routing)]),
            JsonResponse.Create<FlexV1InteractionInteractionChannelInteractionChannelInvite>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List all Invites for a Channel.
    /// </summary>
    /// <param name="interactionSid">The Interaction SID for this Channel.</param>
    /// <param name="channelSid">The Channel SID for this Participant.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInteractionChannelInviteResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all Invites for a Channel.
    /// </remarks>
    public Task<ListInteractionChannelInviteResponse> ListInteractionChannelInvite(string interactionSid,
        string channelSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Invites"),
            [new TemplateParam("InteractionSid", interactionSid), new TemplateParam("ChannelSid", channelSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInteractionChannelInviteResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
