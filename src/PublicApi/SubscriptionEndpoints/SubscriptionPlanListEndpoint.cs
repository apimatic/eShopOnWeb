using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists the subscription plans in the configured Maxio product family.</summary>
public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public async Task<IResult> HandleAsync(IMaxioBillingService billing)
    {
        try
        {
            return Results.Ok(await billing.GetPlansAsync(CancellationToken.None));
        }
        catch (MaxioApiException exception)
        {
            return SubscriptionEndpointHelpers.MaxioFailure(exception);
        }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioBillingService billing, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await billing.GetPlansAsync(cancellationToken));
                }
                catch (MaxioApiException exception)
                {
                    return SubscriptionEndpointHelpers.MaxioFailure(exception);
                }
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlanDto[]>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
