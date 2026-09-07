using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(MaxioAdvancedBillingClient client, IHttpContextAccessor httpContextAccessor)
    {
        _client = client;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscribeRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<SubscribeResponse>()
            .WithName("CreateSubscription")
            .Produces(400)
            .Produces(500)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Results.Unauthorized();
            }

            var userId = httpContext.User.FindFirst("sub")?.Value ??
                        httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { error = "User identity not found" });
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "Plan handle is required" });
            }

            Customer customer = await GetOrCreateCustomerAsync(userId);

            var subscription = await CreateSubscriptionAsync(customer.Id ?? 0, request.PlanHandle, userId);

            return Results.Created($"api/my-subscriptions/{subscription.Id}", new SubscribeResponse
            {
                Subscription = new SubscriptionDto
                {
                    Id = subscription.Id ?? 0,
                    State = subscription.State?.Value,
                    ProductPriceInCents = subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CreatedAt = subscription.CreatedAt,
                    UpdatedAt = subscription.UpdatedAt,
                    Reference = subscription.Reference,
                    CouponCode = subscription.CouponCode,
                    CouponCodes = subscription.CouponCodes
                }
            });
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Results.StatusCode(500);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                return Results.BadRequest(new { errors = validationError.Errors });
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                return Results.StatusCode((int?)raw.StatusCode ?? 500);
            }
            return Results.StatusCode(500);
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string userId)
    {
        try
        {
            var existingCustomer = await _client.Customers.ReadCustomerByReference(userId, CancellationToken.None);
            return existingCustomer.Customer!;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "eShop",
                    LastName = "User",
                    Email = $"{userId}@eshopweb.local",
                    Reference = userId
                }
            };

            try
            {
                var createdCustomer = await _client.Customers.CreateCustomer(createRequest, CancellationToken.None);
                return createdCustomer.Customer!;
            }
            catch (SdkException<CreateCustomerError> creationEx)
            {
                if (creationEx.Error.TryGetCustomerErrorResponse1(out var error422))
                {
                    if (error422.Errors?.PerPage?.Any(e => e == "Reference") == true)
                    {
                        var retry = await _client.Customers.ReadCustomerByReference(userId, CancellationToken.None);
                        return retry.Customer!;
                    }
                }
                throw;
            }
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, string userId)
    {
        var subRef = $"{customerId}:{planHandle}";

        var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                Reference = subRef
            }
        };

        var response = await _client.Subscriptions.CreateSubscription(subscriptionRequest, CancellationToken.None);
        return response.Subscription!;
    }

}

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse
{
    public SubscriptionDto? Subscription { get; set; }
}
