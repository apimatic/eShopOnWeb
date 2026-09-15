using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a Maxio plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, billing, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
        HandleAsync(request, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var (firstName, lastName) = NamesFromUser(user);
        var result = await billing.SubscribeAsync(
            buyerId: user.Id,
            email: user.Email ?? user.UserName ?? $"{user.Id}@eshop.local",
            firstName: firstName,
            lastName: lastName,
            productHandle: request.ProductHandle,
            cancellationToken: cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(result.Subscription)
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }

    private static (string First, string Last) NamesFromUser(ApplicationUser user)
    {
        var source = user.Email ?? user.UserName ?? "shopper";
        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    internal static SubscriptionDto Map(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        State = subscription.State,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt
    };
}
