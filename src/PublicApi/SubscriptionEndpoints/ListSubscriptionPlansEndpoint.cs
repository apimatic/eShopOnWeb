using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .RequireAuthorization()
            .Produces<ProductListResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptionService)
    {
        var response = new ProductListResponse();

        try
        {
            var products = await subscriptionService.GetSubscriptionPlansAsync(CancellationToken.None);
            response.Products.AddRange(products);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
