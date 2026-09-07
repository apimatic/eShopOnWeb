using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionApiRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly MaxioSettings _maxioSettings;
    private readonly IRepository<MaxioSubscription> _subscriptionRepository;

    public CreateSubscriptionEndpoint(MaxioAdvancedBillingClient maxioClient, Microsoft.Extensions.Options.IOptions<MaxioSettings> maxioSettings, IRepository<MaxioSubscription> subscriptionRepository)
    {
        _maxioClient = maxioClient;
        _maxioSettings = maxioSettings.Value;
        _subscriptionRepository = subscriptionRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionApiRequest request, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    int maxioCustomerId = await EnsureMaxioCustomerExists(userId);

                    var createSubRequest = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            CustomerId = maxioCustomerId,
                            ProductHandle = request.ProductHandle,
                            Reference = userId
                        }
                    };

                    var subResponse = await _maxioClient.Subscriptions.CreateSubscription(body: createSubRequest, ct: default);
                    var sub = subResponse.Subscription;

                    var maxioSub = new MaxioSubscription
                    {
                        UserId = userId,
                        MaxioCustomerId = maxioCustomerId,
                        MaxioSubscriptionId = sub?.Id ?? 0,
                        ProductHandle = request.ProductHandle,
                        State = "active",
                        NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1),
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };

                    await _subscriptionRepository.AddAsync(maxioSub);

                    return Results.Ok(new CreateSubscriptionResponse
                    {
                        SubscriptionId = sub?.Id ?? 0,
                        State = "active",
                        NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1),
                        PriceInCents = 0
                    });
                }
                catch (SdkException<RawError> ex)
                {
                    int statusCode = ex.Error.StatusCode != null ? (int)ex.Error.StatusCode : (int)HttpStatusCode.InternalServerError;
                    return Results.StatusCode(statusCode);
                }
            })
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request)
    {
        return Results.Ok(new CreateSubscriptionResponse());
    }

    private async Task<int> EnsureMaxioCustomerExists(string userId)
    {
        try
        {
            var existing = await _maxioClient.Customers.ReadCustomerByReference(reference: userId, ct: default);
            if (existing?.Customer?.Id.HasValue == true)
            {
                return existing.Customer.Id.Value;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        var createCustomerRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "User",
                LastName = userId.Substring(0, Math.Min(10, userId.Length)),
                Email = $"{userId}@eshop.local",
                Reference = userId
            }
        };

        var custResponse = await _maxioClient.Customers.CreateCustomer(body: createCustomerRequest, ct: default);
        return custResponse?.Customer?.Id ?? 0;
    }
}

public class CreateSubscriptionApiRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public long PriceInCents { get; set; }
}
