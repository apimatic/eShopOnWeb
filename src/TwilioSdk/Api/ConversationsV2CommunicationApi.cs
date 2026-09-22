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
/// A communication is the smallest unit of interaction within a conversation. Each communication represents a single event—such as an SMS message or a voice utterance.
/// </summary>
public sealed class ConversationsV2CommunicationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV2CommunicationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Communication
    /// </summary>
    /// <param name="conversationSid"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Communication"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateCommunicationInConversationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a Communication.
    /// </remarks>
    public Task<ConversationsV2Communication> CreateCommunicationInConversation(string conversationSid,
        V2ConversationsCommunicationsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationSid}/Communications"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2Communication>(),
            CreateCommunicationInConversationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch Communication
    /// </summary>
    /// <param name="conversationSid"></param>
    /// <param name="sid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Communication"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchCommunicationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a Communication.
    /// </remarks>
    public Task<ConversationsV2Communication> FetchCommunication(string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationSid}/Communications/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2Communication>(),
            FetchCommunicationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List Communications
    /// </summary>
    /// <param name="conversationSid"></param>
    /// <param name="channelId">Resource identifier to filter communications</param>
    /// <param name="pageToken">Page token for pagination</param>
    /// <param name="pageSize">Maximum number of items to return</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="V2ConversationsCommunicationsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListCommunicationByConversationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Communications in a Conversation.
    /// </remarks>
    public Task<V2ConversationsCommunicationsResponse> ListCommunicationByConversation(string conversationSid,
        string? channelId,
        string? pageToken,
        int? pageSize = 50,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationSid}/Communications"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [new Param("channelId", channelId), new Param("pageSize", pageSize), new Param("pageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<V2ConversationsCommunicationsResponse>(),
            ListCommunicationByConversationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
