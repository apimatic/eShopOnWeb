using System;
using System.Collections.Generic;
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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

/// <summary>
/// A conversation is a record of interactions between participants. It's the container for all communications that occur during an interaction, including voice calls, SMS messages, and other supported channels.
/// </summary>
public sealed class ConversationsV2ConversationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV2ConversationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Conversation
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Conversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateConversationWithConfigError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new conversation
    /// </remarks>
    public Task<ConversationsV2Conversation> CreateConversationWithConfig(V2ConversationsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2Conversation>(),
            CreateConversationWithConfigErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a Conversation (async)
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="idempotencyKey">Client-generated UUID key to ensure idempotent behavior. Submitting the same key returns the original response without creating a duplicate operation. Keys are scoped to account + region with a 24-hour TTL.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2OperationAccepted"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteConversationAsyncError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Asynchronously delete a conversation and all associated data.
    /// Returns 202 Accepted with an Operation-Id for status tracking via GET /v2/ControlPlane/Operations/{operationId}.
    /// </remarks>
    public Task<ConversationsV2OperationAccepted> DeleteConversationAsync(string sid,
        string? idempotencyKey,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", idempotencyKey)],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2OperationAccepted>(),
            DeleteConversationAsyncErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch Conversation
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Conversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchConversation2Error"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a Conversation.
    /// </remarks>
    public Task<ConversationsV2Conversation> FetchConversation2(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2Conversation>(),
            FetchConversation2ErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List Conversations
    /// </summary>
    /// <param name="status">Filters for specific statuses</param>
    /// <param name="channelId">The resource identifier (such as callSid or messageSid) to filter conversations.</param>
    /// <param name="pageToken">A URL-safe, base64-encoded token representing the page of results to return</param>
    /// <param name="pageSize">Maximum number of items to return in a single response</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="V2ConversationsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListConversationByAccountError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Conversations.
    /// </remarks>
    public Task<V2ConversationsResponse> ListConversationByAccount(IReadOnlyList<Status31>? status,
        string? channelId,
        string? pageToken,
        int? pageSize = 50,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations"),
            [],
            [new Param("status", status),
                new Param("channelId", channelId),
                new Param("pageSize", pageSize),
                new Param("pageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<V2ConversationsResponse>(),
            ListConversationByAccountErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Partially Update a Conversation
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Conversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PatchConversationByIdError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Partially update the details of an existing Conversation.
    /// </remarks>
    public Task<ConversationsV2Conversation> PatchConversationById(string sid,
        V2ConversationsRequest2? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2Conversation>(),
            PatchConversationByIdErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a Conversation
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Conversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateConversationByIdError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing conversation
    /// </remarks>
    public Task<ConversationsV2Conversation> UpdateConversationById(string sid,
        V2ConversationsRequest1? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2Conversation>(),
            UpdateConversationByIdErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
