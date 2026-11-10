using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>The holdings of the shopper's brokerage account.</summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (ClaimsPrincipal user, PortfolioService portfolio) =>
                    Results.Ok(new { positions = await portfolio.GetPositionsAsync(Shopper.Id(user)) }))
            .WithTags("BankEndpoints");
    }
}
