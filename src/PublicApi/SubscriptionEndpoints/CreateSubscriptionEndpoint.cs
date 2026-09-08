using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: repeating the same
/// request returns the existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    private readonly ICurrentUserContext _currentUser;

    public CreateSubscriptionEndpoint(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest();
        }

        var result = await billingService.SubscribeAsync(
            user.Id,
            user.Email ?? user.UserName ?? string.Empty,
            request.PlanHandle.Trim());

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription,
            Created = result.Created
        };

        return result.Created
            ? Results.Created($"api/subscriptions/{result.Subscription.Id}", response)
            : Results.Ok(response);
    }
}
