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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioClient _maxioClient;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, CreateSubscriptionRequest request) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
                var contextRequest = new CreateSubscriptionRequest
                {
                    ProductHandle = request.ProductHandle,
                    UserId = userId,
                    UserName = userName,
                    UserEmail = email
                };
                return await HandleAsync(contextRequest);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = request.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                response.Success = false;
                response.Message = "User not authenticated";
                return Results.Unauthorized();
            }

            // Try to find existing customer by reference (userId)
            var existingCustomer = await _maxioClient.LookupCustomerAsync(userId);

            int customerId;
            if (existingCustomer != null)
            {
                customerId = existingCustomer.Id;
            }
            else
            {
                // Extract user name from claims if available
                var userName = request.UserName ?? userId;
                var userEmail = request.UserEmail ?? $"{userId}@eshop.local";

                var nameParts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var firstName = nameParts.Length > 0 ? nameParts[0] : "User";
                var lastName = nameParts.Length > 1 ? nameParts[1] : userId;

                var createCustomerRequest = new CreateCustomerRequest
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = userEmail,
                    Reference = userId
                };

                var newCustomer = await _maxioClient.CreateCustomerAsync(createCustomerRequest);
                if (newCustomer == null)
                {
                    response.Success = false;
                    response.Message = "Failed to create customer in billing system";
                    return Results.BadRequest(response);
                }

                customerId = newCustomer.Id;
            }

            // Create subscription
            var createSubRequest = new Maxio.MaxioCreateSubscriptionRequest
            {
                ProductHandle = request.ProductHandle,
                CustomerId = customerId
            };

            var subscription = await _maxioClient.CreateSubscriptionAsync(createSubRequest);
            if (subscription != null)
            {
                response.Success = true;
                response.SubscriptionId = subscription.Id;
                response.State = subscription.State;
                response.CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt;
                response.Product = subscription.Product != null ? new SubscriptionPlanDto
                {
                    Id = subscription.Product.Id,
                    Name = subscription.Product.Name,
                    PriceInCents = subscription.Product.PriceInCents,
                    Interval = subscription.Product.Interval,
                    IntervalUnit = subscription.Product.IntervalUnit
                } : null;
                return Results.Created($"api/subscriptions/{subscription.Id}", response);
            }
            else
            {
                response.Success = false;
                response.Message = "Failed to create subscription";
                return Results.BadRequest(response);
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error creating subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    public int CustomerId { get; set; }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public SubscriptionPlanDto? Product { get; set; }
}
