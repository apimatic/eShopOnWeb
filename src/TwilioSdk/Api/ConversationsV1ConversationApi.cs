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

public sealed class ConversationsV1ConversationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1ConversationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new conversation in your account's default service
    /// </summary>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="uniqueName"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="attributes"></param>
    /// <param name="state"></param>
    /// <param name="timersInactive"></param>
    /// <param name="timersClosed"></param>
    /// <param name="bindingsEmailAddress"></param>
    /// <param name="bindingsEmailName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Conversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new conversation in your account's default service
    /// </remarks>
    public Task<ConversationsV1Conversation> CreateConversation(Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        string? uniqueName,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? messagingServiceSid,
        string? attributes,
        ConversationEnumState? state,
        string? timersInactive,
        string? timersClosed,
        string? bindingsEmailAddress,
        string? bindingsEmailName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations"),
            [],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("UniqueName", uniqueName),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("MessagingServiceSid", messagingServiceSid),
                    new Param("Attributes", attributes),
                    new Param("State", state),
                    new Param("Timers.Inactive", timersInactive),
                    new Param("Timers.Closed", timersClosed),
                    new Param("Bindings.Email.Address", bindingsEmailAddress),
                    new Param("Bindings.Email.Name", bindingsEmailName)]),
            JsonResponse.Create<ConversationsV1Conversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Create a new conversation in your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="uniqueName"></param>
    /// <param name="attributes"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="state"></param>
    /// <param name="timersInactive"></param>
    /// <param name="timersClosed"></param>
    /// <param name="bindingsEmailAddress"></param>
    /// <param name="bindingsEmailName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new conversation in your service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversation> CreateServiceConversation(string chatServiceSid,
        Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        string? uniqueName,
        string? attributes,
        string? messagingServiceSid,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        ServiceConversationEnumState? state,
        string? timersInactive,
        string? timersClosed,
        string? bindingsEmailAddress,
        string? bindingsEmailName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("UniqueName", uniqueName),
                    new Param("Attributes", attributes),
                    new Param("MessagingServiceSid", messagingServiceSid),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("State", state),
                    new Param("Timers.Inactive", timersInactive),
                    new Param("Timers.Closed", timersClosed),
                    new Param("Bindings.Email.Address", bindingsEmailAddress),
                    new Param("Bindings.Email.Name", bindingsEmailName)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove a conversation from your account's default service
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Can also be the <c>unique_name</c> of the Conversation.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a conversation from your account's default service
    /// </remarks>
    public Task DeleteConversation(string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{Sid}"),
            [new TemplateParam("Sid", sid)],
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
    /// Remove a conversation from your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Can also be the <c>unique_name</c> of the Conversation.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a conversation from your service
    /// </remarks>
    public Task DeleteServiceConversation(string chatServiceSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("Sid", sid)],
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
    /// Fetch a conversation from your account's default service
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Can also be the <c>unique_name</c> of the Conversation.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Conversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a conversation from your account's default service
    /// </remarks>
    public Task<ConversationsV1Conversation> FetchConversation(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1Conversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a conversation from your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Can also be the <c>unique_name</c> of the Conversation.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a conversation from your service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversation> FetchServiceConversation(string chatServiceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceConversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of conversations in your account's default service
    /// </summary>
    /// <param name="startDate">Specifies the beginning of the date range for filtering Conversations based on their creation date. Conversations that were created on or after this date will be included in the results. The date must be in ISO8601 format, specifically starting at the beginning of the specified date (YYYY-MM-DDT00:00:00Z), for precise filtering. This parameter can be combined with other filters. If this filter is used, the returned list is sorted by latest conversation creation date in descending order.</param>
    /// <param name="endDate">Defines the end of the date range for filtering conversations by their creation date. Only conversations that were created on or before this date will appear in the results.  The date must be in ISO8601 format, specifically capturing up to the end of the specified date (YYYY-MM-DDT23:59:59Z), to ensure that conversations from the entire end day are included. This parameter can be combined with other filters. If this filter is used, the returned list is sorted by latest conversation creation date in descending order.</param>
    /// <param name="state">State for sorting and filtering list of Conversations. Can be <c>active</c>, <c>inactive</c> or <c>closed</c></param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConversationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of conversations in your account's default service
    /// </remarks>
    public Task<ListConversationResponse> ListConversation(string? startDate,
        string? endDate,
        ConversationEnumState? state,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations"),
            [],
            [new Param("StartDate", startDate),
                new Param("EndDate", endDate),
                new Param("State", state),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConversationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of conversations in your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="startDate">Specifies the beginning of the date range for filtering Conversations based on their creation date. Conversations that were created on or after this date will be included in the results. The date must be in ISO8601 format, specifically starting at the beginning of the specified date (YYYY-MM-DDT00:00:00Z), for precise filtering. This parameter can be combined with other filters. If this filter is used, the returned list is sorted by latest conversation creation date in descending order.</param>
    /// <param name="endDate">Defines the end of the date range for filtering conversations by their creation date. Only conversations that were created on or before this date will appear in the results.  The date must be in ISO8601 format, specifically capturing up to the end of the specified date (YYYY-MM-DDT23:59:59Z), to ensure that conversations from the entire end day are included. This parameter can be combined with other filters. If this filter is used, the returned list is sorted by latest conversation creation date in descending order.</param>
    /// <param name="state">State for sorting and filtering list of Conversations. Can be <c>active</c>, <c>inactive</c> or <c>closed</c></param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceConversationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of conversations in your service
    /// </remarks>
    public Task<ListServiceConversationResponse> ListServiceConversation(string chatServiceSid,
        string? startDate,
        string? endDate,
        ServiceConversationEnumState? state,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [new Param("StartDate", startDate),
                new Param("EndDate", endDate),
                new Param("State", state),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceConversationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing conversation in your account's default service
    /// </summary>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Can also be the <c>unique_name</c> of the Conversation.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="state"></param>
    /// <param name="timersInactive"></param>
    /// <param name="timersClosed"></param>
    /// <param name="uniqueName"></param>
    /// <param name="bindingsEmailAddress"></param>
    /// <param name="bindingsEmailName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Conversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing conversation in your account's default service
    /// </remarks>
    public Task<ConversationsV1Conversation> UpdateConversation(string sid,
        Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? messagingServiceSid,
        ConversationEnumState? state,
        string? timersInactive,
        string? timersClosed,
        string? uniqueName,
        string? bindingsEmailAddress,
        string? bindingsEmailName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("MessagingServiceSid", messagingServiceSid),
                    new Param("State", state),
                    new Param("Timers.Inactive", timersInactive),
                    new Param("Timers.Closed", timersClosed),
                    new Param("UniqueName", uniqueName),
                    new Param("Bindings.Email.Address", bindingsEmailAddress),
                    new Param("Bindings.Email.Name", bindingsEmailName)]),
            JsonResponse.Create<ConversationsV1Conversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing conversation in your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Conversation resource is associated with.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource. Can also be the <c>unique_name</c> of the Conversation.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="dateCreated"></param>
    /// <param name="dateUpdated"></param>
    /// <param name="attributes"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="state"></param>
    /// <param name="timersInactive"></param>
    /// <param name="timersClosed"></param>
    /// <param name="uniqueName"></param>
    /// <param name="bindingsEmailAddress"></param>
    /// <param name="bindingsEmailName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversation"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing conversation in your service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversation> UpdateServiceConversation(string chatServiceSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateUpdated,
        string? attributes,
        string? messagingServiceSid,
        ServiceConversationEnumState? state,
        string? timersInactive,
        string? timersClosed,
        string? uniqueName,
        string? bindingsEmailAddress,
        string? bindingsEmailName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("DateCreated", dateCreated),
                    new Param("DateUpdated", dateUpdated),
                    new Param("Attributes", attributes),
                    new Param("MessagingServiceSid", messagingServiceSid),
                    new Param("State", state),
                    new Param("Timers.Inactive", timersInactive),
                    new Param("Timers.Closed", timersClosed),
                    new Param("UniqueName", uniqueName),
                    new Param("Bindings.Email.Address", bindingsEmailAddress),
                    new Param("Bindings.Email.Name", bindingsEmailName)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
