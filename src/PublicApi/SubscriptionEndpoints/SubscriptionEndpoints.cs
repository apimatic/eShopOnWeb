using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpoints
{
    public static void AddSubscriptionRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
            SubscribeRequest request,
            MaxioAdvancedBillingClient client,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var response = new CreateSubscriptionResponse();
            var userReference = httpContext.User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(userReference))
                return Results.Unauthorized();

            try
            {
                CustomerResponse customerResponse;
                try
                {
                    customerResponse = await client.Customers.ReadCustomerByReference(
                        reference: userReference,
                        ct: ct);
                }
                catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    customerResponse = await client.Customers.CreateCustomer(
                        body: new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = userReference,
                                LastName = "",
                                Email = $"{userReference}@eshop.local",
                                Reference = userReference
                            }
                        },
                        ct: ct);
                }

                var customerId = customerResponse.Customer?.Id;

                var subscriptionResponse = await client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = request.ProductHandle,
                            CustomerId = customerId
                        }
                    },
                    ct: ct);

                var sub = subscriptionResponse.Subscription;
                response.Subscription = new SubscriptionDto
                {
                    Id = sub?.Id ?? 0,
                    State = sub?.State?.Value ?? "",
                    ProductPriceInCents = sub?.ProductPriceInCents ?? 0,
                    NextAssessmentAt = sub?.NextAssessmentAt,
                    CurrentPeriodEndsAt = sub?.CurrentPeriodEndsAt,
                    ActivatedAt = sub?.ActivatedAt,
                    ProductHandle = request.ProductHandle,
                    ProductName = sub?.Product?.Name ?? ""
                };

                return Results.Ok(response);
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                if (ex.Error.TryGetCustomerErrorResponse1(out var errorBody))
                {
                    var errors = errorBody.Errors;
                    var detail = errors?.ToString() ?? "Customer creation failed";
                    return Results.Problem(detail, statusCode: 422);
                }
                if (ex.Error.TryGetRawError(out var rawError))
                    return Results.Problem(rawError.ReadAsString(), statusCode: (int)rawError.StatusCode);
                return Results.Problem("Failed to create customer.", statusCode: 500);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var messages = string.Join("; ", errorList.Errors);
                    return Results.Problem(messages, statusCode: 422);
                }
                if (ex.Error.TryGetRawError(out var rawError))
                    return Results.Problem(rawError.ReadAsString(), statusCode: (int)rawError.StatusCode);
                return Results.Problem("Failed to create subscription.", statusCode: 500);
            }
            catch (SdkException<RawError> ex)
            {
                return Results.Problem(ex.Error.ReadAsString(), statusCode: (int)ex.Error.StatusCode);
            }
            catch (JsonException)
            {
                return Results.Problem(
                    "The billing service returned an unexpected response. Please try again later.",
                    statusCode: 502);
            }
        })
        .WithName("CreateSubscription")
        .Produces<CreateSubscriptionResponse>()
        .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", async (
            MaxioAdvancedBillingClient client,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userReference = httpContext.User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(userReference))
                return Results.Problem("Unable to identify the authenticated user.", statusCode: 401);

            var response = new MySubscriptionsResponse();

            try
            {
                CustomerResponse customerResponse;
                try
                {
                    customerResponse = await client.Customers.ReadCustomerByReference(
                        reference: userReference,
                        ct: ct);
                }
                catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    return Results.Ok(response);
                }

                var customerId = customerResponse.Customer?.Id;
                if (customerId == null)
                    return Results.Ok(response);

                var subscriptions = await client.Customers.ListCustomerSubscriptions(
                    customerId: customerId.Value,
                    ct: ct);

                foreach (var subResponse in subscriptions)
                {
                    var sub = subResponse.Subscription;
                    if (sub == null) continue;

                    response.Subscriptions.Add(new SubscriptionDto
                    {
                        Id = sub.Id ?? 0,
                        State = sub.State?.Value ?? "",
                        ProductPriceInCents = sub.ProductPriceInCents ?? 0,
                        NextAssessmentAt = sub.NextAssessmentAt,
                        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                        ActivatedAt = sub.ActivatedAt,
                        ProductHandle = sub.Product?.Handle ?? "",
                        ProductName = sub.Product?.Name ?? ""
                    });
                }
            }
            catch (SdkException<RawError> ex)
            {
                return Results.Problem(ex.Error.ReadAsString(), statusCode: (int)ex.Error.StatusCode);
            }
            catch (JsonException)
            {
                return Results.Problem(
                    "The billing service returned an unexpected response. Please try again later.",
                    statusCode: 502);
            }

            return Results.Ok(response);
        })
        .WithName("GetMySubscriptions")
        .Produces<MySubscriptionsResponse>()
        .WithTags("SubscriptionEndpoints");
    }
}

public class SubscribeRequest
{
    public string ProductHandle { get; set; } = "";
    public int? Quantity { get; set; }
}

public class CreateSubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

public class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
}
