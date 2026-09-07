using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequestDto>
{
    private HttpContext? HttpContext { get; set; }
    private UserManager<ApplicationUser>? UserManager { get; set; }
    private MaxioAdvancedBillingClient? MaxioClient { get; set; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, UserManager<ApplicationUser> userManager, MaxioAdvancedBillingClient maxioClient) =>
            {
                var endpoint = new GetMySubscriptionsEndpoint
                {
                    HttpContext = httpContext,
                    UserManager = userManager,
                    MaxioClient = maxioClient
                };
                return await endpoint.HandleAsync(new GetMySubscriptionsRequestDto());
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequestDto request)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var userId = HttpContext!.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await UserManager!.FindByIdAsync(userId);
            if (user == null || !int.TryParse(user.MaxioCustomerId, out var customerId) || customerId <= 0)
            {
                return Results.Ok(response);
            }

            var subscriptions = await MaxioClient!.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: default);

            foreach (var subscriptionResponse in subscriptions)
            {
                var subscription = subscriptionResponse.Subscription;
                if (subscription != null)
                {
                    response.Subscriptions.Add(new UserSubscriptionDto
                    {
                        SubscriptionId = subscription.Id ?? 0,
                        ProductHandle = subscription.Product?.Handle ?? string.Empty,
                        ProductName = subscription.Product?.Name ?? string.Empty,
                        ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
                        State = subscription.State?.Value ?? string.Empty,
                        NextBillingAt = subscription.NextAssessmentAt,
                        CreatedAt = subscription.CreatedAt
                    });
                }
            }

            return Results.Ok(response);
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
}

public class GetMySubscriptionsRequestDto : BaseRequest
{
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
