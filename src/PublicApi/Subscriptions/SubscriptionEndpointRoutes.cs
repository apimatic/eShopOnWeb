using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class SubscriptionEndpointRoutes
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioSubscriptionService service, HttpContext context) =>
            await GetPlansAsync(service, context.RequestAborted))
            .RequireAuthorization()
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, IMaxioSubscriptionService service, HttpContext context) =>
            Results.Ok(new SubscribeResponse(request.CorrelationId())
            {
                Subscription = await service.SubscribeAsync(request, context.RequestAborted)
            }))
            .RequireAuthorization()
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", async (IMaxioSubscriptionService service, HttpContext context) =>
            await GetMySubscriptionsAsync(service, context.RequestAborted))
            .RequireAuthorization()
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetPlansAsync(IMaxioSubscriptionService service, CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(await service.GetPlansAsync(cancellationToken));
        return Results.Ok(response);
    }

    private static async Task<IResult> GetMySubscriptionsAsync(IMaxioSubscriptionService service, CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(await service.GetMySubscriptionsAsync(cancellationToken));
        return Results.Ok(response);
    }
}
