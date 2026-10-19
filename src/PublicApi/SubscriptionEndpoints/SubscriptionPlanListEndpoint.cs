using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the recurring subscription plans offered by the configured billing product family
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(context, subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, SubscriptionService subscriptionService)
    {
        var catalog = await subscriptionService.GetPlansAsync(context.RequestAborted);

        var response = new ListSubscriptionPlansResponse
        {
            ProductFamilyHandle = catalog.ProductFamilyHandle,
            IsTruncated = catalog.IsTruncated
        };
        response.Plans.AddRange(catalog.Plans.OrderBy(p => p.PriceInCents).Select(SubscriptionPlanDto.From));

        return Results.Ok(response);
    }
}
