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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers (the products in the configured Maxio
/// product family). Requires an authenticated caller.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await HandleAsync(billingService, cancellationToken))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        try
        {
            var plans = await billingService.GetPlansAsync(cancellationToken);
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(p => p.ToDto()).ToList(),
            };
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return Results.BadRequest(new { errors = ex.Errors });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                title: "The billing provider could not be reached.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
