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

public sealed class ConversationsV1UserApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV1UserApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add a new conversation user to your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the User resource is associated with.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="identity"></param>
    /// <param name="friendlyName"></param>
    /// <param name="attributes"></param>
    /// <param name="roleSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceUser"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new conversation user to your service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceUser> CreateServiceUser(string chatServiceSid,
        Confirmation? xTwilioWebhookEnabled,
        string identity,
        string? friendlyName,
        string? attributes,
        string? roleSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Identity", identity),
                    new Param("FriendlyName", friendlyName),
                    new Param("Attributes", attributes),
                    new Param("RoleSid", roleSid)]),
            JsonResponse.Create<ConversationsV1ServiceServiceUser>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Add a new conversation user to your account's default service
    /// </summary>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="identity"></param>
    /// <param name="friendlyName"></param>
    /// <param name="attributes"></param>
    /// <param name="roleSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1User"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new conversation user to your account's default service
    /// </remarks>
    public Task<ConversationsV1User> CreateUser(Confirmation? xTwilioWebhookEnabled,
        string identity,
        string? friendlyName,
        string? attributes,
        string? roleSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users"),
            [],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Identity", identity),
                    new Param("FriendlyName", friendlyName),
                    new Param("Attributes", attributes),
                    new Param("RoleSid", roleSid)]),
            JsonResponse.Create<ConversationsV1User>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove a conversation user from your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> to delete the User resource from.</param>
    /// <param name="sid">The SID of the User resource to delete. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource to delete.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a conversation user from your service
    /// </remarks>
    public Task DeleteServiceUser(string chatServiceSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users/{Sid}"),
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
    /// Remove a conversation user from your account's default service
    /// </summary>
    /// <param name="sid">The SID of the User resource to delete. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource to delete.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a conversation user from your account's default service
    /// </remarks>
    public Task DeleteUser(string sid,
        Confirmation? xTwilioWebhookEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users/{Sid}"),
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
    /// Fetch a conversation user from your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> to fetch the User resource from.</param>
    /// <param name="sid">The SID of the User resource to fetch. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceUser"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a conversation user from your service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceUser> FetchServiceUser(string chatServiceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1ServiceServiceUser>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a conversation user from your account's default service
    /// </summary>
    /// <param name="sid">The SID of the User resource to fetch. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1User"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a conversation user from your account's default service
    /// </remarks>
    public Task<ConversationsV1User> FetchUser(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV1User>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all conversation users in your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> to read the User resources from.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceUserResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all conversation users in your service
    /// </remarks>
    public Task<ListServiceUserResponse> ListServiceUser(string chatServiceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users"),
            [new TemplateParam("ChatServiceSid", chatServiceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceUserResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all conversation users in your account's default service
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 50.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListUserResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all conversation users in your account's default service
    /// </remarks>
    public Task<ListUserResponse> ListUser(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListUserResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing conversation user in your service
    /// </summary>
    /// <param name="chatServiceSid">The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the User resource is associated with.</param>
    /// <param name="sid">The SID of the User resource to update. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource to update.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="attributes"></param>
    /// <param name="roleSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1ServiceServiceUser"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing conversation user in your service
    /// </remarks>
    public Task<ConversationsV1ServiceServiceUser> UpdateServiceUser(string chatServiceSid,
        string sid,
        Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        string? attributes,
        string? roleSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Services/{ChatServiceSid}/Users/{Sid}"),
            [new TemplateParam("ChatServiceSid", chatServiceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Attributes", attributes),
                    new Param("RoleSid", roleSid)]),
            JsonResponse.Create<ConversationsV1ServiceServiceUser>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing conversation user in your account's default service
    /// </summary>
    /// <param name="sid">The SID of the User resource to update. This value can be either the <c>sid</c> or the <c>identity</c> of the User resource to update.</param>
    /// <param name="xTwilioWebhookEnabled">The X-Twilio-Webhook-Enabled HTTP request header</param>
    /// <param name="friendlyName"></param>
    /// <param name="attributes"></param>
    /// <param name="roleSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV1User"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing conversation user in your account's default service
    /// </remarks>
    public Task<ConversationsV1User> UpdateUser(string sid,
        Confirmation? xTwilioWebhookEnabled,
        string? friendlyName,
        string? attributes,
        string? roleSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v1/Users/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("X-Twilio-Webhook-Enabled", xTwilioWebhookEnabled),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Attributes", attributes),
                    new Param("RoleSid", roleSid)]),
            JsonResponse.Create<ConversationsV1User>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
