using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;

    public SubscriptionCreateEndpoint(MaxioAdvancedBillingClient client, IConfiguration configuration)
    {
        _client = client;
        _configuration = configuration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleSubscriptionCreate(request, user, ct);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    async Task<IResult> IEndpoint<IResult>.HandleAsync()
    {
        return Results.BadRequest();
    }

    public async Task<IResult> HandleSubscriptionCreate(SubscriptionCreateRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        try
        {
            var userName = user?.FindFirst(ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var customerReference = $"{userName}";

            var customer = await GetOrCreateCustomerAsync(customerReference, userName, cancellationToken);
            if (customer == null || customer.Id == null)
            {
                return Results.StatusCode(500);
            }

            var subscriptionRequest = new global::MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.ProductHandle,
                    CustomerReference = customerReference,
                    PaymentCollectionMethod = null
                }
            };

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: cancellationToken);

            var subscription = subscriptionResponse.Subscription;

            return Results.Created(
                $"/api/subscriptions/{subscription?.Id}",
                new SubscriptionCreateResponse
                {
                    Id = subscription?.Id,
                    State = subscription?.State?.ToString(),
                    ProductPriceInCents = subscription?.ProductPriceInCents,
                    NextAssessmentAt = subscription?.NextAssessmentAt,
                    ActivatedAt = subscription?.ActivatedAt
                });
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                return Results.BadRequest(new { errors = errorList.Errors });
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                return Results.StatusCode((int?)raw.StatusCode ?? 500);
            }
            return Results.StatusCode(500);
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

    private async Task<Customer?> GetOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        try
        {
            var customerResponse = await _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: cancellationToken);
            return customerResponse.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "",
                    LastName = email?.Split('@')[0] ?? "User",
                    Email = email,
                    Reference = reference
                }
            };

            var createResponse = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: cancellationToken);
            return createResponse.Customer;
        }
    }
}

public class SubscriptionCreateRequest
{
    public string? ProductHandle { get; set; }
}

public class SubscriptionCreateResponse
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
