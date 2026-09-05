using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IHttpContextAccessor>
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IHttpContextAccessor contextAccessor) =>
            {
                return await HandleAsync(request, contextAccessor);
            })
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .RequireAuthorization()
           .WithName("CreateSubscription")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IHttpContextAccessor contextAccessor)
    {
        try
        {
            var httpContext = contextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Results.BadRequest("No HTTP context");
            }

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound("User not found");
            }

            var customerReference = $"eshop-user-{user.Id}";
            var customer = await _maxioClient.GetOrCreateCustomerAsync(
                customerReference,
                user.UserName ?? "User",
                user.UserName ?? "User",
                user.Email ?? ""
            );

            if (customer == null)
            {
                return Results.BadRequest("Failed to create or retrieve customer");
            }

            user.MaxioCustomerId = customer.Customer.Id;
            await _userManager.UpdateAsync(user);

            var subscriptionRequest = new SubscriptionCreateRequest
            {
                Subscription = new SubscriptionCreate
                {
                    Product_handle = request.ProductHandle,
                    Customer_id = customer.Customer.Id,
                    Payment_collection_method = "remittance"
                }
            };

            var subscription = await _maxioClient.CreateSubscriptionAsync(subscriptionRequest);

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Subscription.Id,
                State = subscription.Subscription.State,
                ProductName = subscription.Subscription.Product.Name,
                MonthlyPrice = subscription.Subscription.Product_price_in_cents.HasValue
                    ? subscription.Subscription.Product_price_in_cents.Value / 100m
                    : null,
                NextBillingDate = subscription.Subscription.Current_period_ends_at,
                Message = $"Subscription created successfully. Next billing date: {subscription.Subscription.Current_period_ends_at:yyyy-MM-dd}"
            };

            return Results.Created($"/api/subscriptions/{subscription.Subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message, details = ex.InnerException?.Message });
        }
    }
}
