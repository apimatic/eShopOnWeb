using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListEndpoint : IEndpoint<IResult, object>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _http;

    public MySubscriptionListEndpoint(IMaxioSubscriptionService service, UserManager<ApplicationUser> userManager, IHttpContextAccessor http)
    {
        _service = service;
        _userManager = userManager;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async () =>
        {
            return await HandleAsync(new object());
        })
        .Produces<List<SubscriptionDto>>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(object request)
    {
        var user = _http.HttpContext?.User;
        try
        {
            var appUser = await _userManager.GetUserAsync(user);
            if (appUser == null)
                return Results.Unauthorized();

            var email = appUser.Email ?? user.Identity?.Name ?? "unknown";
            var customer = await _service.FindCustomerByEmailAsync(email);
            if (customer?.Customer == null)
                return Results.Ok(new List<SubscriptionDto>());

            var subs = await _service.ListCustomerSubscriptionsAsync(customer.Customer.Id ?? 0);
            var dtos = subs.Select(s => new SubscriptionDto
            {
                Id = s.Subscription?.Id ?? 0,
                State = s.Subscription?.State ?? string.Empty
            }).ToList();

            return Results.Ok(dtos);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}
