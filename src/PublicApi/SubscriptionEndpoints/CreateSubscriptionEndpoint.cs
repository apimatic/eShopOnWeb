using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioApiClient>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _context;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, CatalogContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioApiClient maxioClient, HttpContext httpContext) =>
            {
                var endpoint = new CreateSubscriptionEndpointHandler(_userManager, _context);
                return await endpoint.HandleAsync(request, maxioClient, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiClient maxioClient)
    {
        throw new NotImplementedException("Use CreateSubscriptionEndpointHandler directly");
    }
}

internal class CreateSubscriptionEndpointHandler
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _context;

    public CreateSubscriptionEndpointHandler(UserManager<ApplicationUser> userManager, CatalogContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiClient maxioClient, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound();
            }

            // Get or create Maxio customer
            var maxioCustomer = await maxioClient.GetOrCreateCustomerAsync(
                userId,
                user.UserName ?? "User",
                user.UserName ?? "User",
                user.Email ?? "");

            if (maxioCustomer == null)
            {
                return Results.BadRequest(new { error = "Failed to create Maxio customer" });
            }

            // Store mapping if new
            var existingMapping = _context.MaxioCustomerMappings.FirstOrDefault(m => m.EshopUserId == userId);
            if (existingMapping == null)
            {
                var mapping = new MaxioCustomerMapping
                {
                    EshopUserId = userId,
                    MaxioCustomerId = maxioCustomer.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.MaxioCustomerMappings.Add(mapping);
                await _context.SaveChangesAsync();
            }

            // Create subscription
            var subscription = await maxioClient.CreateSubscriptionAsync(
                maxioCustomer.Id,
                request.ProductHandle);

            if (subscription == null)
            {
                return Results.BadRequest(new { error = "Failed to create subscription" });
            }

            var response = new CreateSubscriptionResponse(Guid.NewGuid())
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductName = subscription.Product?.Name ?? "",
                ProductHandle = subscription.Product?.Handle ?? "",
                Price = subscription.Product != null ? subscription.Product.PriceInCents / 100m : 0,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
}
