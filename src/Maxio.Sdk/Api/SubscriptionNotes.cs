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

namespace Maxio.Api;

public sealed class SubscriptionNotes
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SubscriptionNotes(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Subscription Note
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionNoteResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSubscriptionNoteError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a note for a subscription.
    /// <para>
    /// ## How to Use Subscription Notes
    /// </para>
    /// <para>
    /// Notes allow you to record information about a particular Subscription in a free text format.
    /// </para>
    /// <para>
    /// If you have structured data such as birth date, color, etc., consider using Metadata instead.
    /// </para>
    /// <para>
    /// Full documentation on how to use Notes in the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24251712214413-Subscription-Summary-Overview">here</see>.
    /// </para>
    /// </remarks>
    public Task<SubscriptionNoteResponse> CreateSubscriptionNote(int subscriptionId,
        UpdateSubscriptionNoteRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/notes.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionNoteResponse>(),
            CreateSubscriptionNoteErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Subscription Note
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="noteId">The Advanced Billing id of the note</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a note for a Subscription.
    /// </remarks>
    public Task DeleteSubscriptionNote(int subscriptionId,
        int noteId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/notes/{note_id}.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("note_id", noteId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Subscription Notes
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="SubscriptionNoteResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListSubscriptionNotesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a list of notes associated with a subscription. The response will be an array of Notes.
    /// </remarks>
    public Task<IReadOnlyList<SubscriptionNoteResponse>> ListSubscriptionNotes(int subscriptionId,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/notes.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [new Param("page", page), new Param("per_page", perPage)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<SubscriptionNoteResponse>>(),
            ListSubscriptionNotesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Subscription Note
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="noteId">The Advanced Billing id of the note</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionNoteResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a specific note attached to a subscription.
    /// </remarks>
    public Task<SubscriptionNoteResponse> ReadSubscriptionNote(int subscriptionId,
        int noteId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/notes/{note_id}.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("note_id", noteId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SubscriptionNoteResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Subscription Note
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="noteId">The Advanced Billing id of the note</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionNoteResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateSubscriptionNoteError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a note for a subscription.
    /// </remarks>
    public Task<SubscriptionNoteResponse> UpdateSubscriptionNote(int subscriptionId,
        int noteId,
        UpdateSubscriptionNoteRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/notes/{note_id}.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("note_id", noteId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionNoteResponse>(),
            UpdateSubscriptionNoteErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
