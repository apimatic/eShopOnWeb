using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for enrollment (GET /api/subscription-plans).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        IMaxioSubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionService.ListPlansAsync(cancellationToken);
        foreach (var plan in plans)
        {
            response.SubscriptionPlans.Add(new SubscriptionPlanDto
            {
                PlanHandle = plan.Handle,
                Name = plan.Name,
                PriceInCents = plan.PriceInCents,
                Price = plan.PriceInCents / 100m,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit,
                RequiresPaymentMethod = plan.RequiresPaymentMethod
            });
        }

        return Results.Ok(response);
    }
}
