using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public partial class ListUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, MaxioAdvancedBillingClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListUserSubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioAdvancedBillingClient maxioClient) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioClient);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListUserSubscriptions");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, MaxioAdvancedBillingClient maxioClient)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Results.StatusCode(500);
        }
        var response = new ListUserSubscriptionsResponse(request.CorrelationId());

        try
        {
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                return Results.Unauthorized();
            }

            try
            {
                var customerResponse = await maxioClient.Customers.ReadCustomerByReference(reference: userEmail, ct: default);
                if (customerResponse.Customer?.Id == null || customerResponse.Customer.Id <= 0)
                {
                    return Results.Ok(response);
                }

                var subscriptions = await maxioClient.Customers.ListCustomerSubscriptions(
                    customerId: (int)customerResponse.Customer.Id,
                    ct: default);

                foreach (var subscriptionResponse in subscriptions)
                {
                    if (subscriptionResponse.Subscription == null)
                        continue;

                    var subscription = subscriptionResponse.Subscription;
                    response.Subscriptions.Add(new SubscriptionDto
                    {
                        Id = subscription.Id ?? 0,
                        State = subscription.State?.ToString(),
                        ProductName = subscription.Product?.Name,
                        ProductHandle = subscription.Product?.Handle,
                        Price = (decimal?)subscription.Product?.PriceInCents / 100m ?? 0m,
                        NextBillingDate = subscription.NextAssessmentAt,
                        ActivatedAt = subscription.ActivatedAt
                    });
                }

                return Results.Ok(response);
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

    public class ListUserSubscriptionsResponse : BaseResponse
    {
        public ListUserSubscriptionsResponse()
        {
        }

        public ListUserSubscriptionsResponse(System.Guid correlationId) : base(correlationId)
        {
        }

        public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; } = new();
    }
}
