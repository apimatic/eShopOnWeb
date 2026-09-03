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

public sealed class ConversationsV1Participant
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1Participant(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add a new participant to the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="identity"></param>
    /// <param name="messagingBindingAddress"></param>
    /// <param name="messagingBindingProxyAddress"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="messagingBindingProjectedAddress"></param>
    /// <param name="roleSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new participant to the conversation
    /// </remarks>
    public Task<ConversationsV1ConversationConversationParticipant> CreateConversationParticipant(string conversationSid,
        Confirmation? xTwilioWebhookEnabled,
        string? identity,
        string? messagingBindingAddress,
        string? messagingBindingProxyAddress,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? messagingBindingProjectedAddress,
        string? roleSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Participants"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Identity", identity),
                    new Param("MessagingBinding.Address", messagingBindingAddress),
                    new Param("MessagingBinding.ProxyAddress", messagingBindingProxyAddress),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("MessagingBinding.ProjectedAddress", messagingBindingProjectedAddress),
                    new Param("RoleSid", roleSid)]),
            JsonResponse.Create<ConversationsV1ConversationConversationParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Add a new participant to the conversation in a specific service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="identity"></param>
    /// <param name="messagingBindingAddress"></param>
    /// <param name="messagingBindingProxyAddress"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="messagingBindingProjectedAddress"></param>
    /// <param name="roleSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new participant to the conversation in a specific service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationParticipant> CreateServiceConversationParticipant(string chatServiceSid,
        string conversationSid,
        Confirmation? xTwilioWebhookEnabled,
        string? identity,
        string? messagingBindingAddress,
        string? messagingBindingProxyAddress,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? messagingBindingProjectedAddress,
        string? roleSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Participants"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Identity", identity),
                    new Param("MessagingBinding.Address", messagingBindingAddress),
                    new Param("MessagingBinding.ProxyAddress", messagingBindingProxyAddress),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("MessagingBinding.ProjectedAddress", messagingBindingProjectedAddress),
                    new Param("RoleSid", roleSid)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove a participant from the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a participant from the conversation
    /// </remarks>
    public Task DeleteConversationParticipant(string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove a participant from the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a participant from the conversation
    /// </remarks>
    public Task DeleteServiceConversationParticipant(string chatServiceSid,
        string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a participant of the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Alternatively, you can pass a Participant's <c>identity</c> rather than the SID.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a participant of the conversation
    /// </remarks>
    public Task<ConversationsV1ConversationConversationParticipant> FetchConversationParticipant(string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ConversationConversationParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a participant of the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Alternatively, you can pass a Participant's <c>identity</c> rather than the SID.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a participant of the conversation
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationParticipant> FetchServiceConversationParticipant(string chatServiceSid,
        string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all participants of the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for participants.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConversationParticipantResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all participants of the conversation
    /// </remarks>
    public Task<ListConversationParticipantResponse> ListConversationParticipant(string conversationSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Participants"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConversationParticipantResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all participants of the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for participants.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceConversationParticipantResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all participants of the conversation
    /// </remarks>
    public Task<ListServiceConversationParticipantResponse> ListServiceConversationParticipant(string chatServiceSid,
        string conversationSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Participants"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("ConversationSid", conversationSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceConversationParticipantResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing participant in the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="roleSid"></param>
    /// <param name="messagingBindingProxyAddress"></param>
    /// <param name="messagingBindingProjectedAddress"></param>
    /// <param name="identity"></param>
    /// <param name="lastReadMessageIndex"></param>
    /// <param name="lastReadTimestamp"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing participant in the conversation
    /// </remarks>
    public Task<ConversationsV1ConversationConversationParticipant> UpdateConversationParticipant(string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? roleSid,
        string? messagingBindingProxyAddress,
        string? messagingBindingProjectedAddress,
        string? identity,
        int? lastReadMessageIndex,
        string? lastReadTimestamp,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("RoleSid", roleSid),
                    new Param("MessagingBinding.ProxyAddress", messagingBindingProxyAddress),
                    new Param("MessagingBinding.ProjectedAddress", messagingBindingProjectedAddress),
                    new Param("Identity", identity),
                    new Param("LastReadMessageIndex", lastReadMessageIndex),
                    new Param("LastReadTimestamp", lastReadTimestamp)]),
            JsonResponse.Create<ConversationsV1ConversationConversationParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing participant in the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this participant.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="identity"></param>
    /// <param name="attributes"></param>
    /// <param name="roleSid"></param>
    /// <param name="messagingBindingProxyAddress"></param>
    /// <param name="messagingBindingProjectedAddress"></param>
    /// <param name="lastReadMessageIndex"></param>
    /// <param name="lastReadTimestamp"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationParticipant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing participant in the conversation
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationParticipant> UpdateServiceConversationParticipant(string chatServiceSid,
        string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? identity,
        string? attributes,
        string? roleSid,
        string? messagingBindingProxyAddress,
        string? messagingBindingProjectedAddress,
        int? lastReadMessageIndex,
        string? lastReadTimestamp,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Identity", identity),
                    new Param("Attributes", attributes),
                    new Param("RoleSid", roleSid),
                    new Param("MessagingBinding.ProxyAddress", messagingBindingProxyAddress),
                    new Param("MessagingBinding.ProjectedAddress", messagingBindingProjectedAddress),
                    new Param("LastReadMessageIndex", lastReadMessageIndex),
                    new Param("LastReadTimestamp", lastReadTimestamp)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationParticipant>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
