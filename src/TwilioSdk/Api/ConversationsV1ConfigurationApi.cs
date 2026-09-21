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

namespace TwilioSdk.Api;

public sealed class ConversationsV1ConfigurationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1ConfigurationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch the global configuration of conversations on your account
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Configuration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the global configuration of conversations on your account
    /// </remarks>
    public Task<ConversationsV1Configuration> FetchConfiguration(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1Configuration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch the configuration of a conversation service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the Service configuration resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConfiguration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the configuration of a conversation service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConfiguration> FetchServiceConfiguration(string chatServiceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Configuration"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update the global configuration of conversations on your account
    /// </summary>
    /// <param name="defaultChatServiceSid"></param>
    /// <param name="defaultMessagingServiceSid"></param>
    /// <param name="defaultInactiveTimer"></param>
    /// <param name="defaultClosedTimer"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1Configuration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update the global configuration of conversations on your account
    /// </remarks>
    public Task<ConversationsV1Configuration> UpdateConfiguration(string? defaultChatServiceSid,
        string? defaultMessagingServiceSid,
        string? defaultInactiveTimer,
        string? defaultClosedTimer,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Configuration"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("DefaultChatServiceSid", defaultChatServiceSid),
                    new Param("DefaultMessagingServiceSid", defaultMessagingServiceSid),
                    new Param("DefaultInactiveTimer", defaultInactiveTimer),
                    new Param("DefaultClosedTimer", defaultClosedTimer)]),
            JsonResponse.Create<ConversationsV1Configuration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update configuration settings of a conversation service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the Service configuration resource to update.</param>
    /// <param name="defaultConversationCreatorRoleSid"></param>
    /// <param name="defaultConversationRoleSid"></param>
    /// <param name="defaultChatServiceRoleSid"></param>
    /// <param name="reachabilityEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConfiguration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update configuration settings of a conversation service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConfiguration> UpdateServiceConfiguration(string chatServiceSid,
        string? defaultConversationCreatorRoleSid,
        string? defaultConversationRoleSid,
        string? defaultChatServiceRoleSid,
        bool? reachabilityEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Configuration"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("DefaultConversationCreatorRoleSid",
                        defaultConversationCreatorRoleSid),
                    new Param("DefaultConversationRoleSid", defaultConversationRoleSid),
                    new Param("DefaultChatServiceRoleSid", defaultChatServiceRoleSid),
                    new Param("ReachabilityEnabled", reachabilityEnabled)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
