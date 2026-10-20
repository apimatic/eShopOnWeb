using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Repeating the call for the same plan returns the
/// existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, BillingBuyer, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var buyer = await SubscriptionMapping.ResolveBuyerAsync(user, userManager);
                if (buyer is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(request, buyer, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, BillingBuyer buyer, ISubscriptionBillingService billingService) =>
        HandleAsync(request, buyer, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, BillingBuyer buyer, ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(buyer, request.PlanHandle ?? string.Empty, cancellationToken);

        response.Subscription = SubscriptionMapping.ToDto(result.Subscription);
        response.Created = result.Created;

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
