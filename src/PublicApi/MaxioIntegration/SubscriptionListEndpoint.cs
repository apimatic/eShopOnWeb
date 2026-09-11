using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class SubscriptionListEndpoint : IEndpoint<IResult, SubscriptionListRequest>
{
    private readonly IMaxioApiClient _maxioClient;

    public SubscriptionListEndpoint(IMaxioApiClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (HttpRequest httpRequest) =>
        {
            return await HandleAsync(new SubscriptionListRequest(), httpRequest);
        })
        .Produces<SubscriptionListResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionListRequest request, HttpRequest httpRequest)
    {
        var response = new SubscriptionListResponse(request.CorrelationId());

        var userId = httpRequest.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            response.IsSuccess = false;
            response.ErrorMessage = "User not authenticated.";
            return Results.Unauthorized();
        }

        try
        {
            var customer = await _maxioClient.FindCustomerByReferenceAsync(userId);
            if (customer == null)
            {
                response.IsSuccess = true;
                response.Subscriptions = new();
                return Results.Ok(response);
            }

            var subscriptions = await _maxioClient.GetSubscriptionsByCustomerIdAsync(customer.Id);
            response.IsSuccess = true;
            response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductHandle = s.Product?.Handle ?? string.Empty,
                ProductName = s.Product?.Name ?? string.Empty,
                PriceInCents = s.ProductPriceInCents,
                Price = s.ProductPriceInCents / 100m,
                NextBillingDate = s.NextAssessmentAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Failed to load subscriptions: {ex.Message}";
        }

        return Results.Ok(response);
    }
}
