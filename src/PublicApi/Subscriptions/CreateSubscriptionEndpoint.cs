using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, IMaxioSubscriptionService service) =>
                await HandleAsync(request, service))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService service)
    {
        try
        {
            var subscription = await service.SubscribeAsync(request.PlanHandle, default);
            if (subscription is null)
                return Results.Unauthorized();

            return Results.Created("api/my-subscriptions", new SubscribeResponse(request.CorrelationId(), subscription));
        }
        catch (Exception exception)
        {
            return SubscriptionEndpointResults.FromException(exception);
        }
    }
}
