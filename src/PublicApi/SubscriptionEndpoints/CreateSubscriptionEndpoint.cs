using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequestDto>
{
    private HttpContext? HttpContext { get; set; }
    private UserManager<ApplicationUser>? UserManager { get; set; }
    private MaxioAdvancedBillingClient? MaxioClient { get; set; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequestDto request, HttpContext httpContext, UserManager<ApplicationUser> userManager, MaxioAdvancedBillingClient maxioClient) =>
            {
                var endpoint = new CreateSubscriptionEndpoint
                {
                    HttpContext = httpContext,
                    UserManager = userManager,
                    MaxioClient = maxioClient
                };
                return await endpoint.HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequestDto request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await UserManager!.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            int maxioCustomerId;

            if (int.TryParse(user.MaxioCustomerId, out var existingCustomerId) && existingCustomerId > 0)
            {
                maxioCustomerId = existingCustomerId;
            }
            else
            {
                maxioCustomerId = await GetOrCreateMaxioCustomer(MaxioClient!, user);
                user.MaxioCustomerId = maxioCustomerId.ToString();
                await UserManager!.UpdateAsync(user);
            }

            var subscriptionRequest = new CreateSubscription
            {
                ProductHandle = request.PlanHandle,
                CustomerId = maxioCustomerId
            };

            var sdkRequest = new CreateSubscriptionRequest
            {
                Subscription = subscriptionRequest
            };

            var subscriptionResponse = await MaxioClient!.Subscriptions.CreateSubscription(
                body: sdkRequest,
                ct: default);

            var subscription = subscriptionResponse.Subscription;
            response.SubscriptionId = subscription?.Id ?? 0;
            response.State = subscription?.State?.Value ?? string.Empty;
            response.NextBillingAt = subscription?.NextAssessmentAt;
            response.ProductHandle = subscription?.Product?.Handle ?? string.Empty;
            response.ProductName = subscription?.Product?.Name ?? string.Empty;
            response.ProductPriceInCents = subscription?.ProductPriceInCents ?? 0;

            return Results.Created($"api/subscriptions/{response.SubscriptionId}", response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                return Results.BadRequest(new { errors = errorList.Errors });
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                return Results.StatusCode((int?)rawError.StatusCode ?? 400);
            }
            return Results.BadRequest();
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception)
        {
            return Results.StatusCode(500);
        }
    }

    private async Task<int> GetOrCreateMaxioCustomer(MaxioAdvancedBillingClient maxioClient, ApplicationUser user)
    {
        try
        {
            var existingCustomer = await maxioClient.Customers.ReadCustomerByReference(
                reference: user.Email!,
                ct: default);

            return existingCustomer.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            var firstName = !string.IsNullOrEmpty(user.UserName) ? user.UserName.Split('@')[0] : "Customer";
            var lastName = !string.IsNullOrEmpty(user.UserName) ? user.UserName.Split('@')[0] : "Account";

            var createCustomerRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = user.Email!,
                    Reference = user.Id
                }
            };

            var createdCustomer = await maxioClient.Customers.CreateCustomer(
                body: createCustomerRequest,
                ct: default);

            return createdCustomer.Customer?.Id ?? throw new InvalidOperationException("Failed to create customer");
        }
    }
}

public class CreateSubscriptionRequestDto : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
}
