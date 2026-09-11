using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest req, MaxioBillingService billing) =>
            {
                return await HandleAsync(req, billing);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioBillingService billing)
    {
        var response = new CreateSubscriptionResponse();
        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null) return Results.Unauthorized();

        var userId = user.Id.ToString();
        var email = user.Email ?? $"{userId}@example.com";
        var first = user.UserName ?? "User";
        var last = "User";

        await billing.EnsureCustomerAsync(userId, email, first, last);
        var sub = await billing.SubscribeAsync(userId, request.ProductHandle);

        response.Subscription = new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            ProductHandle = sub.ProductHandle,
            Price = sub.Price,
            NextBillingDate = sub.NextBillingDate,
            CustomerReference = sub.CustomerReference
        };
        return Results.Ok(response);
    }
}
