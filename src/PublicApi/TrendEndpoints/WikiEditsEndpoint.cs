using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Watches Wikimedia's live revision stream for a few seconds and reports the edits that mention the catalog
/// </summary>
public class WikiEditsEndpoint : IEndpoint<IResult, GetWikiEditsRequest, CatalogTrendTerms>
{
    private readonly WikiEditWatcher _watcher;
    private readonly WikiWatchGate _gate;

    public WikiEditsEndpoint(WikiEditWatcher watcher, WikiWatchGate gate)
    {
        _watcher = watcher;
        _gate = gate;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/trends/wiki-edits",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? seconds, CatalogTrendTerms catalogTerms, HttpContext httpContext) =>
            {
                return await HandleAsync(new GetWikiEditsRequest(seconds ?? GetWikiEditsRequest.DEFAULT_SECONDS), catalogTerms, httpContext.RequestAborted);
            })
            .Produces<WikiEditsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithTags("TrendEndpoints");
    }

    public Task<IResult> HandleAsync(GetWikiEditsRequest request, CatalogTrendTerms catalogTerms) =>
        HandleAsync(request, catalogTerms, CancellationToken.None);

    public async Task<IResult> HandleAsync(GetWikiEditsRequest request, CatalogTrendTerms catalogTerms, CancellationToken cancellationToken)
    {
        if (request.Seconds < GetWikiEditsRequest.MIN_SECONDS || request.Seconds > GetWikiEditsRequest.MAX_SECONDS)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["seconds"] = [$"seconds must be between {GetWikiEditsRequest.MIN_SECONDS} and {GetWikiEditsRequest.MAX_SECONDS}."]
            });
        }

        using var lease = _gate.TryEnter();
        if (lease is null)
        {
            return Results.Problem(
                detail: "Another Wikimedia watch is already running; try again when it finishes.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var terms = await catalogTerms.ListAsync(cancellationToken);
        var response = await _watcher.WatchAsync(request.CorrelationId(), TimeSpan.FromSeconds(request.Seconds), terms, cancellationToken);

        return Results.Ok(response);
    }
}
