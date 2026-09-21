using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Errors;
using TwilioSdk.Models;

namespace TwilioSdk.Api;

/// <summary>
/// A participant represents an actor involved in a conversation. Conversation Orchestrator assigns each participant a type that identifies their role, such as customer, human agent, or AI agent.
/// </summary>
public sealed class ConversationsV2ParticipantApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV2ParticipantApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Participant
    /// </summary>
    /// <param name="conversationSid"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Participant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateParticipantInConversationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a Participant.
    /// </remarks>
    public Task<ConversationsV2Participant> CreateParticipantInConversation(string conversationSid,
        V2ConversationsParticipantsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationSid}/Participants"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2Participant>(),
            CreateParticipantInConversationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch Participant
    /// </summary>
    /// <param name="conversationSid"></param>
    /// <param name="sid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Participant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchParticipant2Error"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a Participant.
    /// </remarks>
    public Task<ConversationsV2Participant> FetchParticipant2(string conversationSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2Participant>(),
            FetchParticipant2ErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List Participants
    /// </summary>
    /// <param name="conversationSid"></param>
    /// <param name="pageToken">Page token for pagination</param>
    /// <param name="pageSize">Maximum number of items to return</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="V2ConversationsParticipantsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListParticipantByConversationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Participants in a Conversation.
    /// </remarks>
    public Task<V2ConversationsParticipantsResponse> ListParticipantByConversation(string conversationSid,
        string? pageToken,
        int? pageSize = 50,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationSid}/Participants"),
            [new TemplateParam("ConversationSid", conversationSid)],
            [new Param("pageSize", pageSize), new Param("pageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<V2ConversationsParticipantsResponse>(),
            ListParticipantByConversationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a Participant
    /// </summary>
    /// <param name="conversationSid"></param>
    /// <param name="sid"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Participant"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateParticipantInConversationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing Participant
    /// </remarks>
    public Task<ConversationsV2Participant> UpdateParticipantInConversation(string conversationSid,
        string sid,
        V2ConversationsParticipantsRequest1? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/Conversations/{ConversationSid}/Participants/{Sid}"),
            [new TemplateParam("ConversationSid", conversationSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2Participant>(),
            UpdateParticipantInConversationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
