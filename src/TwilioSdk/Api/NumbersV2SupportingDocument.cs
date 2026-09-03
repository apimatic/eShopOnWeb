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

public sealed class NumbersV2SupportingDocument
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2SupportingDocument(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Supporting Document.
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="type"></param>
    /// <param name="attributes"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceSupportingDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Supporting Document.
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceSupportingDocument> CreateSupportingDocument(string friendlyName,
        string type,
        object? attributes,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/SupportingDocuments"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Type", type),
                    new Param("Attributes", attributes)]),
            JsonResponse.Create<NumbersV2RegulatoryComplianceSupportingDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Supporting Document.
    /// </summary>
    /// <param name="sid">The unique string created by Twilio to identify the Supporting Document resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Supporting Document.
    /// </remarks>
    public Task DeleteSupportingDocument(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/SupportingDocuments/{Sid}"),
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
    /// Fetch specific Supporting Document Instance.
    /// </summary>
    /// <param name="sid">The unique string created by Twilio to identify the Supporting Document resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceSupportingDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch specific Supporting Document Instance.
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceSupportingDocument> FetchSupportingDocument(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/SupportingDocuments/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV2RegulatoryComplianceSupportingDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Supporting Document for an account.
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSupportingDocumentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Supporting Document for an account.
    /// </remarks>
    public Task<ListSupportingDocumentResponse> ListSupportingDocument(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/SupportingDocuments"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSupportingDocumentResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an existing Supporting Document.
    /// </summary>
    /// <param name="sid">The unique string created by Twilio to identify the Supporting Document resource.</param>
    /// <param name="friendlyName"></param>
    /// <param name="attributes"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceSupportingDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an existing Supporting Document.
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceSupportingDocument> UpdateSupportingDocument(string sid,
        string? friendlyName,
        object? attributes,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/SupportingDocuments/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Attributes", attributes)]),
            JsonResponse.Create<NumbersV2RegulatoryComplianceSupportingDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
