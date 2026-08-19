using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a Maxio plan. Idempotent on (user, plan).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> users, IHttpContextAccessor httpContextAccessor)
    {
        _users = users;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        var http = _httpContextAccessor.HttpContext;
        if (http is null)
        {
            return Results.Unauthorized();
        }

        var userName = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var user = await _users.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var (firstName, lastName) = SplitDisplayName(user.Email ?? userName);

        var details = await billing.SubscribeAsync(new SubscribeShopperRequest
        {
            ShopperUserId = user.Id,
            Email = user.Email ?? userName,
            FirstName = firstName,
            LastName = lastName,
            ProductHandle = request.ProductHandle
        }, http.RequestAborted);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(details)
        };

        if (details.AlreadyExisted)
        {
            return Results.Ok(response);
        }

        return Results.Created($"api/subscriptions/{details.Id}", response);
    }

    internal static SubscriptionDto Map(SubscriptionDetails details) => new()
    {
        Id = details.Id,
        Reference = details.Reference,
        State = details.State,
        ProductHandle = details.ProductHandle,
        ProductName = details.ProductName,
        Price = details.Price,
        CurrentPeriodStartedAt = details.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = details.CurrentPeriodEndsAt,
        NextBillingDate = details.NextBillingDate,
        AlreadyExisted = details.AlreadyExisted
    };

    private static (string FirstName, string LastName) SplitDisplayName(string emailOrName)
    {
        var local = emailOrName.Split('@')[0];
        var parts = local.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        return (Capitalize(local), "Shopper");
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
