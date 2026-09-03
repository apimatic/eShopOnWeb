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

public sealed class TrusthubV1ComplianceInquiries
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TrusthubV1ComplianceInquiries(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Compliance Inquiry for the authenticated account. This is necessary to start a new embedded session.
    /// </summary>
    /// <param name="notificationEmail"></param>
    /// <param name="themeSetId"></param>
    /// <param name="primaryProfileSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1ComplianceInquiry"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Compliance Inquiry for the authenticated account. This is necessary to start a new embedded session.
    /// </remarks>
    public Task<TrusthubV1ComplianceInquiry> CreateComplianceInquiry(string? notificationEmail,
        string? themeSetId,
        string? primaryProfileSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/ComplianceInquiries/Customers/Initialize"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("NotificationEmail", notificationEmail),
                    new Param("ThemeSetId", themeSetId),
                    new Param("PrimaryProfileSid", primaryProfileSid)]),
            JsonResponse.Create<TrusthubV1ComplianceInquiry>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Resume a specific Compliance Inquiry that has expired, or re-open a rejected Compliance Inquiry for editing.
    /// </summary>
    /// <param name="customerId">The unique CustomerId matching the Customer Profile/Compliance Inquiry that should be resumed or resubmitted. This value will have been returned by the initial Compliance Inquiry creation call.</param>
    /// <param name="primaryProfileSid"></param>
    /// <param name="themeSetId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TrusthubV1ComplianceInquiry"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Resume a specific Compliance Inquiry that has expired, or re-open a rejected Compliance Inquiry for editing.
    /// </remarks>
    public Task<TrusthubV1ComplianceInquiry> UpdateComplianceInquiry(string customerId,
        string primaryProfileSid,
        string? themeSetId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default9("/v1/ComplianceInquiries/Customers/{CustomerId}/Initialize"),
            [new TemplateParam("CustomerId", customerId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("PrimaryProfileSid", primaryProfileSid),
                    new Param("ThemeSetId", themeSetId)]),
            JsonResponse.Create<TrusthubV1ComplianceInquiry>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
