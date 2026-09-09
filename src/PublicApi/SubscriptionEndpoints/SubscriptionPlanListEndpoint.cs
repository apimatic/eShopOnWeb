using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleCoreAsync(billingService, cancellationToken);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        return HandleCoreAsync(billingService, CancellationToken.None);
    }

    private static async Task<IResult> HandleCoreAsync(ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await billingService.ListPlansAsync(cancellationToken);
            var response = new SubscriptionPlanListResponse
            {
                Plans = plans.Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    RequiresPaymentMethod = p.RequiresPaymentMethod
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return ex.ToProblemResult();
        }
    }
}
