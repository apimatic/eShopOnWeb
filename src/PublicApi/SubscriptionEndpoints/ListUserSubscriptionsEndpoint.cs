using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IRepository<MaxioSubscription> _subscriptionRepository;

    public ListUserSubscriptionsEndpoint(MaxioAdvancedBillingClient maxioClient, IRepository<MaxioSubscription> subscriptionRepository)
    {
        _maxioClient = maxioClient;
        _subscriptionRepository = subscriptionRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var userSubscriptions = await _subscriptionRepository.ListAsync(
                        new UserSubscriptionsSpecification(userId));

                    if (!userSubscriptions.Any())
                    {
                        return Results.Ok(new ListUserSubscriptionsResponse { Subscriptions = new() });
                    }

                    var maxioCustomerId = userSubscriptions.First().MaxioCustomerId;
                    var maxioSubscriptions = await _maxioClient.Customers.ListCustomerSubscriptions(
                        customerId: maxioCustomerId,
                        ct: default);

                    var subscriptions = maxioSubscriptions
                        .Select(s => new SubscriptionDto
                        {
                            Id = s.Subscription?.Id ?? 0,
                            CustomerId = s.Subscription?.Id ?? 0,
                            ProductHandle = s.Subscription?.Product?.Handle ?? string.Empty,
                            State = s.Subscription?.State?.ToString() ?? string.Empty,
                            NextBillingAt = s.Subscription?.NextAssessmentAt,
                            CreatedAt = s.Subscription?.CreatedAt ?? DateTimeOffset.UtcNow
                        })
                        .ToList();

                    return Results.Ok(new ListUserSubscriptionsResponse { Subscriptions = subscriptions });
                }
                catch (SdkException<RawError> ex)
                {
                    int statusCode = ex.Error.StatusCode != null ? (int)ex.Error.StatusCode : (int)HttpStatusCode.InternalServerError;
                    return Results.StatusCode(statusCode);
                }
            })
            .WithName("GetMySubscriptions")
            .Produces<ListUserSubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        return Results.Ok(new ListUserSubscriptionsResponse());
    }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
