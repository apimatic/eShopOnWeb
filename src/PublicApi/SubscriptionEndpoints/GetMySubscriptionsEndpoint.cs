using System;
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
/// Lists the logged-in shopper's Maxio subscriptions. Read-only: if the shopper has never
/// subscribed to anything, no Maxio customer is created and an empty list is returned.
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No authenticated user on the current request.");
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("Token is missing the user identifier claim.");

        var response = new MySubscriptionsResponse();

        var customer = await billingService.FindCustomerByReferenceAsync(userId);
        if (customer is null) return Results.Ok(response);

        var subscriptions = await billingService.GetSubscriptionsForCustomerAsync(customer.Id);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));

        return Results.Ok(response);
    }
}
