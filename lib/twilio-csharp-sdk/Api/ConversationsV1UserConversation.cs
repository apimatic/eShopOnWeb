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
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class ConversationsV1UserConversation
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1UserConversation(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a specific User Conversation.
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="conversationSid">The unique SID identifier of the Conversation. This value can be either the <c>sid</c> or the <c>unique_name</c> of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation resource</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific User Conversation.
    /// </remarks>
    public Task DeleteServiceUserConversation(string chatServiceSid,
        string userSid,
        string conversationSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users/{UserSid}/Conversations/{ConversationSid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("UserSid", userSid),
                new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific User Conversation.
    /// </summary>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="conversationSid">The unique SID identifier of the Conversation. This value can be either the <c>sid</c> or the <c>unique_name</c> of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation resource</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific User Conversation.
    /// </remarks>
    public Task DeleteUserConversation(string userSid,
        string conversationSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users/{UserSid}/Conversations/{ConversationSid}"),
            [new TemplateParam("UserSid", userSid), new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a specific User Conversation.
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="conversationSid">The unique SID identifier of the Conversation. This value can be either the <c>sid</c> or the <c>unique_name</c> of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation resource</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceUserServiceUserConversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific User Conversation.
    /// </remarks>
    public Task<ConversationsV1ServiceServiceUserServiceUserConversation> FetchServiceUserConversation(string chatServiceSid,
        string userSid,
        string conversationSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users/{UserSid}/Conversations/{ConversationSid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("UserSid", userSid),
                new TemplateParam("ConversationSid", conversationSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceUserServiceUserConversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a specific User Conversation.
    /// </summary>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="conversationSid">The unique SID identifier of the Conversation. This value can be either the <c>sid</c> or the <c>unique_name</c> of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation resource</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1UserUserConversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific User Conversation.
    /// </remarks>
    public Task<ConversationsV1UserUserConversation> FetchUserConversation(string userSid,
        string conversationSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users/{UserSid}/Conversations/{ConversationSid}"),
            [new TemplateParam("UserSid", userSid), new TemplateParam("ConversationSid", conversationSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1UserUserConversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all User Conversations for the User.
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceUserConversationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all User Conversations for the User.
    /// </remarks>
    public Task<ListServiceUserConversationResponse> ListServiceUserConversation(string chatServiceSid,
        string userSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users/{UserSid}/Conversations"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("UserSid", userSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceUserConversationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all User Conversations for the User.
    /// </summary>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListUserConversationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all User Conversations for the User.
    /// </remarks>
    public Task<ListUserConversationResponse> ListUserConversation(string userSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users/{UserSid}/Conversations"),
            [new TemplateParam("UserSid", userSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListUserConversationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific User Conversation.
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="conversationSid">The unique SID identifier of the Conversation. This value can be either the <c>sid</c> or the <c>unique_name</c> of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation resource</see>.</param>
    /// <param name="notificationLevel"></param>
    /// <param name="lastReadTimestamp"></param>
    /// <param name="lastReadMessageIndex"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceUserServiceUserConversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific User Conversation.
    /// </remarks>
    public Task<ConversationsV1ServiceServiceUserServiceUserConversation> UpdateServiceUserConversation(string chatServiceSid,
        string userSid,
        string conversationSid,
        ServiceUserConversationEnumNotificationLevel? notificationLevel,
        DateTimeOffset? lastReadTimestamp,
        int? lastReadMessageIndex,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users/{UserSid}/Conversations/{ConversationSid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("UserSid", userSid),
                new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("NotificationLevel", notificationLevel),
                    new Param("LastReadTimestamp", lastReadTimestamp),
                    new Param("LastReadMessageIndex", lastReadMessageIndex)]),
            JsonResponse.Create<ConversationsV1ServiceServiceUserServiceUserConversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific User Conversation.
    /// </summary>
    /// <param name="userSid">The unique SID identifier of the <see href="https://www.twilio.com/docs/conversations/api/user-resource">User resource</see>. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource.</param>
    /// <param name="conversationSid">The unique SID identifier of the Conversation. This value can be either the <c>sid</c> or the <c>unique_name</c> of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation resource</see>.</param>
    /// <param name="notificationLevel"></param>
    /// <param name="lastReadTimestamp"></param>
    /// <param name="lastReadMessageIndex"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1UserUserConversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific User Conversation.
    /// </remarks>
    public Task<ConversationsV1UserUserConversation> UpdateUserConversation(string userSid,
        string conversationSid,
        UserConversationEnumNotificationLevel? notificationLevel,
        DateTimeOffset? lastReadTimestamp,
        int? lastReadMessageIndex,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users/{UserSid}/Conversations/{ConversationSid}"),
            [new TemplateParam("UserSid", userSid), new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("NotificationLevel", notificationLevel),
                    new Param("LastReadTimestamp", lastReadTimestamp),
                    new Param("LastReadMessageIndex", lastReadMessageIndex)]),
            JsonResponse.Create<ConversationsV1UserUserConversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
