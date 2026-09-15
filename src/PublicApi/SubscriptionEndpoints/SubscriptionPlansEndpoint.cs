using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists active Maxio plans in the configured product family.</summary>
public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billing, CancellationToken cancellationToken) =>
            {
                try
                {
                    return await HandleAsync(billing, cancellationToken);
                }
                catch (MaxioApiException)
                {
                    return Results.Problem("The billing service could not retrieve plans.", statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billing)
    {
        var plans = await billing.GetPlansAsync(CancellationToken.None);
        return Results.Ok(new SubscriptionPlansResponse { Plans = plans.ToList() });
    }

    private async Task<IResult> HandleAsync(IMaxioBillingService billing, CancellationToken cancellationToken)
    {
        var plans = await billing.GetPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse { Plans = plans.ToList() });
    }
}
