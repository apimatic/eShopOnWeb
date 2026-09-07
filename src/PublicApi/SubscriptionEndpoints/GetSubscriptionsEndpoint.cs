using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetSubscriptionsEndpoint(MaxioAdvancedBillingClient client, IHttpContextAccessor httpContextAccessor)
    {
        _client = client;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                GetSubscriptionsEndpoint endpoint) =>
            {
                return await endpoint.HandleAsync();
            })
            .Produces<GetSubscriptionsResponse>()
            .Produces(500)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
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

            try
            {
                var customer = await _client.Customers.ReadCustomerByReference(userId, CancellationToken.None);
                if (customer.Customer?.Id == null)
                {
                    return Results.Ok(new GetSubscriptionsResponse { Subscriptions = new() });
                }

                var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                    customerId: customer.Customer.Id.Value,
                    ct: CancellationToken.None);

                var response = new GetSubscriptionsResponse();
                response.Subscriptions.AddRange(subscriptions
                    .Where(s => s.Subscription != null)
                    .Select(s => new SubscriptionDto
                    {
                        Id = s.Subscription.Id ?? 0,
                        State = s.Subscription.State?.Value,
                        ProductPriceInCents = s.Subscription.ProductPriceInCents,
                        CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt,
                        NextAssessmentAt = s.Subscription.NextAssessmentAt,
                        ActivatedAt = s.Subscription.ActivatedAt,
                        CreatedAt = s.Subscription.CreatedAt,
                        UpdatedAt = s.Subscription.UpdatedAt,
                        Reference = s.Subscription.Reference,
                        CouponCode = s.Subscription.CouponCode,
                        CouponCodes = s.Subscription.CouponCodes
                    }));

                return Results.Ok(response);
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return Results.Ok(new GetSubscriptionsResponse { Subscriptions = new() });
                }
                return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }

}

public class GetSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
