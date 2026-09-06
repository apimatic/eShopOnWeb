using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, MaxioSubscriptionService service, HttpContext context, IHttpContextAccessor contextAccessor) =>
            {
                return await HandleAsyncWithContext(request, service, context);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    private async Task<IResult> HandleAsyncWithContext(CreateSubscriptionRequest request, MaxioSubscriptionService service, HttpContext context)
    {
        try
        {
            var userName = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Results.BadRequest(new { error = "User not found" });
            }

            var customerId = await service.GetOrCreateCustomerAsync(
                userId: user.Id,
                email: user.Email ?? string.Empty,
                firstName: user.UserName?.Split('@')[0] ?? "User",
                lastName: string.Empty);

            var subscription = await service.CreateSubscriptionAsync(
                userId: user.Id,
                customerId: customerId,
                productHandle: request.ProductHandle);

            return Results.Ok(new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                NextBillingAt = subscription.NextBillingAt,
                PriceInDollars = subscription.PriceInCents / 100m
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService service)
    {
        throw new NotImplementedException("Use HandleAsyncWithContext instead");
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public decimal PriceInDollars { get; set; }
}
