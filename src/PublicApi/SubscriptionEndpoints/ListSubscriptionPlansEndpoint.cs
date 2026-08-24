using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();
        response.Plans.AddRange(await billingService.ListPlansAsync());
        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
