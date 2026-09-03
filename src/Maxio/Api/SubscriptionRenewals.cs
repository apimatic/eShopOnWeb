using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class SubscriptionRenewals
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SubscriptionRenewals(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Cancel Scheduled Renewal
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="id">The renewal id.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CancelScheduledRenewalConfigurationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Cancels a scheduled renewal configuration.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationResponse> CancelScheduledRenewalConfiguration(int subscriptionId,
        int id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{id}/cancel.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            EmptyBody.Instance,
            JsonResponse.Create<ScheduledRenewalConfigurationResponse>(),
            CancelScheduledRenewalConfigurationErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Scheduled Renewal
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateScheduledRenewalConfigurationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a scheduled renewal configuration for a subscription. The scheduled renewal is based on the subscription’s current product and component setup.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationResponse> CreateScheduledRenewalConfiguration(int subscriptionId,
        ScheduledRenewalConfigurationRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ScheduledRenewalConfigurationResponse>(),
            CreateScheduledRenewalConfigurationErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Scheduled Renewal Configuration Item
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="scheduledRenewalsConfigurationId">The scheduled renewal configuration id.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationItemResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateScheduledRenewalConfigurationItemError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Adds product and component line items to the scheduled renewal.
    /// <para>
    /// If your site has list vs sales pricing enabled, accepts renewal_configuration_item.custom_price.list_price_point_id, validates and persists it; omitted value follows existing/default behavior; with list vs sales pricing disabled, parameter is ignored (no validation/behavioral impact). This functionality is supported in the API, but is not currently supported in SDKs.
    /// </para>
    /// </remarks>
    public Task<ScheduledRenewalConfigurationItemResponse> CreateScheduledRenewalConfigurationItem(int subscriptionId,
        int scheduledRenewalsConfigurationId,
        ScheduledRenewalConfigurationItemRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{scheduled_renewals_configuration_id}/configuration_items.json"),
            [new TemplateParam("subscription_id", subscriptionId),
                new TemplateParam("scheduled_renewals_configuration_id", scheduledRenewalsConfigurationId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ScheduledRenewalConfigurationItemResponse>(),
            CreateScheduledRenewalConfigurationItemErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Scheduled Renewal Configuration Item
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="scheduledRenewalsConfigurationId">The scheduled renewal configuration id.</param>
    /// <param name="id">The scheduled renewal configuration item id.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteScheduledRenewalConfigurationItemError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Removes an item from the pending renewal configuration.
    /// </remarks>
    public Task DeleteScheduledRenewalConfigurationItem(int subscriptionId,
        int scheduledRenewalsConfigurationId,
        int id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{scheduled_renewals_configuration_id}/configuration_items/{id}.json"),
            [new TemplateParam("subscription_id", subscriptionId),
                new TemplateParam("scheduled_renewals_configuration_id", scheduledRenewalsConfigurationId),
                new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeleteScheduledRenewalConfigurationItemErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Scheduled Renewals
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="status">(Optional) Status filter for scheduled renewal configurations.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists scheduled renewal configurations for the subscription and permits an optional status query filter.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationsResponse> ListScheduledRenewalConfigurations(int subscriptionId,
        Status? status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [new Param("status", status)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ScheduledRenewalConfigurationsResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Immediate Renewal Lock-In
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="id">The renewal id.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="LockInScheduledRenewalImmediatelyError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Locks in the renewal immediately.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationResponse> LockInScheduledRenewalImmediately(int subscriptionId,
        int id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{id}/immediate_lock_in.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            EmptyBody.Instance,
            JsonResponse.Create<ScheduledRenewalConfigurationResponse>(),
            LockInScheduledRenewalImmediatelyErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Scheduled Renewal
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="id">The renewal id.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves the configuration settings for the scheduled renewal.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationResponse> ReadScheduledRenewalConfiguration(int subscriptionId,
        int id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{id}.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ScheduledRenewalConfigurationResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Scheduled Renewal Lock-In
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="id">The renewal id.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ScheduleScheduledRenewalLockInError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Schedules a future lock-in date for the renewal.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationResponse> ScheduleScheduledRenewalLockIn(int subscriptionId,
        int id,
        ScheduledRenewalLockInRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{id}/schedule_lock_in.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ScheduledRenewalConfigurationResponse>(),
            ScheduleScheduledRenewalLockInErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Unpublish Scheduled Renewal
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="id">The renewal id.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UnpublishScheduledRenewalConfigurationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Restores a scheduled renewal configuration to an editable state.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationResponse> UnpublishScheduledRenewalConfiguration(int subscriptionId,
        int id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{id}/unpublish.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            EmptyBody.Instance,
            JsonResponse.Create<ScheduledRenewalConfigurationResponse>(),
            UnpublishScheduledRenewalConfigurationErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Scheduled Renewal
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="id">The renewal id.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateScheduledRenewalConfigurationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates an existing configuration.
    /// </remarks>
    public Task<ScheduledRenewalConfigurationResponse> UpdateScheduledRenewalConfiguration(int subscriptionId,
        int id,
        ScheduledRenewalConfigurationRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{id}.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ScheduledRenewalConfigurationResponse>(),
            UpdateScheduledRenewalConfigurationErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Scheduled Renewal Configuration Item
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="scheduledRenewalsConfigurationId">The scheduled renewal configuration id.</param>
    /// <param name="id">The scheduled renewal configuration item id.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScheduledRenewalConfigurationItemResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateScheduledRenewalConfigurationItemError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates an existing configuration item’s pricing and quantity.
    /// <para>
    /// If you site has list vs sales pricing enabled, accepts renewal_configuration_item.custom_price.list_price_point_id, validates and persists it; omitted value follows existing/default behavior; with list vs sales pricing disabled, parameter is ignored (no validation/behavioral impact). This functionality is supported in the API, but is not currently supported in SDKs.
    /// </para>
    /// </remarks>
    public Task<ScheduledRenewalConfigurationItemResponse> UpdateScheduledRenewalConfigurationItem(int subscriptionId,
        int scheduledRenewalsConfigurationId,
        int id,
        ScheduledRenewalUpdateRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/scheduled_renewals/{scheduled_renewals_configuration_id}/configuration_items/{id}.json"),
            [new TemplateParam("subscription_id", subscriptionId),
                new TemplateParam("scheduled_renewals_configuration_id", scheduledRenewalsConfigurationId),
                new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ScheduledRenewalConfigurationItemResponse>(),
            UpdateScheduledRenewalConfigurationItemErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
