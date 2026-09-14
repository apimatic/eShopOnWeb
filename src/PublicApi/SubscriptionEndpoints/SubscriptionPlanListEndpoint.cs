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

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (IMaxioBillingService billingService, CancellationToken cancellationToken) =>
                    await HandleAsync(billingService, cancellationToken))
            .Produces<SubscriptionPlanListResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        IMaxioBillingService billingService,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await billingService.GetPlansAsync(cancellationToken);
            return Results.Ok(new SubscriptionPlanListResponse(plans));
        }
        catch (MaxioApiException exception)
        {
            return SubscriptionEndpointHelpers.BillingFailure(exception);
        }
    }

    Task<IResult> IEndpoint<IResult, IMaxioBillingService>.HandleAsync(IMaxioBillingService billingService)
        => HandleAsync(billingService, CancellationToken.None);
}
