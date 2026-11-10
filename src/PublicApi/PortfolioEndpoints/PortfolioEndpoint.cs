using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PortfolioEndpoints;

public class PositionDto
{
    public int ConnectionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string? Ticker { get; set; }
    public string? Name { get; set; }
    public decimal Value { get; set; }
    public string? Currency { get; set; }

    /// <summary>Units held, for anything that is not an option.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Quantity { get; set; }

    /// <summary>Contracts held, for an option.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Contracts { get; set; }
}

public class PortfolioResponse : BaseResponse
{
    public List<PositionDto> Positions { get; set; } = new();
}

/// <summary>
/// The holdings of the shopper's connected brokerage account
/// </summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService portfolio, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, portfolio, cancellationToken);
            })
            .Produces<PortfolioResponse>()
            .WithTags("PortfolioEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, PortfolioService portfolio, CancellationToken cancellationToken)
    {
        var positions = await portfolio.GetAsync(user.ShopperId(), cancellationToken);
        return Results.Ok(new PortfolioResponse
        {
            Positions = positions.Select(p => new PositionDto
            {
                ConnectionId = p.ConnectionId,
                AccountId = p.AccountId,
                Ticker = p.Ticker,
                Name = p.Name,
                Value = p.Value,
                Currency = p.Currency,
                Quantity = p.Quantity,
                Contracts = p.Contracts,
            }).ToList(),
        });
    }
}
