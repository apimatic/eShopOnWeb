using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    private readonly MaxioApiClient _maxioClient;
    private readonly AppIdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        MaxioApiClient maxioClient,
        AppIdentityDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _dbContext = dbContext;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/subscriptions", Handle)
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();

    private async Task<IResult> Handle(CreateSubscriptionRequest request)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Results.Unauthorized();
            }

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var existingMapping = await _dbContext.MaxioCustomerMappings
                .FirstOrDefaultAsync(m => m.UserId == userId);

            int maxioCustomerId;

            if (existingMapping != null)
            {
                maxioCustomerId = existingMapping.MaxioCustomerId;
            }
            else
            {
                var customerRef = $"eshop-{userId}";
                var existingCustomer = await _maxioClient.FindCustomerByReferenceAsync(customerRef);

                if (existingCustomer?.Customer != null)
                {
                    maxioCustomerId = existingCustomer.Customer.Id;
                }
                else
                {
                    var createCustomerRequest = new CreateMaxioCustomerRequest
                    {
                        Customer = new MaxioCustomer
                        {
                            FirstName = user.UserName ?? "Customer",
                            LastName = "",
                            Email = user.Email ?? "",
                            Reference = customerRef
                        }
                    };

                    var customerResponse = await _maxioClient.CreateCustomerAsync(createCustomerRequest);
                    if (customerResponse?.Customer == null)
                    {
                        return Results.BadRequest(new { error = "Failed to create customer in Maxio" });
                    }

                    maxioCustomerId = customerResponse.Customer.Id;
                }

                var mapping = new MaxioCustomerMapping
                {
                    UserId = userId,
                    MaxioCustomerId = maxioCustomerId,
                    MaxioCustomerReference = $"eshop-{userId}",
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.MaxioCustomerMappings.Add(mapping);
                await _dbContext.SaveChangesAsync();
            }

            var subscriptionRequest = new CreateMaxioSubscriptionRequest
            {
                Subscription = new MaxioSubscription
                {
                    CustomerId = maxioCustomerId,
                    ProductHandle = request.ProductHandle,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var subscriptionResponse = await _maxioClient.CreateSubscriptionAsync(subscriptionRequest);
            if (subscriptionResponse?.Subscription == null)
            {
                return Results.BadRequest(new { error = "Failed to create subscription" });
            }

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscriptionResponse.Subscription.Id,
                State = subscriptionResponse.Subscription.State,
                ProductName = subscriptionResponse.Subscription.Product?.Name ?? "",
                ProductHandle = subscriptionResponse.Subscription.Product?.Handle ?? "",
                PriceInCents = subscriptionResponse.Subscription.Product?.PriceInCents ?? 0,
                NextBillingDate = subscriptionResponse.Subscription.NextAssessmentAt,
                CreatedAt = subscriptionResponse.Subscription.CreatedAt
            };

            return Results.Created($"api/subscriptions/{response.SubscriptionId}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CreatedAt { get; set; }
}
