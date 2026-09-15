using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateSubscriptionRequest request, SubscriptionService service, CancellationToken cancellationToken) =>
                    await HandleCoreAsync(request, service, cancellationToken))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionService service)
    {
        return HandleCoreAsync(request, service, CancellationToken.None);
    }

    private static async Task<IResult> HandleCoreAsync(
        CreateSubscriptionRequest request,
        SubscriptionService service,
        CancellationToken cancellationToken = default)
    {
        var planHandle = request.PlanHandle ?? request.ProductHandle;
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Results.BadRequest(new { error = "planHandle is required." });
        }

        var user = await service.GetCurrentUserAsync(cancellationToken);
        var subscription = await service.SubscribeAsync(user, planHandle.Trim(), cancellationToken);
        return Results.Created("api/subscriptions", new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription
        });
    }
}
