using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext>
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(
        IMaxioApiClient maxioClient,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .RequireAuthorization(auth => auth.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser())
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var email = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? $"{userId}@eshop.local";
        var firstName = httpContext.User?.FindFirst("given_name")?.Value ?? "eShop";
        var lastName = httpContext.User?.FindFirst("family_name")?.Value ?? "User";

        _logger.LogInformation("Creating subscription for user {UserId}, product {ProductHandle}", userId, request.ProductHandle);

        // Step 1: Find or create Maxio customer (idempotent)
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            customer = await _maxioClient.CreateCustomerAsync(userId, email, firstName, lastName);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", customer.Id, userId);
        }
        else
        {
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for user {UserId}", customer.Id, userId);
        }

        // Step 2: Create subscription with uniqueness token for idempotency
        var uniquenessToken = Guid.NewGuid().ToString();
        var subscription = await _maxioClient.CreateSubscriptionAsync(request.ProductHandle, customer.Id, uniquenessToken);
        _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", subscription.Id, customer.Id);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = MapToDto(subscription)
        };

        return Results.Created($"/api/my-subscriptions", response);
    }

    private static SubscriptionDto MapToDto(MaxioSubscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            ProductHandle = sub.ProductHandle,
            ProductName = sub.Product?.Name ?? string.Empty,
            Price = sub.ProductPriceInCents / 100m,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            CancelAtEndOfPeriod = sub.CancelAtEndOfPeriod,
            CustomerEmail = sub.Customer?.Email ?? string.Empty
        };
    }
}
