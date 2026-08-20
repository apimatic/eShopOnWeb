using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a Maxio subscription plan. Idempotent per user+plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request,
                ISubscriptionBillingService billing,
                UserManager<ApplicationUser> userManager,
                HttpContext httpContext) =>
            {
                return await ExecuteAsync(request, billing, userManager, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        return ExecuteAsync(request, billing, null, null);
    }

    internal static async Task<IResult> ExecuteAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser>? userManager,
        HttpContext? httpContext)
    {
        var identity = ShopperIdentity.From(httpContext?.User);
        if (identity is null || userManager is null)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByNameAsync(identity);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new BillingValidationException("productHandle is required.");
        }

        var (firstName, lastName) = ShopperIdentity.SplitDisplayName(user.Email ?? user.UserName ?? identity);
        var command = new SubscribeToPlanCommand(
            shopperIdentity: identity,
            email: user.Email ?? user.UserName ?? identity,
            firstName: firstName,
            lastName: lastName,
            productHandle: request.ProductHandle);

        var result = await billing.SubscribeAsync(command);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDtoMapper.Map(result.Subscription),
            Created = result.Created
        };

        if (result.Created)
        {
            return Results.Created("api/my-subscriptions", response);
        }

        return Results.Ok(response);
    }
}
