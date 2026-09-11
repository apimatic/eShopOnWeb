using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Dto;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _maxioOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioClient maxioClient,
        IOptions<MaxioOptions> maxioOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.Identity?.Name
            ?? string.Empty;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var name = user?.FindFirst(ClaimTypes.Name)?.Value
            ?? user?.FindFirst("unique_name")?.Value
            ?? "Unknown User";
        var email = user?.FindFirst(ClaimTypes.Email)?.Value
            ?? user?.FindFirst("email")?.Value
            ?? (name.Contains('@') ? name : $"{name.ToLower().Replace(" ", ".")}@example.com");

        var parts = name.Split(' ', 2);
        var firstName = parts.Length > 0 ? parts[0] : name;
        var lastName = parts.Length > 1 ? parts[1] : "User";

        try
        {
            var customerReference = $"eshop-{userId}";
            var maxioCustomer = await _maxioClient.EnsureCustomerAsync(customerReference, firstName, lastName, email);

            var subscriptionReference = $"sub-{userId}-{request.ProductHandle}";
            var existingSub = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference);
            if (existingSub != null)
            {
                response.Subscription = MapToDto(existingSub);
                return Results.Ok(response);
            }

            var createRequest = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscriptionBody
                {
                    ProductHandle = request.ProductHandle,
                    CustomerId = maxioCustomer.Id,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var subscription = await _maxioClient.CreateSubscriptionAsync(createRequest);
            response.Subscription = MapToDto(subscription);

            return Results.Created("/api/my-subscriptions", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: 502,
                title: "Maxio API Error");
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                detail: $"HTTP error communicating with Maxio: {ex.Message}",
                statusCode: 502,
                title: "Maxio API Error");
        }
    }

    private static SubscriptionDto MapToDto(MaxioSubscriptionDto sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            Balance = sub.BalanceInCents / 100m,
            TotalRevenue = sub.TotalRevenueInCents / 100m,
            ProductPrice = sub.ProductPriceInCents / 100m,
            ProductPriceDisplay = $"${sub.ProductPriceInCents / 100m:F2}",
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            CancelAtEndOfPeriod = sub.CancelAtEndOfPeriod,
            ProductName = sub.Product?.Name,
            ProductHandle = sub.Product?.Handle,
            CustomerEmail = sub.Customer?.Email,
            Reference = sub.Reference,
            Currency = sub.Currency
        };
    }
}
