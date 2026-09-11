using System;
using System.Collections.Generic;
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

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly IMaxioClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(IMaxioClient maxio, IOptions<MaxioSettings> settings, IHttpContextAccessor httpContextAccessor)
    {
        _maxio = maxio;
        _settings = settings.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest req) =>
            {
                return await HandleAsync(req);
            })
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var email = user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("email")?.Value
            ?? user.Identity?.Name
            ?? "unknown";
        var firstName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
        var lastName = user.FindFirst(ClaimTypes.Surname)?.Value ?? "Name";

        // Idempotent customer creation via reference
        var existing = await _maxio.GetCustomerByReferenceAsync(email);
        Customer customer;
        if (existing?.Customer != null)
        {
            customer = existing.Customer;
        }
        else
        {
            var created = await _maxio.CreateCustomerAsync(email, email, firstName, lastName);
            customer = created.Customer;
        }

        // Create subscription using product handle
        var subResp = await _maxio.CreateSubscriptionAsync(request.PlanHandle, email);
        var s = subResp.Subscription;

        return Results.Ok(new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductName = s.Product?.Name ?? request.PlanHandle,
            ProductHandle = s.Product?.Handle ?? request.PlanHandle,
            Price = s.ProductPriceInCents / 100m,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt
        });
    }
}
