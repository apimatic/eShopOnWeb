using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                    await HandleAsync(billingService, cancellationToken))
            .Produces<SubscriptionPlan[]>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAuthorization()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Results.Ok(await billingService.ListPlansAsync(cancellationToken));
        }
        catch (BillingException ex)
        {
            return BillingHttpResults.FromException(ex);
        }
    }
}
