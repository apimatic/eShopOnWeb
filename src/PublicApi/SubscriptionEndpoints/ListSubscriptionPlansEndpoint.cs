using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a shopper can subscribe to.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists available subscription plans", "Lists the plans in the configured product family that a shopper can subscribe to."));
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        try
        {
            var plans = await billingService.GetAvailablePlansAsync();
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(p => p.ToDto()).ToList()
            };
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return SubscriptionErrorResults.FromBillingException(ex);
        }
    }
}
