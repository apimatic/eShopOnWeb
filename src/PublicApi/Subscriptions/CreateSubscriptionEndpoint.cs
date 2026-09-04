using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService service, CancellationToken cancellationToken) =>
            await HandleRouteAsync(request, service, user, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<SubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService service) =>
        HandleRouteAsync(request, service, new ClaimsPrincipal(), CancellationToken.None);

    private static async Task<IResult> HandleRouteAsync(CreateSubscriptionRequest request, ISubscriptionService service, ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["planHandle"] = new[] { "A Maxio plan handle is required." }
            });
        }

        var userName = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var subscription = await service.SubscribeAsync(userName, request.PlanHandle, cancellationToken);
        var response = new SubscriptionResponse { Subscription = subscription };
        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
