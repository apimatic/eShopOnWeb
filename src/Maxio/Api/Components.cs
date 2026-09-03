using System;
using System.Collections.Generic;
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

public sealed class Components
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Components(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Archive Component
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the component belongs</param>
    /// <param name="componentId">Either the Advanced Billing id of the component or the handle for the component prefixed with <c>handle:</c></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Component"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ArchiveComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Archives the component; all current subscribers will continue to be charged as usual.
    /// </remarks>
    public Task<Component> ArchiveComponent(int productFamilyId,
        string componentId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/components/{component_id}.json"),
            [new TemplateParam("product_family_id", productFamilyId), new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<Component>(),
            ArchiveComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Event Based Component
    /// </summary>
    /// <param name="productFamilyId">Either the product family's id or its handle prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateEventBasedComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an event-based component definition under the specified product family. An event-based component can then be added and “allocated” for a subscription.
    /// <para>
    /// Event-based components are similar to other component types, in that you define the component parameters (such as name and taxability) and the pricing. A key difference for the event-based component is that it must be attached to a metric. This is because the metric provides the component with the actual quantity used in computing what and how much will be billed each period for each subscription.
    /// </para>
    /// <para>
    /// So, instead of reporting usage directly for each component (as you would with metered components), the usage is derived from analysis of your events.
    /// </para>
    /// <para>
    /// For more information on components, see our documentation <see href="https://maxio.zendesk.com/hc/en-us/articles/24261141522189-Components-Overview">here</see>.
    /// </para>
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, taxable components must include a non-blank <c>tax_code</c>. Sending <c>"tax_code": ""</c> returns <c>422</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> CreateEventBasedComponent(string productFamilyId,
        CreateEbbComponent? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/event_based_components.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentResponse>(),
            CreateEventBasedComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Metered Component
    /// </summary>
    /// <param name="productFamilyId">Either the product family's id or its handle prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateMeteredComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a metered component definition under the specified product family. A metered component can then be added and “allocated” for a subscription.
    /// <para>
    /// Metered components are used to bill for any type of unit that resets to 0 at the end of the billing period (think daily Google Ads clicks or monthly cell phone minutes). This is most commonly associated with usage-based billing and many other pricing schemes.
    /// </para>
    /// <para>
    /// Note that this is different from recurring quantity-based components, which DO NOT reset to zero at the start of every billing period. If you want to bill for a quantity of something that does not change unless you change it, then you want quantity components, instead.
    /// </para>
    /// <para>
    /// For more information on components, see our documentation <see href="https://maxio.zendesk.com/hc/en-us/articles/24261141522189-Components-Overview">here</see>.
    /// </para>
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, taxable components must include a non-blank <c>tax_code</c>. Sending <c>"tax_code": ""</c> returns <c>422</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> CreateMeteredComponent(string productFamilyId,
        CreateMeteredComponent? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/metered_components.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentResponse>(),
            CreateMeteredComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create On/Off Component
    /// </summary>
    /// <param name="productFamilyId">Either the product family's id or its handle prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateOnOffComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an On/Off component definition under the specified product family. An On/Off component can then be added and “allocated” for a subscription.
    /// <para>
    /// On/off components are used for any flat fee, recurring add on (think $99/month for tech support or a flat add on shipping fee).
    /// </para>
    /// <para>
    /// For more information on components, see our documentation <see href="https://maxio.zendesk.com/hc/en-us/articles/24261141522189-Components-Overview">here</see>.
    /// </para>
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, taxable components must include a non-blank <c>tax_code</c>. Sending <c>"tax_code": ""</c> returns <c>422</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> CreateOnOffComponent(string productFamilyId,
        CreateOnOffComponent? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/on_off_components.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentResponse>(),
            CreateOnOffComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Prepaid Usage Component
    /// </summary>
    /// <param name="productFamilyId">Either the product family's id or its handle prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreatePrepaidUsageComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a prepaid usage component definition under the specified product family. A prepaid component can then be added and “allocated” for a subscription.
    /// <para>
    /// Prepaid components allow customers to pre-purchase units that can be used up over time on their subscription. In a sense, they are the mirror image of metered components; while metered components charge at the end of the period for the amount of units used, prepaid components are charged for at the time of purchase, and we subsequently keep track of the usage against the amount purchased.
    /// </para>
    /// <para>
    /// For more information on components, see our documentation <see href="https://maxio.zendesk.com/hc/en-us/articles/24261141522189-Components-Overview">here</see>.
    /// </para>
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, taxable components must include a non-blank <c>tax_code</c>. Sending <c>"tax_code": ""</c> returns <c>422</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> CreatePrepaidUsageComponent(string productFamilyId,
        CreatePrepaidComponent? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/prepaid_usage_components.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentResponse>(),
            CreatePrepaidUsageComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Quantity Based Component
    /// </summary>
    /// <param name="productFamilyId">Either the product family's id or its handle prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateQuantityBasedComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a Quantity Based component definition under the specified product family. A Quantity Based component can then be added and “allocated” for a subscription.
    /// <para>
    /// When defining a Quantity Based component, you can choose one of 2 types:
    /// #### Recurring
    /// Recurring quantity-based components are used to bill for the number of some unit (think monthly software user licenses or the number of pairs of socks in a box-a-month club). This is most commonly associated with billing for user licenses, number of users, number of employees, etc.
    /// </para>
    /// <para>
    /// #### One-time
    /// One-time quantity-based components are used to create ad hoc usage charges that do not recur. For example, at the time of signup, you might want to charge your customer a one-time fee for onboarding or other services.
    /// </para>
    /// <para>
    /// The allocated quantity for one-time quantity-based components immediately gets reset back to zero after the allocation is made.
    /// </para>
    /// <para>
    /// For more information on components, see our documentation <see href="https://maxio.zendesk.com/hc/en-us/articles/24261141522189-Components-Overview">here</see>.
    /// </para>
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, taxable components must include a non-blank <c>tax_code</c>. Sending <c>"tax_code": ""</c> returns <c>422</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> CreateQuantityBasedComponent(string productFamilyId,
        CreateQuantityBasedComponent? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/quantity_based_components.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentResponse>(),
            CreateQuantityBasedComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Find Component
    /// </summary>
    /// <param name="handle">The handle of the component to find</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns information for a component matching the provided handle. You can identify your components with a handle so you don't have to save or reference the IDs we generate.
    /// </remarks>
    public Task<ComponentResponse> FindComponent(string handle,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/lookup.json"),
            [],
            [new Param("handle", handle)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ComponentResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Components
    /// </summary>
    /// <param name="dateField">The type of filter you would like to apply to your search.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="includeArchived">Include archived items.</param>
    /// <param name="filter">Filter to use for List Components operations</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists components for a site.
    /// </remarks>
    public Task<IReadOnlyList<ComponentResponse>> ListComponents(BasicDateField? dateField,
        string? startDate,
        string? endDate,
        string? startDatetime,
        string? endDatetime,
        bool? includeArchived,
        ListComponentsFilter? filter,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components.json"),
            [],
            [new Param("date_field", dateField),
                new Param("start_date", startDate),
                new Param("end_date", endDate),
                new Param("start_datetime", startDatetime),
                new Param("end_datetime", endDatetime),
                new Param("include_archived", includeArchived),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("filter", filter)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ComponentResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Components for Product Family
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family</param>
    /// <param name="includeArchived">Include archived items.</param>
    /// <param name="filter">Filter to use for List Components operations</param>
    /// <param name="dateField">The type of filter you would like to apply to your search. Use in query <c>date_field=created_at</c>.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists components for a particular product family.
    /// </remarks>
    public Task<IReadOnlyList<ComponentResponse>> ListComponentsForProductFamily(int productFamilyId,
        bool? includeArchived,
        ListComponentsFilter? filter,
        BasicDateField? dateField,
        string? endDate,
        string? endDatetime,
        string? startDate,
        string? startDatetime,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/components.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [new Param("include_archived", includeArchived),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("filter", filter),
                new Param("date_field", dateField),
                new Param("end_date", endDate),
                new Param("end_datetime", endDatetime),
                new Param("start_date", startDate),
                new Param("start_datetime", startDatetime)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ComponentResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Component
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the component belongs</param>
    /// <param name="componentId">Either the Advanced Billing id of the component or the handle for the component prefixed with <c>handle:</c></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns information regarding a component from a specific product family.
    /// <para>
    /// You can read the component by either the component's id or handle. When using the handle, it must be prefixed with <c>handle:</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> ReadComponent(int productFamilyId,
        string componentId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/components/{component_id}.json"),
            [new TemplateParam("product_family_id", productFamilyId), new TemplateParam("component_id", componentId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ComponentResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Component
    /// </summary>
    /// <param name="componentId">The id or handle of the component</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a component.
    /// <para>
    /// You may read the component by either the component's id or handle. When using the handle, it must be prefixed with <c>handle:</c>.
    /// </para>
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, taxable components must include a non-blank <c>tax_code</c>. Sending <c>"tax_code": ""</c> returns <c>422</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> UpdateComponent(string componentId,
        UpdateComponentRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}.json"),
            [new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentResponse>(),
            UpdateComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Product Family Component
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the component belongs</param>
    /// <param name="componentId">Either the Advanced Billing id of the component or the handle for the component prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateProductFamilyComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a component from a specific product family.
    /// <para>
    /// You may read the component by either the component's id or handle. When using the handle, it must be prefixed with <c>handle:</c>.
    /// </para>
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, taxable components must include a non-blank <c>tax_code</c>. Sending <c>"tax_code": ""</c> returns <c>422</c>.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> UpdateProductFamilyComponent(int productFamilyId,
        string componentId,
        UpdateComponentRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/components/{component_id}.json"),
            [new TemplateParam("product_family_id", productFamilyId), new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentResponse>(),
            UpdateProductFamilyComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
