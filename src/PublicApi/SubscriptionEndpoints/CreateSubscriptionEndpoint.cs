using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the caller to a Maxio plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var shopper = await ShopperIdentityFactory.FromAsync(user, userManager);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, billing, shopper, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        throw new System.NotSupportedException("Shopper identity is required.");
    }

    private async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ApplicationCore.Entities.SubscriptionAggregate.ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var result = await billing.SubscribeAsync(shopper, request.ProductHandle, cancellationToken);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDtoMapper.ToDto(result.Subscription)
        };

        if (result.Created)
        {
            return Results.Created("api/my-subscriptions", response);
        }

        return Results.Ok(response);
    }
}
