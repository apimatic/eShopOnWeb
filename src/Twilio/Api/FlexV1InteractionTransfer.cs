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

public sealed class FlexV1InteractionTransfer
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1InteractionTransfer(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Transfer.
    /// </summary>
    /// <param name="interactionSid">The Interaction Sid for the Interaction</param>
    /// <param name="channelSid">The Channel Sid for the Channel.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannelInteractionTransfer"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Transfer.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannelInteractionTransfer> CreateInteractionTransfer(string interactionSid,
        string channelSid,
        object? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Transfers"),
            [new TemplateParam("InteractionSid", interactionSid), new TemplateParam("ChannelSid", channelSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<FlexV1InteractionInteractionChannelInteractionTransfer>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a specific Transfer by SID.
    /// </summary>
    /// <param name="interactionSid">The Interaction Sid for this channel.</param>
    /// <param name="channelSid">The Channel Sid for this Transfer.</param>
    /// <param name="sid">The unique string created by Twilio to identify a Transfer resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannelInteractionTransfer"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Transfer by SID.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannelInteractionTransfer> FetchInteractionTransfer(string interactionSid,
        string channelSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Transfers/{Sid}"),
            [new TemplateParam("InteractionSid", interactionSid),
                new TemplateParam("ChannelSid", channelSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1InteractionInteractionChannelInteractionTransfer>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing Transfer.
    /// </summary>
    /// <param name="interactionSid">The Interaction Sid for this channel.</param>
    /// <param name="channelSid">The Channel Sid for this Transfer.</param>
    /// <param name="sid">The unique string created by Twilio to identify a Transfer resource.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannelInteractionTransfer"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing Transfer.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannelInteractionTransfer> UpdateInteractionTransfer(string interactionSid,
        string channelSid,
        string sid,
        object? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{ChannelSid}/Transfers/{Sid}"),
            [new TemplateParam("InteractionSid", interactionSid),
                new TemplateParam("ChannelSid", channelSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<FlexV1InteractionInteractionChannelInteractionTransfer>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
