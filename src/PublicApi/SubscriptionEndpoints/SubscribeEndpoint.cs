using System;
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

/// <summary>
/// Subscribe to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioApiClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, IMaxioApiClient maxio) =>
            {
                return await HandleAsync(request, maxio);
            })
           .Produces<SubscribeResponse>()
           .Produces(400)
           .Produces(409)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioApiClient maxio)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var user = _httpContextAccessor.HttpContext?.User;
        var email = user?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(email))
        {
            return Results.BadRequest(new { error = "Unable to determine user identity from token." });
        }

        // Idempotent customer lookup / creation
        var customer = await maxio.FindCustomerByEmailAsync(email);
        if (customer == null)
        {
            var nameParts = email.Split('@')[0];
            customer = await maxio.CreateCustomerAsync(new MaxioCustomerCreateRequest
            {
                FirstName = nameParts,
                LastName = "User",
                Email = email,
                Reference = email
            });
        }

        var subscription = await maxio.CreateSubscriptionAsync(new MaxioSubscriptionCreateRequest
        {
            ProductHandle = request.ProductHandle,
            CustomerId = customer.Id,
            Reference = $"{email}:{request.ProductHandle}",
            PaymentCollectionMethod = "remittance"
        });

        response.Subscription = MapSubscription(subscription);
        return Results.Created($"/api/my-subscriptions", response);
    }

    private static SubscriptionDto MapSubscription(MaxioSubscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            BalanceInCents = sub.BalanceInCents,
            TotalRevenueInCents = sub.TotalRevenueInCents,
            ProductPriceInCents = sub.ProductPriceInCents,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextBillingDate = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            ProductHandle = sub.Product?.Handle,
            ProductName = sub.Product?.Name,
            PlanInterval = sub.Product?.IntervalUnit,
            PlanIntervalCount = sub.Product?.Interval
        };
    }
}
