using System;
using System.Linq;
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
/// Watches Wikimedia's live stream of new page revisions for a few seconds and reports
/// the English Wikipedia / Wikimedia Commons edits that mention catalog brands or types.
/// </summary>
public class WikiEditsEndpoint : IEndpoint<IResult, WikiEditsRequest, ICatalogTermSource>
{
    private readonly IWikiEditWatcher _watcher;

    public WikiEditsEndpoint(IWikiEditWatcher watcher)
    {
        _watcher = watcher;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/trends/wiki-edits",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? seconds, ICatalogTermSource termSource, CancellationToken requestAborted) =>
            {
                return await HandleAsync(new WikiEditsRequest(seconds, requestAborted), termSource);
            })
            .Produces<WikiEditsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithTags("TrendEndpoints");
    }

    public async Task<IResult> HandleAsync(WikiEditsRequest request, ICatalogTermSource termSource)
    {
        if (!request.IsValid)
        {
            return Results.BadRequest(
                $"seconds must be between {WikiEditsRequest.MinSeconds} and {WikiEditsRequest.MaxSeconds}.");
        }

        var matcher = new CatalogTermMatcher(await termSource.GetTermsAsync(request.RequestAborted));

        var result = await _watcher.WatchAsync(TimeSpan.FromSeconds(request.Seconds), matcher, request.RequestAborted);
        if (result is null)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var response = new WikiEditsResponse(request.CorrelationId())
        {
            Seconds = request.Seconds,
            Received = result.Received,
            Unreadable = result.Unreadable,
            Matches = result.Matches.ToList(),
            MatchCount = result.MatchCount,
            MatchesTruncated = result.MatchesTruncated,
            LatestCommons = result.LatestCommons.ToList(),
            StoppedBecause = result.StoppedBecause,
            Reason = result.Reason
        };

        return Results.Ok(response);
    }
}
