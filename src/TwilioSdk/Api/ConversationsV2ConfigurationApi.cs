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
/// A conversation configuration is the top-level object in Conversation Orchestrator. It contains the settings that define how Conversation Orchestrator captures traffic and connects to other services.
/// </summary>
public sealed class ConversationsV2ConfigurationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ConversationsV2ConfigurationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a Configuration
    /// </summary>
    /// <param name="idempotencyKey">Client-generated UUID key to ensure idempotent behavior. Submitting the same key returns the original response without creating a duplicate operation. Keys are scoped to account + region with a 24-hour TTL.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2OperationAccepted"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateConfigurationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Configuration
    /// </remarks>
    public Task<ConversationsV2OperationAccepted> CreateConfiguration(string? idempotencyKey,
        V2ControlPlaneConfigurationsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/ControlPlane/Configurations"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", idempotencyKey)],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2OperationAccepted>(),
            CreateConfigurationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Configuration
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="idempotencyKey">Client-generated UUID key to ensure idempotent behavior. Submitting the same key returns the original response without creating a duplicate operation. Keys are scoped to account + region with a 24-hour TTL.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2OperationAccepted"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteConfigurationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a Configuration
    /// </remarks>
    public Task<ConversationsV2OperationAccepted> DeleteConfiguration(string sid,
        string? idempotencyKey,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/ControlPlane/Configurations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", idempotencyKey)],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2OperationAccepted>(),
            DeleteConfigurationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch Configuration
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2Configuration"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchConfiguration2Error"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a Configuration.
    /// </remarks>
    public Task<ConversationsV2Configuration> FetchConfiguration2(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/ControlPlane/Configurations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ConversationsV2Configuration>(),
            FetchConfiguration2ErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List Configurations
    /// </summary>
    /// <param name="pageToken">A URL-safe, base64-encoded token representing the page of results to return</param>
    /// <param name="memoryStoreId">Filter configurations by Memory Store ID</param>
    /// <param name="pageSize">Maximum number of items to return in a single response</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="V2ControlPlaneConfigurationsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListConfigurationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Configurations.
    /// </remarks>
    public Task<V2ControlPlaneConfigurationsResponse> ListConfiguration(string? pageToken,
        string? memoryStoreId,
        int? pageSize = 50,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/ControlPlane/Configurations"),
            [],
            [new Param("pageSize", pageSize),
                new Param("pageToken", pageToken),
                new Param("memoryStoreId", memoryStoreId)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<V2ControlPlaneConfigurationsResponse>(),
            ListConfigurationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update Configuration
    /// </summary>
    /// <param name="sid"></param>
    /// <param name="idempotencyKey">Client-generated UUID key to ensure idempotent behavior. Submitting the same key returns the original response without creating a duplicate operation. Keys are scoped to account + region with a 24-hour TTL.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ConversationsV2OperationAccepted"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateConfiguration2Error"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing Configuration
    /// </remarks>
    public Task<ConversationsV2OperationAccepted> UpdateConfiguration2(string sid,
        string? idempotencyKey,
        V2ControlPlaneConfigurationsRequest1? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default7("/v2/ControlPlane/Configurations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", idempotencyKey)],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ConversationsV2OperationAccepted>(),
            UpdateConfiguration2ErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
