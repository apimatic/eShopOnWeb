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

namespace TwilioSdk.Api;

/// <summary>
/// Perform actions within a Conversation. Actions trigger side effects such as sending messages and return 202 Accepted.
/// </summary>
public sealed class ConversationsV2ActionApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV2ActionApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create an Action
    /// </summary>
    /// <param name="conversationId"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Action"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateConversationActionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an Action within a Conversation. Currently supports SEND_MESSAGE,
    /// which sends a message to recipients via the configured channel.
    /// <para>
    /// Returns 202 Accepted with the Action in PENDING status. Poll
    /// <c>GET /v2/Conversations/{ConversationId}/Actions/{ActionId}</c> to check completion.
    /// </para>
    /// </remarks>
    public Task<ConversationsV2Action> CreateConversationAction(string conversationId,
        ConversationsV2SendMessageActionRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationId}/Actions"),
            [new TemplateParam("ConversationId", conversationId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2Action>(),
            CreateConversationActionErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Get Action Status
    /// </summary>
    /// <param name="conversationId"></param>
    /// <param name="actionId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Action"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchConversationActionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve the current status of an Action.
    /// </remarks>
    public Task<ConversationsV2Action> FetchConversationAction(string conversationId,
        string actionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationId}/Actions/{ActionId}"),
            [new TemplateParam("ConversationId", conversationId), new TemplateParam("ActionId", actionId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2Action>(),
            FetchConversationActionErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
