using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a new subscription for the authenticated user
/// </summary>
public partial class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscriptionCreationRequest, MaxioAdvancedBillingClient>
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionCreationRequest request, MaxioAdvancedBillingClient maxioClient) =>
            {
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreationRequest request, MaxioAdvancedBillingClient maxioClient)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Results.StatusCode(500);
        }
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new { error = "ProductHandle is required" });
            }

            Customer? customer = null;
            try
            {
                var existingCustomer = await maxioClient.Customers.ReadCustomerByReference(reference: userEmail, ct: default);
                customer = existingCustomer.Customer;
            }
            catch (System.Net.Http.HttpRequestException)
            {
            }
            catch (Exception)
            {
            }

            if (customer == null)
            {
                var maxioCreateCustomerRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        FirstName = userEmail.Split('@')[0],
                        LastName = "Customer",
                        Email = userEmail,
                        Reference = userEmail
                    }
                };

                try
                {
                    var customerResponse = await maxioClient.Customers.CreateCustomer(body: maxioCreateCustomerRequest, ct: default);
                    customer = customerResponse.Customer;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    return Results.BadRequest(new { error = "Failed to create customer", details = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.StatusCode(500);
                }
            }

            if (customer?.Id == null || customer.Id <= 0)
            {
                return Results.BadRequest(new { error = "Failed to get or create customer" });
            }

            var maxioCreateSubscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerId = (int)customer.Id,
                    ProductHandle = request.ProductHandle
                }
            };

            try
            {
                var subscriptionResponse = await maxioClient.Subscriptions.CreateSubscription(body: maxioCreateSubscriptionRequest, ct: default);

                if (subscriptionResponse.Subscription == null)
                {
                    return Results.BadRequest(new { error = "Failed to create subscription" });
                }

                var subscription = subscriptionResponse.Subscription;
                response.Subscription = new SubscriptionDto
                {
                    Id = subscription.Id ?? 0,
                    State = subscription.State?.ToString(),
                    ProductName = subscription.Product?.Name,
                    ProductHandle = subscription.Product?.Handle,
                    Price = (decimal?)subscription.Product?.PriceInCents / 100m ?? 0m,
                    NextBillingDate = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt
                };

                return Results.Created($"api/subscriptions/{subscription.Id}", response);
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.BadRequest(new { error = "Failed to parse subscription response", details = ex.Message });
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                return Results.StatusCode(503);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Results.BadRequest(new { error = "Invalid request", details = ex.Message });
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            return Results.StatusCode(503);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}
