using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, CreateSubscriptionDependency>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext,
                   IMaxioBillingService billingService, UserManager<ApplicationUser> userManager,
                   CatalogContext catalogContext) =>
            {
                var deps = new CreateSubscriptionDependency(billingService, userManager, httpContext, catalogContext);
                var endpoint = new CreateSubscriptionEndpoint();
                return await endpoint.HandleAsync(request, deps);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithName("CreateSubscription")
            .WithTags("Subscriptions")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request,
        CreateSubscriptionDependency dependency)
    {
        var userId = dependency.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await dependency.UserManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.NotFound("User not found");
        }

        var customerInfo = await dependency.BillingService.GetOrCreateCustomerAsync(
            userId,
            user.UserName ?? "User",
            user.Email ?? string.Empty,
            user.Email ?? string.Empty);

        if (customerInfo == null)
        {
            return Results.BadRequest("Failed to create or retrieve customer");
        }

        var existingMapping = dependency.CatalogContext.MaxioCustomerMappings
            .FirstOrDefault(m => m.UserId == userId);
        if (existingMapping == null)
        {
            var mapping = new MaxioCustomerMapping
            {
                UserId = userId,
                MaxioCustomerId = customerInfo.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            dependency.CatalogContext.MaxioCustomerMappings.Add(mapping);
            await dependency.CatalogContext.SaveChangesAsync();
        }

        var subscription = await dependency.BillingService.CreateSubscriptionAsync(
            customerInfo.Id,
            request.ProductHandle);

        if (subscription == null)
        {
            return Results.BadRequest("Failed to create subscription");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductName = subscription.ProductName,
                ProductHandle = subscription.ProductHandle,
                Price = subscription.PricePerBillingCycle,
                BillingPeriod = subscription.BillingPeriod,
                NextBillingAt = subscription.NextBillingAt
            }
        };

        return Results.Created($"/api/subscriptions/{subscription.Id}", response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

public class CreateSubscriptionDependency
{
    public IMaxioBillingService BillingService { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public HttpContext HttpContext { get; }
    public CatalogContext CatalogContext { get; }

    public CreateSubscriptionDependency(
        IMaxioBillingService billingService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CatalogContext catalogContext)
    {
        BillingService = billingService;
        UserManager = userManager;
        HttpContext = httpContext;
        CatalogContext = catalogContext;
    }
}
