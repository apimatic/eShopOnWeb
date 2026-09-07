using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class GetSubscriptionPlansEndpoint
{
    public static void MapGetSubscriptionPlans(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
           .Produces<GetSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        IMaxioSubscriptionService subscriptionService,
        IMapper mapper,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await subscriptionService.GetSubscriptionPlansAsync(cancellationToken);
            var response = new GetSubscriptionPlansResponse
            {
                Plans = plans.Select(mapper.Map<SubscriptionPlanDto>).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
