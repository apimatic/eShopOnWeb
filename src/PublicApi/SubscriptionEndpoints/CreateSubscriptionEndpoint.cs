using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioClient maxioClient) =>
            {
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
            return Results.Unauthorized();

        var userId = user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            return Results.Unauthorized();

        var email = user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)
            ?? $"{userId}@eshop.local";

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var reference = $"eshop-{userId}";
        var customer = await maxioClient.GetOrCreateCustomerAsync(
            firstName: user.FindFirstValue(ClaimTypes.GivenName) ?? "eShop",
            lastName: user.FindFirstValue(ClaimTypes.Surname) ?? "User",
            email: email,
            reference: reference);

        var existingSubscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            s.Product?.Handle == request.ProductHandle &&
            s.State is "active" or "trialing" or "past_due");

        Maxio.MaxioSubscription subscription;
        if (existing != null)
        {
            subscription = existing;
        }
        else
        {
            subscription = await maxioClient.CreateSubscriptionAsync(request.ProductHandle, customer.Id);
        }

        response.Subscription = MapSubscription(subscription);
        return Results.Created($"api/my-subscriptions/{subscription.Id}", response);
    }

    private static SubscriptionDto MapSubscription(Maxio.MaxioSubscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            PlanName = sub.Product?.Name ?? string.Empty,
            PlanHandle = sub.Product?.Handle ?? string.Empty,
            Price = (sub.Product?.PriceInCents ?? 0) / 100m,
            NextBillingDate = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            PaymentCollectionMethod = sub.PaymentCollectionMethod
        };
    }
}
