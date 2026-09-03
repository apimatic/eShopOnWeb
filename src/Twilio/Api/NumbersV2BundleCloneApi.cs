using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;

namespace Twilio.Api;

public sealed class NumbersV2BundleCloneApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2BundleCloneApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Creates a new clone of the Bundle in target Account. It will internally create clones of all the bundle items (identities and documents) of the original bundle
    /// </summary>
    /// <param name="bundleSid">The unique string that identifies the Bundle to be cloned.</param>
    /// <param name="targetAccountSid"></param>
    /// <param name="moveToDraft"></param>
    /// <param name="friendlyName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2BundleClone"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a new clone of the Bundle in target Account. It will internally create clones of all the bundle items (identities and documents) of the original bundle
    /// </remarks>
    public Task<NumbersV2BundleClone> CreateBundleClone(string bundleSid,
        string targetAccountSid,
        bool? moveToDraft,
        string? friendlyName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles/{BundleSid}/Clones"),
            [new TemplateParam("BundleSid", bundleSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("TargetAccountSid", targetAccountSid),
                    new Param("MoveToDraft", moveToDraft),
                    new Param("FriendlyName", friendlyName)]),
            JsonResponse.Create<NumbersV2BundleClone>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
