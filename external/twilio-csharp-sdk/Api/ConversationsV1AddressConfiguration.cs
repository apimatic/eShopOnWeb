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

public sealed class ConversationsV1AddressConfiguration
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1AddressConfiguration(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new address configuration
    /// </summary>
    /// <param name="type"></param>
    /// <param name="address"></param>
    /// <param name="friendlyName"></param>
    /// <param name="autoCreationEnabled"></param>
    /// <param name="autoCreationType"></param>
    /// <param name="autoCreationConversationServiceSid"></param>
    /// <param name="autoCreationWebhookUrl"></param>
    /// <param name="autoCreationWebhookMethod"></param>
    /// <param name="autoCreationWebhookFilters"></param>
    /// <param name="autoCreationStudioFlowSid"></param>
    /// <param name="autoCreationStudioRetryCount"></param>
    /// <param name="addressCountry"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConfigurationAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new address configuration
    /// </remarks>
    public Task<ConversationsV1ConfigurationAddress> CreateConfigurationAddress(ConfigurationAddressEnumType type,
        string address,
        string? friendlyName,
        bool? autoCreationEnabled,
        ConfigurationAddressEnumAutoCreationType? autoCreationType,
        string? autoCreationConversationServiceSid,
        string? autoCreationWebhookUrl,
        ConfigurationAddressEnumMethod? autoCreationWebhookMethod,
        IReadOnlyList<string>? autoCreationWebhookFilters,
        string? autoCreationStudioFlowSid,
        int? autoCreationStudioRetryCount,
        string? addressCountry,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration/Addresses"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Type", type),
                    new Param("Address", address),
                    new Param("FriendlyName", friendlyName),
                    new Param("AutoCreation.Enabled", autoCreationEnabled),
                    new Param("AutoCreation.Type", autoCreationType),
                    new Param("AutoCreation.ConversationServiceSid", autoCreationConversationServiceSid),
                    new Param("AutoCreation.WebhookUrl", autoCreationWebhookUrl),
                    new Param("AutoCreation.WebhookMethod", autoCreationWebhookMethod),
                    new Param("AutoCreation.WebhookFilters", autoCreationWebhookFilters),
                    new Param("AutoCreation.StudioFlowSid", autoCreationStudioFlowSid),
                    new Param("AutoCreation.StudioRetryCount", autoCreationStudioRetryCount),
                    new Param("AddressCountry", addressCountry)]),
            JsonResponse.Create<ConversationsV1ConfigurationAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove an existing address configuration
    /// </summary>
    /// <param name="sid">The SID of the Address Configuration resource. This value can be either the <c>sid</c> or the <c>address</c> of the configuration</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove an existing address configuration
    /// </remarks>
    public Task DeleteConfigurationAddress(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration/Addresses/{Sid}"),
            [new TemplateParam("Sid", sid)],
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
    /// Fetch an address configuration
    /// </summary>
    /// <param name="sid">The SID of the Address Configuration resource. This value can be either the <c>sid</c> or the <c>address</c> of the configuration</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConfigurationAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an address configuration
    /// </remarks>
    public Task<ConversationsV1ConfigurationAddress> FetchConfigurationAddress(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration/Addresses/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ConfigurationAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of address configurations for an account
    /// </summary>
    /// <param name="type">Filter the address configurations by its type. This value can be one of: <c>whatsapp</c>, <c>sms</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConfigurationAddressResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of address configurations for an account
    /// </remarks>
    public Task<ListConfigurationAddressResponse> ListConfigurationAddress(string? type,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration/Addresses"),
            [],
            [new Param("Type", type),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConfigurationAddressResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing address configuration
    /// </summary>
    /// <param name="sid">The SID of the Address Configuration resource. This value can be either the <c>sid</c> or the <c>address</c> of the configuration</param>
    /// <param name="friendlyName"></param>
    /// <param name="autoCreationEnabled"></param>
    /// <param name="autoCreationType"></param>
    /// <param name="autoCreationConversationServiceSid"></param>
    /// <param name="autoCreationWebhookUrl"></param>
    /// <param name="autoCreationWebhookMethod"></param>
    /// <param name="autoCreationWebhookFilters"></param>
    /// <param name="autoCreationStudioFlowSid"></param>
    /// <param name="autoCreationStudioRetryCount"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConfigurationAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing address configuration
    /// </remarks>
    public Task<ConversationsV1ConfigurationAddress> UpdateConfigurationAddress(string sid,
        string? friendlyName,
        bool? autoCreationEnabled,
        ConfigurationAddressEnumAutoCreationType? autoCreationType,
        string? autoCreationConversationServiceSid,
        string? autoCreationWebhookUrl,
        ConfigurationAddressEnumMethod? autoCreationWebhookMethod,
        IReadOnlyList<string>? autoCreationWebhookFilters,
        string? autoCreationStudioFlowSid,
        int? autoCreationStudioRetryCount,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration/Addresses/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("AutoCreation.Enabled", autoCreationEnabled),
                    new Param("AutoCreation.Type", autoCreationType),
                    new Param("AutoCreation.ConversationServiceSid", autoCreationConversationServiceSid),
                    new Param("AutoCreation.WebhookUrl", autoCreationWebhookUrl),
                    new Param("AutoCreation.WebhookMethod", autoCreationWebhookMethod),
                    new Param("AutoCreation.WebhookFilters", autoCreationWebhookFilters),
                    new Param("AutoCreation.StudioFlowSid", autoCreationStudioFlowSid),
                    new Param("AutoCreation.StudioRetryCount", autoCreationStudioRetryCount)]),
            JsonResponse.Create<ConversationsV1ConfigurationAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
