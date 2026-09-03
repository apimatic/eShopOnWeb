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

public sealed class VideoV1RecordingSettingsApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1RecordingSettingsApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Recording settings
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="awsCredentialsSid"></param>
    /// <param name="encryptionKeySid"></param>
    /// <param name="awsS3Url"></param>
    /// <param name="awsStorageEnabled"></param>
    /// <param name="encryptionEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RecordingSettings"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RecordingSettings> CreateRecordingSettings(string friendlyName,
        string? awsCredentialsSid,
        string? encryptionKeySid,
        string? awsS3Url,
        bool? awsStorageEnabled,
        bool? encryptionEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/RecordingSettings/Default"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("AwsCredentialsSid", awsCredentialsSid),
                    new Param("EncryptionKeySid", encryptionKeySid),
                    new Param("AwsS3Url", awsS3Url),
                    new Param("AwsStorageEnabled", awsStorageEnabled),
                    new Param("EncryptionEnabled", encryptionEnabled)]),
            JsonResponse.Create<VideoV1RecordingSettings>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Recording settings
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1RecordingSettings"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1RecordingSettings> FetchRecordingSettings(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/RecordingSettings/Default"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1RecordingSettings>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
