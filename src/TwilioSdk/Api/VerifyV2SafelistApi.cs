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

public sealed class VerifyV2SafelistApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2SafelistApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Add a new phone number to SafeList.
    /// </summary>
    /// <param name="phoneNumber"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2Safelist"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Add a new phone number to SafeList.
    /// </remarks>
    public Task<VerifyV2Safelist> CreateSafelist(string phoneNumber,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/SafeList/Numbers"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PhoneNumber", phoneNumber)]),
            JsonResponse.Create<VerifyV2Safelist>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Remove a phone number from SafeList.
    /// </summary>
    /// <param name="phoneNumber">The phone number to be removed from SafeList. Phone numbers must be in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164 format</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Remove a phone number from SafeList.
    /// </remarks>
    public Task DeleteSafelist(string phoneNumber,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/SafeList/Numbers/{PhoneNumber}"),
            [new TemplateParam("PhoneNumber", phoneNumber)],
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
    /// Check if a phone number exists in SafeList.
    /// </summary>
    /// <param name="phoneNumber">The phone number to be fetched from SafeList. Phone numbers must be in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164 format</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2Safelist"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Check if a phone number exists in SafeList.
    /// </remarks>
    public Task<VerifyV2Safelist> FetchSafelist(string phoneNumber,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/SafeList/Numbers/{PhoneNumber}"),
            [new TemplateParam("PhoneNumber", phoneNumber)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2Safelist>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
