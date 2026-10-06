using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the
/// request for a plan the user already holds returns the existing
/// subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CreateSubscriptionEndpoint(IServiceScopeFactory scopeFactory)
    {
        // Endpoint instances are shared across requests, so request-scoped
        // services (UserManager/DbContext) are resolved per call via a scope.
        _scopeFactory = scopeFactory;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(request, user, subscriptionBillingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request,
        ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService)
    {
        var applicationUser = await ResolveUserAsync(user);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.BadRequest(new { Message = "planHandle is required." });
        }

        var customer = new BillingCustomerInfo
        {
            UserId = applicationUser.Id,
            Email = applicationUser.Email ?? applicationUser.UserName ?? string.Empty,
            FirstName = DeriveFirstName(applicationUser),
            LastName = DeriveLastName(applicationUser)
        };

        var result = await subscriptionBillingService.SubscribeAsync(customer, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = MapSubscription(result.Subscription),
            AlreadySubscribed = result.Outcome == SubscriptionSignupOutcome.AlreadySubscribed
        };

        return result.Outcome == SubscriptionSignupOutcome.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private async Task<ApplicationUser?> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByNameAsync(username);
    }

    // The eShopOnWeb identity model carries only the user name (an email
    // address). Derive friendly customer names from the email's local part so
    // the billing-system customer record is readable without collecting
    // additional profile data.
    private static string DeriveFirstName(ApplicationUser user)
    {
        var localPart = EmailLocalPart(user);
        var separatorIndex = localPart.IndexOfAny(new[] { '.', '_', '-' });
        return separatorIndex > 0
            ? Capitalize(localPart[..separatorIndex])
            : Capitalize(localPart);
    }

    private static string DeriveLastName(ApplicationUser user)
    {
        var localPart = EmailLocalPart(user);
        var separatorIndex = localPart.IndexOfAny(new[] { '.', '_', '-' });
        return separatorIndex > 0 && separatorIndex < localPart.Length - 1
            ? Capitalize(localPart[(separatorIndex + 1)..])
            : "Customer";
    }

    private static string EmailLocalPart(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        return string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static SubscriptionDto MapSubscription(UserSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
            CreatedAt = subscription.CreatedAt
        };
    }
}
