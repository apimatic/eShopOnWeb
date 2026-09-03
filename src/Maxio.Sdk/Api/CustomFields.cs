using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Core.Extensions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class CustomFields
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal CustomFields(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Metadata
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="resourceId">The Advanced Billing id of the customer or the subscription for which the metadata applies</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Metadata"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateMetadataError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates metadata and metafields for a specific subscription or customer, or updates metadata values of existing metafields for a subscription or customer. Metadata values are limited to 2 KB in size.
    /// <para>
    /// If you create metadata on a subscription or customer with a metafield that does not already exist, the metafield is created with the metadata you specify and it is always added as a text field. You can update the input_type for the metafield with the <see href="$e/Custom%20Fields/updateMetafield">Update Metafield</see> endpoint.
    /// </para>
    /// <para>
    /// &gt;Note: Each site is limited to 100 unique metafields per resource. This means you can have 100 metafields for Subscriptions and another 100 for Customers.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Metadata>> CreateMetadata(ResourceType resourceType,
        int resourceId,
        CreateMetadataRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/{resource_id}/metadata.json"),
            [new TemplateParam("resource_type", resourceType), new TemplateParam("resource_id", resourceId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<IReadOnlyList<Metadata>>(),
            CreateMetadataErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Metafields
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Metafield"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateMetafieldsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates metafields on a Site for either the Subscriptions or Customers resource.
    /// <para>
    /// Metafields and their metadata are created in the Custom Fields configuration page on your Site. Metafields can be populated with metadata when you create them or later with the <see href="$e/Custom%20Fields/updateMetafield">Update Metafield</see>, <see href="$e/Custom%20Fields/createMetadata">Create Metadata</see>, or <see href="$e/Custom%20Fields/updateMetadata">Update Metadata</see> endpoints. The Create Metadata and Update Metadata endpoints allow you to add metafields and metadata values to a specific subscription or customer.
    /// </para>
    /// <para>
    /// Each site is limited to 100 unique metafields per resource. This means you can have 100 metafields for Subscriptions and another 100 for Customers.
    /// </para>
    /// <para>
    /// &gt; Note: After creating a metafield, the resource type cannot be modified.
    /// </para>
    /// <para>
    /// In the UI and product documentation, metafields and metadata are called Custom Fields.
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Metafield is the custom field</description></item>
    ///   <item><description>Metadata is the data populating the custom field.</description></item>
    /// </list>
    /// <para>
    /// See <see href="https://docs.maxio.com/hc/en-us/articles/24266140850573-Custom-Fields-Reference">Custom Fields Reference</see> and <see href="https://maxio.zendesk.com/hc/en-us/articles/24251701302925-Subscription-Summary-Custom-Fields-Tab">Custom Fields Tab</see> for information on using Custom Fields in the Advanced Billing UI.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Metafield>> CreateMetafields(ResourceType resourceType,
        CreateMetafieldsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/metafields.json"),
            [new TemplateParam("resource_type", resourceType)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<IReadOnlyList<Metafield>>(),
            CreateMetafieldsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Metadata
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="resourceId">The Advanced Billing id of the customer or the subscription for which the metadata applies</param>
    /// <param name="name">Name of field to be removed.</param>
    /// <param name="names">Names of fields to be removed. Use in query: <c>names[]=field1&amp;names[]=my-field&amp;names[]=another-field</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteMetadataError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes one or more metafields (and associated metadata) from the specified subscription or customer.
    /// </remarks>
    public Task DeleteMetadata(ResourceType resourceType,
        int resourceId,
        string? name,
        IReadOnlyList<string>? names,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/{resource_id}/metadata.json"),
            [new TemplateParam("resource_type", resourceType), new TemplateParam("resource_id", resourceId)],
            [new Param("name", name), new Param("names", names)],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeleteMetadataErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Metafield
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="name">The name of the metafield to be deleted</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteMetafieldError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a metafield from your Site. Removes the metafield and associated metadata from all Subscriptions or Customers resources on the Site.
    /// </remarks>
    public Task DeleteMetafield(ResourceType resourceType,
        string? name,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/metafields.json"),
            [new TemplateParam("resource_type", resourceType)],
            [new Param("name", name)],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeleteMetafieldErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Metadata
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="resourceId">The Advanced Billing id of the customer or the subscription for which the metadata applies</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaginatedMetadata"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists metadata and metafields for a specific customer or subscription.
    /// </remarks>
    public Task<PaginatedMetadata> ListMetadata(ResourceType resourceType,
        int resourceId,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/{resource_id}/metadata.json"),
            [new TemplateParam("resource_type", resourceType), new TemplateParam("resource_id", resourceId)],
            [new Param("page", page), new Param("per_page", perPage)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PaginatedMetadata>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Metadata for Resource Type
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="dateField">The type of filter you would like to apply to your search.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns metadata with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns metadata with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns metadata with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns metadata with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="withDeleted">Allow to fetch deleted metadata.</param>
    /// <param name="resourceIds">Allow to fetch metadata for multiple records based on provided ids. Use in query: <c>resource_ids[]=122&amp;resource_ids[]=123&amp;resource_ids[]=124</c>.</param>
    /// <param name="direction">Controls the order in which results are returned. Use in query <c>direction=asc</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PaginatedMetadata"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists metadata for a specified array of subscriptions or customers.
    /// </remarks>
    public Task<PaginatedMetadata> ListMetadataForResourceType(ResourceType resourceType,
        BasicDateField? dateField,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        DateTimeOffset? startDatetime,
        DateTimeOffset? endDatetime,
        bool? withDeleted,
        IReadOnlyList<int>? resourceIds,
        SortingDirection? direction,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/metadata.json"),
            [new TemplateParam("resource_type", resourceType)],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("date_field", dateField),
                new Param("start_date", startDate?.ToDate()),
                new Param("end_date", endDate?.ToDate()),
                new Param("start_datetime", startDatetime?.ToIso8601()),
                new Param("end_datetime", endDatetime?.ToIso8601()),
                new Param("with_deleted", withDeleted),
                new Param("resource_ids", resourceIds),
                new Param("direction", direction)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PaginatedMetadata>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Metafields
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="name">Filter by the name of the metafield.</param>
    /// <param name="direction">Controls the order in which results are returned. Use in query <c>direction=asc</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListMetafieldsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists the metafields and their associated details for a Site and resource type. You can filter the request to a specific metafield.
    /// </remarks>
    public Task<ListMetafieldsResponse> ListMetafields(ResourceType resourceType,
        string? name,
        SortingDirection? direction,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/metafields.json"),
            [new TemplateParam("resource_type", resourceType)],
            [new Param("name", name),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("direction", direction)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMetafieldsResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Metadata
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="resourceId">The Advanced Billing id of the customer or the subscription for which the metadata applies</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Metadata"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateMetadataError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates metadata and metafields on the Site and the customer or subscription specified, and updates the metadata value on a subscription or customer.
    /// <para>
    /// If you update metadata on a subscription or customer with a metafield that does not already exist, the metafield is created with the metadata you specify and it is always added as a text field to the Site and to the subscription or customer you specify. You can update the input_type for the metafield with the Update Metafield endpoint.
    /// </para>
    /// <para>
    /// Each site is limited to 100 unique metafields per resource. This means you can have 100 metafields for the Subscription resource and another 100 for the Customer resource.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Metadata>> UpdateMetadata(ResourceType resourceType,
        int resourceId,
        UpdateMetadataRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/{resource_id}/metadata.json"),
            [new TemplateParam("resource_type", resourceType), new TemplateParam("resource_id", resourceId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<IReadOnlyList<Metadata>>(),
            UpdateMetadataErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Metafield
    /// </summary>
    /// <param name="resourceType">The resource type to which the metafields belong.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Metafield"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateMetafieldError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates metafields on your Site for a resource type.  Depending on the request structure, you can update or add metafields and metadata to the Subscriptions or Customers resource.
    /// <para>
    /// With this endpoint, you can:
    /// </para>
    /// <para>
    /// - Add metafields. If the metafield specified in current_name does not exist, a new metafield is added.
    ///   &gt;Note: Each site is limited to 100 unique metafields per resource. This means you can have 100 metafields for Subscriptions and another 100 for Customers.
    /// </para>
    /// <para>
    /// - Change the name of a metafield.
    ///   &gt;Note: To keep the metafield name the same and only update the metadata for the metafield, you must use the current metafield name in both the <c>current_name</c> and <c>name</c> parameters.
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Change the input type for the metafield. For example, you can change a metafield input type from text to a dropdown. If you change the input type from text to a dropdown or radio, you must update the specific subscriptions or customers where the metafield was used to reflect the updated metafield and metadata.</description></item>
    /// </list>
    /// <para>
    /// - Add metadata values to the existing metadata for a dropdown or radio metafield.
    ///   &gt;Note: Updates to metadata overwrite. To add one or more values, you must specify all metadata values including the new value you want to add.
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Add new metadata to a dropdown or radio for a metafield that was created without metadata.</description></item>
    /// </list>
    /// <para>
    /// - Remove metadata for a dropdown or radio for a metafield.
    ///   &gt;Note: Updates to metadata overwrite existing values. To remove one or more values, specify all metadata values except those you want to remove.
    /// </para>
    /// <para>
    /// - Add or update scope settings for a metafield.
    ///   &gt;Note: Scope changes overwrite existing settings. You must specify the complete scope, including the changes you want to make.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Metafield>> UpdateMetafield(ResourceType resourceType,
        UpdateMetafieldsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/{resource_type}/metafields.json"),
            [new TemplateParam("resource_type", resourceType)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<IReadOnlyList<Metafield>>(),
            UpdateMetafieldErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
