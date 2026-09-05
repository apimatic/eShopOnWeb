using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Lists the active subscription plans in the configured Maxio product family.</summary>
public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/subscription-plans", async (IMaxioBillingClient maxio, CancellationToken cancellationToken) =>
            await HandleAsync(maxio, cancellationToken))
            .RequireAuthorization()
            .Produces<SubscriptionPlanResponse[]>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingClient maxio, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await maxio.GetPlansAsync(cancellationToken);
            return Results.Ok(plans.Select(x => new SubscriptionPlanResponse(x.Handle, x.Name, x.Description, x.PriceInCents, x.Interval, x.IntervalUnit)));
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing catalog is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    public Task<IResult> HandleAsync(IMaxioBillingClient maxio) => HandleAsync(maxio, CancellationToken.None);
}
