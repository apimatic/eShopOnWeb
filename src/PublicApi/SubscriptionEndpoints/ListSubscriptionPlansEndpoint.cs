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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a shopper can subscribe to (the active products of the
/// configured Maxio product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithName("subscriptions.list-plans");
    }

    public async Task<IResult> HandleAsync(
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await billingService.GetPlansAsync(cancellationToken);
        response.Plans = plans.Select(p => p.ToDto()).ToList();

        return Results.Ok(response);
    }

    // Satisfies IEndpoint<IResult, TParam>.
    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
        => HandleAsync(billingService, default);
}
