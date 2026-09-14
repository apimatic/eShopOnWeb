using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a shopper can subscribe to (the active products in
/// the configured Maxio product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
        => HandleAsync(subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await subscriptionService.GetPlansAsync(cancellationToken);
            response.Plans = plans.Select(SubscriptionPlanDto.From).ToList();
            return Results.Ok(response);
        }
        catch (MaxioIntegrationException ex)
        {
            return SubscriptionProblem.From(ex, "Unable to list subscription plans");
        }
    }
}
