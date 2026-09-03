using System;
using System.Collections.Generic;
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

public sealed class ConversationsV1Webhook
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1Webhook(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new webhook scoped to the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="target"></param>
    /// <param name="configurationUrl"></param>
    /// <param name="configurationMethod"></param>
    /// <param name="configurationFilters"></param>
    /// <param name="configurationTriggers"></param>
    /// <param name="configurationFlowSid"></param>
    /// <param name="configurationReplayAfter"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationScopedWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new webhook scoped to the conversation
    /// </remarks>
    public Task<ConversationsV1ConversationConversationScopedWebhook> CreateConversationScopedWebhook(string conversationSid,
        ConversationScopedWebhookEnumTarget target,
        string? configurationUrl,
        ConversationScopedWebhookEnumMethod? configurationMethod,
        IReadOnlyList<string>? configurationFilters,
        IReadOnlyList<string>? configurationTriggers,
        string? configurationFlowSid,
        int? configurationReplayAfter,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Webhooks"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Target", target),
                    new Param("Configuration.Url", configurationUrl),
                    new Param("Configuration.Method", configurationMethod),
                    new Param("Configuration.Filters", configurationFilters),
                    new Param("Configuration.Triggers", configurationTriggers),
                    new Param("Configuration.FlowSid", configurationFlowSid),
                    new Param("Configuration.ReplayAfter", configurationReplayAfter)]),
            JsonResponse.Create<ConversationsV1ConversationConversationScopedWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Create a new webhook scoped to the conversation in a specific service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="target"></param>
    /// <param name="configurationUrl"></param>
    /// <param name="configurationMethod"></param>
    /// <param name="configurationFilters"></param>
    /// <param name="configurationTriggers"></param>
    /// <param name="configurationFlowSid"></param>
    /// <param name="configurationReplayAfter"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new webhook scoped to the conversation in a specific service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook> CreateServiceConversationScopedWebhook(string chatServiceSid,
        string conversationSid,
        ServiceConversationScopedWebhookEnumTarget target,
        string? configurationUrl,
        ServiceConversationScopedWebhookEnumMethod? configurationMethod,
        IReadOnlyList<string>? configurationFilters,
        IReadOnlyList<string>? configurationTriggers,
        string? configurationFlowSid,
        int? configurationReplayAfter,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Webhooks"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Target", target),
                    new Param("Configuration.Url", configurationUrl),
                    new Param("Configuration.Method", configurationMethod),
                    new Param("Configuration.Filters", configurationFilters),
                    new Param("Configuration.Triggers", configurationTriggers),
                    new Param("Configuration.FlowSid", configurationFlowSid),
                    new Param("Configuration.ReplayAfter", configurationReplayAfter)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove an existing webhook scoped to the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove an existing webhook scoped to the conversation
    /// </remarks>
    public Task DeleteConversationScopedWebhook(string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Webhooks/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
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
    /// Remove an existing webhook scoped to the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove an existing webhook scoped to the conversation
    /// </remarks>
    public Task DeleteServiceConversationScopedWebhook(string chatServiceSid,
        string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Webhooks/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
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
    /// A Webhook resource manages a service-level set of callback URLs and their configuration for receiving all conversation events.
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConfigurationConfigurationWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ConversationsV1ConfigurationConfigurationWebhook> FetchConfigurationWebhook(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration/Webhooks"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ConfigurationConfigurationWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch the configuration of a conversation-scoped webhook
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationScopedWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the configuration of a conversation-scoped webhook
    /// </remarks>
    public Task<ConversationsV1ConversationConversationScopedWebhook> FetchConversationScopedWebhook(string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Webhooks/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ConversationConversationScopedWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch the configuration of a conversation-scoped webhook
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the configuration of a conversation-scoped webhook
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook> FetchServiceConversationScopedWebhook(string chatServiceSid,
        string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Webhooks/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a specific service webhook configuration.
    /// </summary>
    /// <param name="chatServiceSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> this conversation belongs to.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConfigurationServiceWebhookConfiguration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific service webhook configuration.
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConfigurationServiceWebhookConfiguration> FetchServiceWebhookConfiguration(string chatServiceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Configuration/Webhooks"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceConfigurationServiceWebhookConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all webhooks scoped to the conversation
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 5, and the maximum is 5.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConversationScopedWebhookResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all webhooks scoped to the conversation
    /// </remarks>
    public Task<ListConversationScopedWebhookResponse> ListConversationScopedWebhook(string conversationSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Webhooks"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConversationScopedWebhookResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all webhooks scoped to the conversation
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 5, and the maximum is 5.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceConversationScopedWebhookResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all webhooks scoped to the conversation
    /// </remarks>
    public Task<ListServiceConversationScopedWebhookResponse> ListServiceConversationScopedWebhook(string chatServiceSid,
        string conversationSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Webhooks"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("ConversationSid", conversationSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceConversationScopedWebhookResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Webhook resource manages a service-level set of callback URLs and their configuration for receiving all conversation events.
    /// </summary>
    /// <param name="method"></param>
    /// <param name="filters"></param>
    /// <param name="preWebhookUrl"></param>
    /// <param name="postWebhookUrl"></param>
    /// <param name="target"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConfigurationConfigurationWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ConversationsV1ConfigurationConfigurationWebhook> UpdateConfigurationWebhook(string? method,
        IReadOnlyList<string>? filters,
        string? preWebhookUrl,
        string? postWebhookUrl,
        ConfigurationWebhookEnumTarget? target,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration/Webhooks"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Method", method),
                    new Param("Filters", filters),
                    new Param("PreWebhookUrl", preWebhookUrl),
                    new Param("PostWebhookUrl", postWebhookUrl),
                    new Param("Target", target)]),
            JsonResponse.Create<ConversationsV1ConfigurationConfigurationWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing conversation-scoped webhook
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="configurationUrl"></param>
    /// <param name="configurationMethod"></param>
    /// <param name="configurationFilters"></param>
    /// <param name="configurationTriggers"></param>
    /// <param name="configurationFlowSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationScopedWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing conversation-scoped webhook
    /// </remarks>
    public Task<ConversationsV1ConversationConversationScopedWebhook> UpdateConversationScopedWebhook(string conversationSid,
        string sid,
        string? configurationUrl,
        ConversationScopedWebhookEnumMethod? configurationMethod,
        IReadOnlyList<string>? configurationFilters,
        IReadOnlyList<string>? configurationTriggers,
        string? configurationFlowSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Webhooks/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Configuration.Url", configurationUrl),
                    new Param("Configuration.Method", configurationMethod),
                    new Param("Configuration.Filters", configurationFilters),
                    new Param("Configuration.Triggers", configurationTriggers),
                    new Param("Configuration.FlowSid", configurationFlowSid)]),
            JsonResponse.Create<ConversationsV1ConversationConversationScopedWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing conversation-scoped webhook
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this webhook.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="configurationUrl"></param>
    /// <param name="configurationMethod"></param>
    /// <param name="configurationFilters"></param>
    /// <param name="configurationTriggers"></param>
    /// <param name="configurationFlowSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing conversation-scoped webhook
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook> UpdateServiceConversationScopedWebhook(string chatServiceSid,
        string conversationSid,
        string sid,
        string? configurationUrl,
        ServiceConversationScopedWebhookEnumMethod? configurationMethod,
        IReadOnlyList<string>? configurationFilters,
        IReadOnlyList<string>? configurationTriggers,
        string? configurationFlowSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Webhooks/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Configuration.Url", configurationUrl),
                    new Param("Configuration.Method", configurationMethod),
                    new Param("Configuration.Filters", configurationFilters),
                    new Param("Configuration.Triggers", configurationTriggers),
                    new Param("Configuration.FlowSid", configurationFlowSid)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConversationServiceConversationScopedWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific Webhook.
    /// </summary>
    /// <param name="chatServiceSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> this conversation belongs to.</param>
    /// <param name="preWebhookUrl"></param>
    /// <param name="postWebhookUrl"></param>
    /// <param name="filters"></param>
    /// <param name="method"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConfigurationServiceWebhookConfiguration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific Webhook.
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConfigurationServiceWebhookConfiguration> UpdateServiceWebhookConfiguration(string chatServiceSid,
        string? preWebhookUrl,
        string? postWebhookUrl,
        IReadOnlyList<string>? filters,
        string? method,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Configuration/Webhooks"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PreWebhookUrl", preWebhookUrl),
                    new Param("PostWebhookUrl", postWebhookUrl),
                    new Param("Filters", filters),
                    new Param("Method", method)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConfigurationServiceWebhookConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
