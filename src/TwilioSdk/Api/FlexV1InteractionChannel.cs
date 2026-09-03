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

public sealed class FlexV1InteractionChannel
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1InteractionChannel(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch a Channel for an Interaction.
    /// </summary>
    /// <param name="interactionSid">The unique string created by Twilio to identify an Interaction resource, prefixed with KD.</param>
    /// <param name="sid">The unique string created by Twilio to identify an Interaction Channel resource, prefixed with UO.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannel"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a Channel for an Interaction.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannel> FetchInteractionChannel(string interactionSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{Sid}"),
            [new TemplateParam("InteractionSid", interactionSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1InteractionInteractionChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List all Channels for an Interaction.
    /// </summary>
    /// <param name="interactionSid">The unique string created by Twilio to identify an Interaction resource, prefixed with KD.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInteractionChannelResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all Channels for an Interaction.
    /// </remarks>
    public Task<ListInteractionChannelResponse> ListInteractionChannel(string interactionSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels"),
            [new TemplateParam("InteractionSid", interactionSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInteractionChannelResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing Interaction Channel.
    /// </summary>
    /// <param name="interactionSid">The unique string created by Twilio to identify an Interaction resource, prefixed with KD.</param>
    /// <param name="sid">The unique string created by Twilio to identify an Interaction Channel resource, prefixed with UO.</param>
    /// <param name="status"></param>
    /// <param name="routing"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FlexV1InteractionInteractionChannel"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing Interaction Channel.
    /// </remarks>
    public Task<FlexV1InteractionInteractionChannel> UpdateInteractionChannel(string interactionSid,
        string sid,
        InteractionChannelEnumUpdateChannelStatus status,
        object? routing,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/Interactions/{InteractionSid}/Channels/{Sid}"),
            [new TemplateParam("InteractionSid", interactionSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status), new Param("Routing", routing)]),
            JsonResponse.Create<FlexV1InteractionInteractionChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
