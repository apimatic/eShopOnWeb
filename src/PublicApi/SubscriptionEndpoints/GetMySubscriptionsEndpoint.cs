using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class GetMySubscriptionsEndpoint
{
    public static void MapGetMySubscriptions(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
           .Produces<GetMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IMaxioSubscriptionService subscriptionService,
        IMapper mapper,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                         context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);
            var response = new GetMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(mapper.Map<SubscriptionDto>).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class GetMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
