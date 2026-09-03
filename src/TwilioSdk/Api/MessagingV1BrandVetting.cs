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

public sealed class MessagingV1BrandVetting
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1BrandVetting(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// A Messaging Service resource to add and get Brand Vettings.
    /// </summary>
    /// <param name="brandSid">The SID of the Brand Registration resource of the vettings to create .</param>
    /// <param name="vettingProvider"></param>
    /// <param name="vettingId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1BrandRegistrationsBrandVetting"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1BrandRegistrationsBrandVetting> CreateBrandVetting(string brandSid,
        BrandVettingEnumVettingProvider vettingProvider,
        string? vettingId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/a2p/BrandRegistrations/{BrandSid}/Vettings"),
            [new TemplateParam("BrandSid", brandSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("VettingProvider", vettingProvider),
                    new Param("VettingId", vettingId)]),
            JsonResponse.Create<MessagingV1BrandRegistrationsBrandVetting>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to add and get Brand Vettings.
    /// </summary>
    /// <param name="brandSid">The SID of the Brand Registration resource of the vettings to read .</param>
    /// <param name="brandVettingSid">The Twilio SID of the third-party vetting record.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1BrandRegistrationsBrandVetting"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1BrandRegistrationsBrandVetting> FetchBrandVetting(string brandSid,
        string brandVettingSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/a2p/BrandRegistrations/{BrandSid}/Vettings/{BrandVettingSid}"),
            [new TemplateParam("BrandSid", brandSid), new TemplateParam("BrandVettingSid", brandVettingSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1BrandRegistrationsBrandVetting>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// A Messaging Service resource to add and get Brand Vettings.
    /// </summary>
    /// <param name="brandSid">The SID of the Brand Registration resource of the vettings to read .</param>
    /// <param name="vettingProvider">The third-party provider of the vettings to read</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListBrandVettingResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListBrandVettingResponse> ListBrandVetting(string brandSid,
        BrandVettingEnumVettingProvider? vettingProvider,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/a2p/BrandRegistrations/{BrandSid}/Vettings"),
            [new TemplateParam("BrandSid", brandSid)],
            [new Param("VettingProvider", vettingProvider)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListBrandVettingResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
