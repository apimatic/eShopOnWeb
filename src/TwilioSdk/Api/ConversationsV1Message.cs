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

public sealed class ConversationsV1Message
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1Message(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add a new message to the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="author"></param>
    /// <param name="body"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="mediaSid"></param>
    /// <param name="contentSid"></param>
    /// <param name="contentVariables"></param>
    /// <param name="subject"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new message to the conversation
    /// </remarks>
    public Task<ConversationsV1ConversationConversationMessage> CreateConversationMessage(string conversationSid,
        Confirmation? xTwilioWebhookEnabled,
        string? author,
        string? body,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? mediaSid,
        string? contentSid,
        string? contentVariables,
        string? subject,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Messages"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Author", author),
                    new Param("Body", body),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("MediaSid", mediaSid),
                    new Param("ContentSid", contentSid),
                    new Param("ContentVariables", contentVariables),
                    new Param("Subject", subject)]),
            JsonResponse.Create<ConversationsV1ConversationConversationMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Add a new message to the conversation in a specific service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="author"></param>
    /// <param name="body"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="mediaSid"></param>
    /// <param name="contentSid"></param>
    /// <param name="contentVariables"></param>
    /// <param name="subject"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new message to the conversation in a specific service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationMessage> CreateServiceConversationMessage(string chatServiceSid,
        string conversationSid,
        Confirmation? xTwilioWebhookEnabled,
        string? author,
        string? body,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? mediaSid,
        string? contentSid,
        string? contentVariables,
        string? subject,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Messages"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Author", author),
                    new Param("Body", body),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("MediaSid", mediaSid),
                    new Param("ContentSid", contentSid),
                    new Param("ContentVariables", contentVariables),
                    new Param("Subject", subject)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove a message from the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a message from the conversation
    /// </remarks>
    public Task DeleteConversationMessage(string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Messages/{Sid}"),
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
    /// Remove a message from the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a message from the conversation
    /// </remarks>
    public Task DeleteServiceConversationMessage(string chatServiceSid,
        string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Messages/{Sid}"),
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
    /// Fetch a message from the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a message from the conversation
    /// </remarks>
    public Task<ConversationsV1ConversationConversationMessage> FetchConversationMessage(string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Messages/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ConversationConversationMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a message from the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a message from the conversation
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationMessage> FetchServiceConversationMessage(string chatServiceSid,
        string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Messages/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all messages in the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for messages.</param>
    /// <param name="order">The sort order of the returned messages. Can be: <c>asc</c> (ascending) or <c>desc</c> (descending), with <c>asc</c> as the default.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConversationMessageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all messages in the conversation
    /// </remarks>
    public Task<ListConversationMessageResponse> ListConversationMessage(string conversationSid,
        ChallengeEnumListOrders? order,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Messages"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [new Param("Order", order),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConversationMessageResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all messages in the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for messages.</param>
    /// <param name="order">The sort order of the returned messages. Can be: <c>asc</c> (ascending) or <c>desc</c> (descending), with <c>asc</c> as the default.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceConversationMessageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all messages in the conversation
    /// </remarks>
    public Task<ListServiceConversationMessageResponse> ListServiceConversationMessage(string chatServiceSid,
        string conversationSid,
        ChallengeEnumListOrders? order,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Messages"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("ConversationSid", conversationSid)],
            [new Param("Order", order),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceConversationMessageResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing message in the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="author"></param>
    /// <param name="body"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="subject"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing message in the conversation
    /// </remarks>
    public Task<ConversationsV1ConversationConversationMessage> UpdateConversationMessage(string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        string? author,
        string? body,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? subject,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Messages/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Author", author),
                    new Param("Body", body),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("Subject", subject)]),
            JsonResponse.Create<ConversationsV1ConversationConversationMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing message in the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="author"></param>
    /// <param name="body"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="subject"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing message in the conversation
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationMessage> UpdateServiceConversationMessage(string chatServiceSid,
        string conversationSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        string? author,
        string? body,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? subject,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Messages/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Author", author),
                    new Param("Body", body),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("Subject", subject)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
