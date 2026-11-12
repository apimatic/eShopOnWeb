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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans that can be subscribed to
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), service, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioSubscriptionService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioSubscriptionService service, CancellationToken cancellationToken)
    {
        var plans = await service.ListPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse(request.CorrelationId())
        {
            Truncated = plans.Truncated
        };
        response.Plans.AddRange(plans.Plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));
        return Results.Ok(response);
    }
}
