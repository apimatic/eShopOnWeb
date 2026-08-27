using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, HttpContext, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (HttpContext context, CancellationToken cancellationToken) =>
                    HandleAsync(context, cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var subscriptionService = context.RequestServices.GetRequiredService<ISubscriptionService>();
        var plans = await subscriptionService.ListPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse(SubscriptionDtoMapper.Map(plans)));
    }
}
