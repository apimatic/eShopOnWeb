using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated user to a Maxio plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioApiClient>
{
    private readonly MaxioConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IOptions<MaxioConfiguration> config,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _config = config.Value;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, MaxioApiClient maxioClient) =>
            {
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioApiClient maxioClient)
    {
        var response = new CreateSubscriptionResponse();

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userId);
        if (user == null)
            return Results.Unauthorized();

        var email = user.Email ?? $"{userId}@eshop.local";
        var customerReference = $"eshop-{user.Id}";

        var customer = await maxioClient.EnsureCustomerAsync(
            customerReference,
            user.UserName ?? userId,
            "Subscriber",
            email);

        if (customer?.Customer == null)
        {
            return Results.StatusCode(502);
        }

        // Idempotency: check for existing active subscription to the same plan
        var existingSubscriptions = await maxioClient.ListSubscriptionsAsync(state: "active");
        if (existingSubscriptions != null)
        {
            var existing = existingSubscriptions.FirstOrDefault(s =>
                s.Subscription?.Customer?.Reference == customerReference
                && s.Subscription?.Product?.Handle == request.ProductHandle
                && s.Subscription?.State == "active");

            if (existing?.Subscription != null)
            {
                response.Subscription = new SubscriptionDto
                {
                    Id = existing.Subscription.Id,
                    State = existing.Subscription.State,
                    Price = existing.Subscription.ProductPriceInCents / 100m,
                    Currency = existing.Subscription.Currency,
                    CurrentPeriodEndsAt = existing.Subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = existing.Subscription.NextAssessmentAt,
                    ActivatedAt = existing.Subscription.ActivatedAt,
                    CreatedAt = existing.Subscription.CreatedAt,
                    CanceledAt = existing.Subscription.CanceledAt,
                    ProductName = existing.Subscription.Product?.Name,
                    ProductHandle = existing.Subscription.Product?.Handle
                };
                return Results.Ok(response);
            }
        }

        var subscriptionRef = $"{customerReference}-{request.ProductHandle}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var subscription = await maxioClient.CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
        {
            ProductHandle = request.ProductHandle,
            CustomerId = customer.Customer.Id,
            Reference = subscriptionRef,
            PaymentCollectionMethod = "remittance"
        });

        if (subscription?.Subscription == null)
        {
            return Results.StatusCode(502);
        }

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Subscription.Id,
            State = subscription.Subscription.State,
            Price = subscription.Subscription.ProductPriceInCents / 100m,
            Currency = subscription.Subscription.Currency,
            CurrentPeriodEndsAt = subscription.Subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.Subscription.NextAssessmentAt,
            ActivatedAt = subscription.Subscription.ActivatedAt,
            CreatedAt = subscription.Subscription.CreatedAt,
            CanceledAt = subscription.Subscription.CanceledAt,
            ProductName = subscription.Subscription.Product?.Name,
            ProductHandle = subscription.Subscription.Product?.Handle
        };

        return Results.Created($"/api/my-subscriptions", response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
}
