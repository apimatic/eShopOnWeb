using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: re-posting the same plan
/// returns the existing subscription instead of creating a duplicate customer/subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var username = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var appUser = await _userManager.FindByNameAsync(username);
        var email = appUser?.Email ?? username;

        var command = new SubscribeCommand
        {
            // The username is the stable shopper identity; it becomes the Maxio customer reference,
            // which is what makes customer creation and enrollment idempotent.
            CustomerReference = username,
            Email = email,
            FirstName = FirstNonEmpty(request.FirstName, email.Split('@')[0]),
            LastName = FirstNonEmpty(request.LastName, "Customer"),
            ProductHandle = request.ProductHandle
        };

        var subscription = await billingService.SubscribeAsync(command);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(subscription)
        };

        return Results.Created("api/my-subscriptions", response);
    }

    internal static SubscriptionDto Map(ShopperSubscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            PriceInCents = subscription.ProductPriceInCents,
            Price = subscription.ProductPrice,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextBillingAt
        };
    }

    private static string FirstNonEmpty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value!;
}
