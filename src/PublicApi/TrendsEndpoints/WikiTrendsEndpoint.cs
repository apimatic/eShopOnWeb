using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

/// <summary>
/// Returns live Wikipedia / Wikimedia Commons edit trends for the shop's catalog keywords.
/// Watches Wikimedia's revision-create stream for the requested number of seconds.
/// </summary>
public class WikiTrendsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/trends/wiki-edits",
            [Authorize(
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int? seconds, IWikiEditsWatcher watcher, CancellationToken ct) =>
            {
                var n = Math.Clamp(seconds ?? 20, 5, 60);
                var result = await watcher.WatchAsync(n, ct);
                return Results.Ok(result);
            })
            .Produces<WikiTrendsResponse>()
            .WithTags("TrendsEndpoints");
    }
}
