using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionMyListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionMyListEndpoint(IMaxioClient maxio, IOptions<MaxioSettings> settings, IHttpContextAccessor httpContextAccessor)
    {
        _maxio = maxio;
        _settings = settings.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<List<SubscriptionDto>>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var email = user?.FindFirst(ClaimTypes.Email)?.Value
            ?? user?.FindFirst("email")?.Value
            ?? user?.Identity?.Name
            ?? "unknown";

        var customerResp = await _maxio.GetCustomerByReferenceAsync(email);
        if (customerResp?.Customer == null)
            return Results.Ok(new List<SubscriptionDto>());

        var subs = await _maxio.ListCustomerSubscriptionsAsync(customerResp.Customer.Id);
        var dtos = subs.Select(s => new SubscriptionDto
        {
            Id = s.Subscription.Id,
            State = s.Subscription.State,
            ProductName = s.Subscription.Product?.Name ?? string.Empty,
            ProductHandle = s.Subscription.Product?.Handle ?? string.Empty,
            Price = s.Subscription.ProductPriceInCents / 100m,
            CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = s.Subscription.NextAssessmentAt,
            ActivatedAt = s.Subscription.ActivatedAt
        }).ToList();
        return Results.Ok(dtos);
    }
}
