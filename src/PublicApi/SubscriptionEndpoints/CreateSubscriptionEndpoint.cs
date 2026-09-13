using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a new subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(MaxioClient maxioClient, IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return Results.BadRequest(new { error = "No HTTP context available." });

        return await HandleAsync(request, httpContext);
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userEmail = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue("unique_name");

        if (string.IsNullOrEmpty(userEmail))
        {
            return Results.BadRequest(new { error = "User identity could not be determined from the token." });
        }

        var firstName = userEmail.Contains('@') ? userEmail.Split('@')[0] : userEmail;
        var lastName = "Subscriber";
        var customerReference = $"eshop-{userEmail}";

        var customer = await _maxioClient.EnsureCustomerAsync(firstName, lastName, userEmail, customerReference);

        var subscription = await _maxioClient.CreateSubscriptionAsync(request.ProductHandle, customerReference);

        response.Subscription = MapToDto(subscription);
        return Results.Ok(response);
    }

    private static SubscriptionDto MapToDto(Maxio.MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PriceInDollars = subscription.ProductPriceInCents / 100m,
            Currency = subscription.Currency,
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            CreatedAt = subscription.CreatedAt,
            ProductName = subscription.Product?.Name,
            ProductHandle = subscription.Product?.Handle,
            ProductId = subscription.Product?.Id
        };
    }
}
