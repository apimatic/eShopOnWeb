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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, MaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioBillingService billing) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), billing);
            })
            .Produces<ListMySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, MaxioBillingService billing)
    {
        var response = new ListMySubscriptionsResponse();
        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null) return Results.Unauthorized();

        var subs = await billing.ListMySubscriptionsAsync(user.Id.ToString());
        response.Subscriptions = subs.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductHandle = s.ProductHandle,
            Price = s.Price,
            NextBillingDate = s.NextBillingDate,
            CustomerReference = s.CustomerReference
        }).ToList();
        return Results.Ok(response);
    }
}
