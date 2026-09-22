using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Extensions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class NumbersV2Bundle
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2Bundle(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Bundle.
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="email"></param>
    /// <param name="statusCallback"></param>
    /// <param name="regulationSid"></param>
    /// <param name="isoCountry"></param>
    /// <param name="endUserType"></param>
    /// <param name="numberType"></param>
    /// <param name="isTest"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceBundle"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Bundle.
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceBundle> CreateBundle(string friendlyName,
        string email,
        string? statusCallback,
        string? regulationSid,
        string? isoCountry,
        BundleEnumEndUserType? endUserType,
        string? numberType,
        bool? isTest,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Email", email),
                    new Param("StatusCallback", statusCallback),
                    new Param("RegulationSid", regulationSid),
                    new Param("IsoCountry", isoCountry),
                    new Param("EndUserType", endUserType),
                    new Param("NumberType", numberType),
                    new Param("IsTest", isTest)]),
            JsonResponse.Create<NumbersV2RegulatoryComplianceBundle>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Bundle.
    /// </summary>
    /// <param name="sid">The unique string that we created to identify the Bundle resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Bundle.
    /// </remarks>
    public Task DeleteBundle(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles/{Sid}"),
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
    /// Fetch a specific Bundle instance.
    /// </summary>
    /// <param name="sid">The unique string that we created to identify the Bundle resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceBundle"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Bundle instance.
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceBundle> FetchBundle(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV2RegulatoryComplianceBundle>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Bundles for an account.
    /// </summary>
    /// <param name="status">The verification status of the Bundle resource. Please refer to <see href="https://www.twilio.com/docs/phone-numbers/regulatory/api/bundles#bundle-statuses">Bundle Statuses</see> for more details.</param>
    /// <param name="bundleSids">A comma-separated list of Bundle SIDs to filter the results (maximum 20). Each Bundle SID must match <c>^BU[0-9a-fA-F]{32}$</c>.</param>
    /// <param name="friendlyName">The string that you assigned to describe the resource. The column can contain 255 variable characters.</param>
    /// <param name="regulationSid">The unique string of a <see href="https://www.twilio.com/docs/phone-numbers/regulatory/api/regulations">Regulation resource</see> that is associated to the Bundle resource.</param>
    /// <param name="isoCountry">The 2-digit <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO country code</see> of the Bundle's phone number country ownership request.</param>
    /// <param name="numberType">The type of phone number of the Bundle's ownership request. Can be <c>local</c>, <c>mobile</c>, <c>national</c>, or <c>toll-free</c>.</param>
    /// <param name="endUserType">The end user type of the regulation of the Bundle. Can be <c>business</c> or <c>individual</c>.</param>
    /// <param name="hasValidUntilDate">Indicates that the Bundle is a valid Bundle until a specified expiration date.</param>
    /// <param name="sortBy">Can be <c>valid-until</c> or <c>date-updated</c>. Defaults to <c>date-created</c>.</param>
    /// <param name="sortDirection">Default is <c>DESC</c>. Can be <c>ASC</c> or <c>DESC</c>.</param>
    /// <param name="validUntilDate">Date to filter Bundles having their <c>valid_until_date</c> before or after the specified date. Can be <c>ValidUntilDate&gt;=</c> or <c>ValidUntilDate&lt;=</c>. Both can be used in conjunction as well. <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> is the acceptable date format.</param>
    /// <param name="validUntilDateQuery">Date to filter Bundles having their <c>valid_until_date</c> before or after the specified date. Can be <c>ValidUntilDate&gt;=</c> or <c>ValidUntilDate&lt;=</c>. Both can be used in conjunction as well. <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> is the acceptable date format.</param>
    /// <param name="validUntilDateQueryQuery">Date to filter Bundles having their <c>valid_until_date</c> before or after the specified date. Can be <c>ValidUntilDate&gt;=</c> or <c>ValidUntilDate&lt;=</c>. Both can be used in conjunction as well. <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> is the acceptable date format.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListBundleResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Bundles for an account.
    /// </remarks>
    public Task<ListBundleResponse> ListBundle(BundleEnumStatus? status,
        string? bundleSids,
        string? friendlyName,
        string? regulationSid,
        string? isoCountry,
        string? numberType,
        BundleEnumEndUserType? endUserType,
        bool? hasValidUntilDate,
        BundleEnumSortBy? sortBy,
        BundleEnumSortDirection? sortDirection,
        DateTimeOffset? validUntilDate,
        DateTimeOffset? validUntilDateQuery,
        DateTimeOffset? validUntilDateQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles"),
            [],
            [new Param("Status", status),
                new Param("BundleSids", bundleSids),
                new Param("FriendlyName", friendlyName),
                new Param("RegulationSid", regulationSid),
                new Param("IsoCountry", isoCountry),
                new Param("NumberType", numberType),
                new Param("EndUserType", endUserType),
                new Param("HasValidUntilDate", hasValidUntilDate),
                new Param("SortBy", sortBy),
                new Param("SortDirection", sortDirection),
                new Param("ValidUntilDate", validUntilDate?.ToIso8601()),
                new Param("ValidUntilDate<", validUntilDateQuery?.ToIso8601()),
                new Param("ValidUntilDate>", validUntilDateQueryQuery?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListBundleResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Updates a Bundle in an account.
    /// </summary>
    /// <param name="sid">The unique string that we created to identify the Bundle resource.</param>
    /// <param name="status"></param>
    /// <param name="statusCallback"></param>
    /// <param name="friendlyName"></param>
    /// <param name="email"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV2RegulatoryComplianceBundle"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a Bundle in an account.
    /// </remarks>
    public Task<NumbersV2RegulatoryComplianceBundle> UpdateBundle(string sid,
        BundleEnumStatus? status,
        string? statusCallback,
        string? friendlyName,
        string? email,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/RegulatoryCompliance/Bundles/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status),
                    new Param("StatusCallback", statusCallback),
                    new Param("FriendlyName", friendlyName),
                    new Param("Email", email)]),
            JsonResponse.Create<NumbersV2RegulatoryComplianceBundle>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
