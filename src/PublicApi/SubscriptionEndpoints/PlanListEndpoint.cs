using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
}

public class ListPlansResponse
{
    public System.Collections.Generic.List<PlanDto> Plans { get; set; } = new();

    /// <summary>True when the list was cut short by a page cap.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class PlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, MaxioBillingService billing) => await HandleAsync(http, billing))
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunAsync(http, async ct =>
        {
            var list = await billing.ListPlansAsync(ct);
            return Results.Ok(new ListPlansResponse
            {
                Truncated = list.Truncated,
                Plans = list.Plans.Select(p => new PlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    MonthlyPrice = p.PriceInCents / 100m,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    Brand = p.Brand
                }).ToList()
            });
        });
}
