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

public sealed class ConversationsV1Notification
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1Notification(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch push notification service settings
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Configuration applies to.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConfigurationServiceNotification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch push notification service settings
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConfigurationServiceNotification> FetchServiceNotification(string chatServiceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Configuration/Notifications"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceConfigurationServiceNotification>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update push notification service settings
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Configuration applies to.</param>
    /// <param name="logEnabled"></param>
    /// <param name="newMessageEnabled"></param>
    /// <param name="newMessageTemplate"></param>
    /// <param name="newMessageSound"></param>
    /// <param name="newMessageBadgeCountEnabled"></param>
    /// <param name="addedToConversationEnabled"></param>
    /// <param name="addedToConversationTemplate"></param>
    /// <param name="addedToConversationSound"></param>
    /// <param name="removedFromConversationEnabled"></param>
    /// <param name="removedFromConversationTemplate"></param>
    /// <param name="removedFromConversationSound"></param>
    /// <param name="newMessageWithMediaEnabled"></param>
    /// <param name="newMessageWithMediaTemplate"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceConfigurationServiceNotification"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update push notification service settings
    /// </remarks>
    public Task<ConversationsV1ServiceServiceConfigurationServiceNotification> UpdateServiceNotification(string chatServiceSid,
        bool? logEnabled,
        bool? newMessageEnabled,
        string? newMessageTemplate,
        string? newMessageSound,
        bool? newMessageBadgeCountEnabled,
        bool? addedToConversationEnabled,
        string? addedToConversationTemplate,
        string? addedToConversationSound,
        bool? removedFromConversationEnabled,
        string? removedFromConversationTemplate,
        string? removedFromConversationSound,
        bool? newMessageWithMediaEnabled,
        string? newMessageWithMediaTemplate,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Configuration/Notifications"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("LogEnabled", logEnabled),
                    new Param("NewMessage.Enabled", newMessageEnabled),
                    new Param("NewMessage.Template", newMessageTemplate),
                    new Param("NewMessage.Sound", newMessageSound),
                    new Param("NewMessage.BadgeCountEnabled", newMessageBadgeCountEnabled),
                    new Param("AddedToConversation.Enabled", addedToConversationEnabled),
                    new Param("AddedToConversation.Template", addedToConversationTemplate),
                    new Param("AddedToConversation.Sound", addedToConversationSound),
                    new Param("RemovedFromConversation.Enabled", removedFromConversationEnabled),
                    new Param("RemovedFromConversation.Template", removedFromConversationTemplate),
                    new Param("RemovedFromConversation.Sound", removedFromConversationSound),
                    new Param("NewMessage.WithMedia.Enabled", newMessageWithMediaEnabled),
                    new Param("NewMessage.WithMedia.Template", newMessageWithMediaTemplate)]),
            JsonResponse.Create<ConversationsV1ServiceServiceConfigurationServiceNotification>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
