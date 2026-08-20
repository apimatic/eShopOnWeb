using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor) :
    IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                async (ISubscriptionBillingService service) => await HandleAsync(service))
            .Produces<IReadOnlyList<SubscriptionPlan>>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService service)
    {
        var plans = await service.ListPlansAsync(
            httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        return Results.Ok(plans);
    }
}
