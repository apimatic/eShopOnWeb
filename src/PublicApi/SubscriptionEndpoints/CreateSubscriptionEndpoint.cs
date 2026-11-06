using System.Security.Claims;
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
/// Subscribes the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken ct) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                    return Results.Unauthorized();

                request.UserName = userName;
                return await HandleAsync(request, subscriptionService, ct);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, CancellationToken ct)
    {
        var result = await subscriptionService.SubscribeAsync(request.UserName, null, request.PlanHandle, ct);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            Created = result.Created
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
