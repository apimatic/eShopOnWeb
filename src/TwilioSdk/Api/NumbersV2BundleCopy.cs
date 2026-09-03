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

public sealed class NumbersV2BundleCopy
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2BundleCopy(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Creates a new copy of a Bundle. It will internally create copies of all the bundle items (identities and documents) of the original bundle
    /// </summary>
    /// <param name="bundleSid">The unique string that identifies the Bundle to be copied.</param>
    /// <param name="friendlyName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceBundleBundleCopy"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a new copy of a Bundle. It will internally create copies of all the bundle items (identities and documents) of the original bundle
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceBundleBundleCopy> CreateBundleCopy(string bundleSid,
        string? friendlyName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles/{BundleSid}/Copies"),
            [new TemplateParam("BundleSid", bundleSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName)]),
            JsonResponse.Create<NumbersV2RegulatoryComplianceBundleBundleCopy>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Bundles Copies for a Bundle.
    /// </summary>
    /// <param name="bundleSid">The unique string that we created to identify the Bundle resource.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListBundleCopyResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Bundles Copies for a Bundle.
    /// </remarks>
    public Task<ListBundleCopyResponse> ListBundleCopy(string bundleSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles/{BundleSid}/Copies"),
            [new TemplateParam("BundleSid", bundleSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListBundleCopyResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
