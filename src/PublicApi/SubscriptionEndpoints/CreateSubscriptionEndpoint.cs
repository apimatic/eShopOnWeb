using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Idempotently subscribes the signed-in caller to a subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, ct);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var email = ResolveEmail(user);
        if (email is null)
        {
            return Results.Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(email, request.ProductHandle, ct);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            AlreadySubscribed = result.AlreadySubscribed
        };

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }

    private static string? ResolveEmail(ClaimsPrincipal user)
    {
        var email = user.FindFirstValue(ClaimTypes.Name)?.Trim();
        return string.IsNullOrEmpty(email) ? null : email.ToLowerInvariant();
    }
}
