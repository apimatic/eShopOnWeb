using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioApiClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(MaxioApiClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
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

        var customerId = await GetOrCreateMaxioCustomer(user);
        if (customerId == null)
        {
            return Results.BadRequest(new { error = "Failed to create or find Maxio customer" });
        }

        var subscriptionResponse = await CreateMaxioSubscription(customerId.Value, request.ProductHandle);
        if (subscriptionResponse == null)
        {
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }

        var sub = subscriptionResponse.Subscription;
        var response = new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDto
            {
                Id = sub!.Id,
                State = sub.State,
                ProductName = sub.Product?.Name ?? string.Empty,
                ProductHandle = sub.Product?.Handle ?? string.Empty,
                ProductPriceInDollars = (sub.Product?.PriceInCents ?? 0) / 100m,
                CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                NextAssessmentAt = sub.NextAssessmentAt,
                ActivatedAt = sub.ActivatedAt,
                CreatedAt = sub.CreatedAt,
                UpdatedAt = sub.UpdatedAt
            }
        };

        return Results.Created($"/api/subscriptions/{response.Subscription.Id}", response);
    }

    private async Task<int?> GetOrCreateMaxioCustomer(ApplicationUser user)
    {
        var reference = user.Id;
        var email = user.Email ?? string.Empty;
        var firstName = user.UserName ?? "User";

        var lookupResponse = await _maxioClient.PostAsync<CustomerLookupResponse>(
            "/customers/lookup.json",
            new { reference = reference }
        );

        if (lookupResponse?.Customer != null)
        {
            return lookupResponse.Customer.Id;
        }

        var createRequest = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = "User",
                email = email,
                reference = reference
            }
        };

        var createResponse = await _maxioClient.PostAsync<CustomerCreateResponse>(
            "/customers.json",
            createRequest
        );

        return createResponse?.Customer?.Id;
    }

    private async Task<SubscriptionCreateResponse?> CreateMaxioSubscription(int customerId, string productHandle)
    {
        var request = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "remittance"
            }
        };

        return await _maxioClient.PostAsync<SubscriptionCreateResponse>("/subscriptions.json", request);
    }

    private class CustomerLookupResponse
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class CustomerCreateResponse
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class MaxioCustomer
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }
    }

    private class SubscriptionCreateResponse
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }

    private class MaxioSubscription
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("current_period_ends_at")]
        public DateTime CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("next_assessment_at")]
        public DateTime NextAssessmentAt { get; set; }

        [JsonPropertyName("activated_at")]
        public DateTime ActivatedAt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class MaxioProduct
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("handle")]
        public string Handle { get; set; } = string.Empty;

        [JsonPropertyName("price_in_cents")]
        public int? PriceInCents { get; set; }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
}
