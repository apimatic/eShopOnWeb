using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a shopper can subscribe to (the products in the configured
/// Maxio product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, ISubscriptionService subscriptionService) =>
                await HandleAsync(http, subscriptionService))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, ISubscriptionService subscriptionService)
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();
            response.Plans.AddRange(await subscriptionService.GetPlansAsync(http.RequestAborted));
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return SubscriptionProblemMapper.ToProblem(ex);
        }
    }
}
