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
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioBillingService billingService)
        => HandleAsync(request, billingService, default);

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await billingService.GetPlansAsync(cancellationToken);
        foreach (var plan in plans)
        {
            response.Plans.Add(plan.ToDto());
        }

        return Results.Ok(response);
    }
}
