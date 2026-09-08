using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to a signed-in shopper.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    private readonly MaxioOptions _maxioOptions;

    public SubscriptionPlanListEndpoint(Microsoft.Extensions.Options.IOptions<MaxioOptions> maxioOptions)
    {
        _maxioOptions = maxioOptions.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var result = await subscriptionService.GetPlansAsync(CancellationToken.None);
        if (!result.IsSuccess)
        {
            return SubscriptionResultMapper.ToHttpResult(result, _ => Results.Ok());
        }

        var response = new SubscriptionPlanListResponse(Guid.NewGuid())
        {
            ProductFamilyHandle = _maxioOptions.ProductFamilyHandle
        };
        response.Plans.AddRange(result.Value.Select(p => new SubscriptionPlanDto
        {
            Id = p.ProductId,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));

        return Results.Ok(response);
    }
}
