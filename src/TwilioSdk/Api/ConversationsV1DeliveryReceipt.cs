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

public sealed class ConversationsV1DeliveryReceipt
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1DeliveryReceipt(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch the delivery and read receipts of the conversation message
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="messageSid">The SID of the message within a <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> the delivery receipt belongs to.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ConversationConversationMessageConversationMessageReceipt"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the delivery and read receipts of the conversation message
    /// </remarks>
    public Task<ConversationsV1ConversationConversationMessageConversationMessageReceipt> FetchConversationMessageReceipt(string conversationSid,
        string messageSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Messages/{MessageSid}/Receipts/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("MessageSid", messageSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ConversationConversationMessageConversationMessageReceipt>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch the delivery and read receipts of the conversation message
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Message resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="messageSid">The SID of the message within a <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> the delivery receipt belongs to.</param>
    /// <param name="sid">A 34 character string that uniquely identifies this resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ServiceConversationMessageReceipt"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch the delivery and read receipts of the conversation message
    /// </remarks>
    public Task<ServiceConversationMessageReceipt> FetchServiceConversationMessageReceipt(string chatServiceSid,
        string conversationSid,
        string messageSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Messages/{MessageSid}/Receipts/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("MessageSid", messageSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ServiceConversationMessageReceipt>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all delivery and read receipts of the conversation message
    /// </summary>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="messageSid">The SID of the message within a <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> the delivery receipt belongs to.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConversationMessageReceiptResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all delivery and read receipts of the conversation message
    /// </remarks>
    public Task<ListConversationMessageReceiptResponse> ListConversationMessageReceipt(string conversationSid,
        string messageSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Conversations/{ConversationSid}/Messages/{MessageSid}/Receipts"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("MessageSid", messageSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConversationMessageReceiptResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all delivery and read receipts of the conversation message
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Message resource is associated with.</param>
    /// <param name="conversationSid">The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.</param>
    /// <param name="messageSid">The SID of the message within a <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> the delivery receipt belongs to.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceConversationMessageReceiptResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all delivery and read receipts of the conversation message
    /// </remarks>
    public Task<ListServiceConversationMessageReceiptResponse> ListServiceConversationMessageReceipt(string chatServiceSid,
        string conversationSid,
        string messageSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Conversations/{ConversationSid}/Messages/{MessageSid}/Receipts"),
            [new TemplateParam("ChatServiceSid", chatServiceSid),
                new TemplateParam("ConversationSid", conversationSid),
                new TemplateParam("MessageSid", messageSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceConversationMessageReceiptResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
