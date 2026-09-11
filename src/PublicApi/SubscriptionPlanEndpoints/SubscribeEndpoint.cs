using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest req, IMaxioBillingService svc) => await HandleAsync(req, svc))
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionPlanEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService svc)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var username = user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name ?? "anonymous";
        var response = new SubscribeResponse(request.CorrelationId());

        try
        {
            var customer = await svc.EnsureCustomerAsync(username, $"{username}@eshop.local", "Eshop", "User");
            var sub = await svc.CreateSubscriptionAsync(username, request.ProductHandle);
            response.Created = true;
            response.SubscriptionId = sub.Id;
            response.State = sub.State;
            response.ProductHandle = sub.ProductHandle;
            response.ProductName = sub.ProductName;
            response.Price = sub.PriceInCents / 100m;
            response.NextBillingDate = sub.CurrentPeriodEndsAt;
        }
        catch (Exception ex)
        {
            return Results.Problem($"Subscription failed: {ex.Message}");
        }

        return Results.Ok(response);
    }
}
