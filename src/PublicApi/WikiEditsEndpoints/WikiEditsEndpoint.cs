using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;

public class WikiEditsEndpoint : IEndpoint<IResult, WikiEditsRequest, IWikiTrendsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/trends/wiki-edits",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async ([Microsoft.AspNetCore.Http.AsParameters] WikiEditsRequest request,
                   IWikiTrendsService service,
                   CancellationToken ct) =>
            {
                return await HandleAsync(request, service, ct);
            })
            .Produces<WikiEditsResponse>()
            .WithTags("WikiTrendsEndpoints");
    }

    public Task<IResult> HandleAsync(WikiEditsRequest request, IWikiTrendsService service) =>
        HandleAsync(request, service, CancellationToken.None);

    private static async Task<IResult> HandleAsync(WikiEditsRequest request, IWikiTrendsService service, CancellationToken ct)
    {
        var seconds = request.Seconds ?? 20;
        if (seconds < 5 || seconds > 60)
            return Results.BadRequest(new { error = "seconds must be between 5 and 60" });

        var result = await service.GetWikiEditsAsync(seconds, ct);
        return Results.Ok(result);
    }
}
