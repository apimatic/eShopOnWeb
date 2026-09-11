using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _http;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService service, UserManager<ApplicationUser> userManager, IHttpContextAccessor http)
    {
        _service = service;
        _userManager = userManager;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest req) =>
        {
            return await HandleAsync(req);
        })
        .Produces<SubscriptionDto>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var user = _http.HttpContext?.User;
        try
        {
            var appUser = await _userManager.GetUserAsync(user);
            if (appUser == null)
                return Results.Unauthorized();

            var email = appUser.Email ?? user.Identity?.Name ?? "unknown";
            var existing = await _service.FindCustomerByEmailAsync(email);
            CustomerResponse customerResponse;
            if (existing == null)
            {
                customerResponse = await _service.CreateCustomerAsync(email, appUser.UserName ?? "User", "Name");
            }
            else
            {
                customerResponse = existing;
            }

            var customerId = customerResponse.Customer?.Id ?? 0;
            if (customerId == 0)
                return Results.BadRequest(new { error = "Customer identifier missing" });

            var sub = await _service.CreateSubscriptionAsync(customerId, request.PlanId);

            var dto = new SubscriptionDto
            {
                Id = sub.Subscription?.Id ?? 0,
                State = sub.Subscription?.State ?? string.Empty
            };

            return Results.Ok(dto);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}
