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

public sealed class SyncV1SyncListPermission
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SyncV1SyncListPermission(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a specific Sync List Permission.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync List Permission resource to delete.</param>
    /// <param name="listSid">The SID of the Sync List with the Sync List Permission resource to delete. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="identity">The application-defined string that uniquely identifies the User's Sync List Permission resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Sync List Permission.
    /// </remarks>
    public Task DeleteSyncListPermission(string serviceSid,
        string listSid,
        string identity,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Permissions/{Identity}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("ListSid", listSid),
                new TemplateParam("Identity", identity)],
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
    /// Fetch a specific Sync List Permission.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync List Permission resource to fetch.</param>
    /// <param name="listSid">The SID of the Sync List with the Sync List Permission resource to fetch. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="identity">The application-defined string that uniquely identifies the User's Sync List Permission resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncListSyncListPermission"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Sync List Permission.
    /// </remarks>
    public Task<SyncV1ServiceSyncListSyncListPermission> FetchSyncListPermission(string serviceSid,
        string listSid,
        string identity,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Permissions/{Identity}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("ListSid", listSid),
                new TemplateParam("Identity", identity)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SyncV1ServiceSyncListSyncListPermission>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Permissions applying to a Sync List.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync List Permission resources to read.</param>
    /// <param name="listSid">The SID of the Sync List with the Sync List Permission resources to read. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSyncListPermissionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Permissions applying to a Sync List.
    /// </remarks>
    public Task<ListSyncListPermissionResponse> ListSyncListPermission(string serviceSid,
        string listSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Permissions"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("ListSid", listSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSyncListPermissionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an identity's access to a specific Sync List.
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync List Permission resource to update.</param>
    /// <param name="listSid">The SID of the Sync List with the Sync List Permission resource to update. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="identity">The application-defined string that uniquely identifies the User's Sync List Permission resource to update.</param>
    /// <param name="read"></param>
    /// <param name="write"></param>
    /// <param name="manage"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncListSyncListPermission"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an identity's access to a specific Sync List.
    /// </remarks>
    public Task<SyncV1ServiceSyncListSyncListPermission> UpdateSyncListPermission(string serviceSid,
        string listSid,
        string identity,
        bool read,
        bool write,
        bool manage,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Permissions/{Identity}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("ListSid", listSid),
                new TemplateParam("Identity", identity)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Read", read),
                    new Param("Write", write),
                    new Param("Manage", manage)]),
            JsonResponse.Create<SyncV1ServiceSyncListSyncListPermission>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
