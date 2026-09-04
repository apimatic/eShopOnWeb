using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService service) =>
                await HandleAsync(service))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService service)
    {
        try
        {
            var subscriptions = await service.GetMySubscriptionsAsync(default);
            if (subscriptions is null)
                return Results.Unauthorized();

            return Results.Ok(new MySubscriptionsResponse(Guid.NewGuid())
            {
                Subscriptions = subscriptions
            });
        }
        catch (Exception exception)
        {
            return SubscriptionEndpointResults.FromException(exception);
        }
    }
}
