using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// List available subscription plans from the configured Maxio product family.
/// GET /api/subscription-plans — no auth required.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly IMaxioBillingService _billingService;

    public ListSubscriptionPlansEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();
        var plans = await billingService.GetPlansAsync();
        response.Plans.AddRange(plans);
        return Results.Ok(response);
    }
}
