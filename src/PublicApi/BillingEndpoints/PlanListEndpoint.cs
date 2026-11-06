using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class PlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            async (MaxioBillingService billing, IRepository<CatalogBrand> brandRepository) =>
            {
                return await HandleAsync(billing, brandRepository);
            })
            .Produces<ListPlansResponse>()
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(MaxioBillingService billing, IRepository<CatalogBrand> brandRepository)
    {
        var brands = (await brandRepository.ListAsync()).Select(b => b.Brand).ToList();
        var plans = await billing.ListPlansAsync(brands);
        return Results.Ok(new ListPlansResponse { Plans = plans });
    }
}
