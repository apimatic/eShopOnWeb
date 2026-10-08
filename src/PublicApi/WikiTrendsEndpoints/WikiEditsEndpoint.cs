using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WikiTrendsEndpoints;

/// <summary>
/// Watches Wikimedia's live revision stream and returns trending edits matching catalog brands/types.
/// </summary>
public class WikiEditsEndpoint : IEndpoint<IResult, WikiEditsRequest, IWikiTrendsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/trends/wiki-edits",
            [Authorize(
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IWikiTrendsService service, CancellationToken ct, int seconds = 20) =>
            {
                return await HandleAsync(new WikiEditsRequest(seconds), service, ct);
            })
            .Produces<WikiEditsResponse>()
            .WithTags("WikiTrendsEndpoints");
    }

    public Task<IResult> HandleAsync(WikiEditsRequest request, IWikiTrendsService service)
        => HandleAsync(request, service, CancellationToken.None);

    private static async Task<IResult> HandleAsync(
        WikiEditsRequest request,
        IWikiTrendsService service,
        CancellationToken ct)
    {
        if (request.Seconds < 5 || request.Seconds > 60)
            return Results.BadRequest("seconds must be between 5 and 60");

        var result = await service.WatchAsync(request.Seconds, ct);
        return Results.Ok(result);
    }
}
