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

public sealed class ConversationsV1ParticipantConversationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1ParticipantConversationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Retrieve a list of all Conversations that this Participant belongs to by identity or by address. Only one parameter should be specified.
    /// </summary>
    /// <param name="identity">A unique string identifier for the conversation participant as <see href="https://www.twilio.com/docs/conversations/api/user-resource">Conversation User</see>. This parameter is non-null if (and only if) the participant is using the Conversations SDK to communicate. Limited to 256 characters.</param>
    /// <param name="address">A unique string identifier for the conversation participant who's not a Conversation User. This parameter could be found in messaging_binding.address field of Participant resource. It should be url-encoded.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListParticipantConversationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Conversations that this Participant belongs to by identity or by address. Only one parameter should be specified.
    /// </remarks>
    public Task<ListParticipantConversationResponse> ListParticipantConversation(string? identity,
        string? address,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/ParticipantConversations"),
            [],
            [new Param("Identity", identity),
                new Param("Address", address),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListParticipantConversationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Conversations that this Participant belongs to by identity or by address. Only one parameter should be specified.
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Participant Conversations resource is associated with.</param>
    /// <param name="identity">A unique string identifier for the conversation participant as <see href="https://www.twilio.com/docs/conversations/api/user-resource">Conversation User</see>. This parameter is non-null if (and only if) the participant is using the Conversations SDK to communicate. Limited to 256 characters.</param>
    /// <param name="address">A unique string identifier for the conversation participant who's not a Conversation User. This parameter could be found in messaging_binding.address field of Participant resource. It should be url-encoded.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceParticipantConversationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Conversations that this Participant belongs to by identity or by address. Only one parameter should be specified.
    /// </remarks>
    public Task<ListServiceParticipantConversationResponse> ListServiceParticipantConversation(string chatServiceSid,
        string? identity,
        string? address,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/ParticipantConversations"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [new Param("Identity", identity),
                new Param("Address", address),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceParticipantConversationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
