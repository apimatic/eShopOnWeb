using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the Maxio subscriptions belonging to the authenticated eShopOnWeb user.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IMaxioSubscriptionService _maxioService;

    public ListMySubscriptionsEndpoint(IMaxioSubscriptionService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var customerReference = $"eshoponweb:{username}";

        var subscriptions = await _maxioService.ListMySubscriptionsAsync(customerReference);

        response.Subscriptions.AddRange(subscriptions.Select(s => new CustomerSubscriptionDto
        {
            Id = s.Id,
            PlanHandle = s.ProductHandle ?? string.Empty,
            PlanName = s.ProductName ?? string.Empty,
            Price = (s.PriceInCents ?? 0) / 100m,
            Currency = s.Currency ?? string.Empty,
            State = s.State ?? string.Empty,
            NextBillingDate = s.NextAssessmentAt
        }));

        return Results.Ok(response);
    }
}
