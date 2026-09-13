using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioApiClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, MaxioApiClient maxioClient) =>
            {
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioApiClient maxioClient)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        // Use the email/username as a stable reference for idempotent customer creation
        var customerRef = $"eshop-{userName}";
        var existingCustomer = await maxioClient.FindCustomerByReferenceAsync(customerRef);

        int customerId;
        if (existingCustomer?.Customer != null)
        {
            customerId = existingCustomer.Customer.Id;
        }
        else
        {
            var nameParts = userName.Split('@')[0];
            var createResult = await maxioClient.CreateCustomerAsync(new CreateCustomerRequest
            {
                FirstName = nameParts,
                LastName = "User",
                Email = userName,
                Reference = customerRef
            });

            if (createResult?.Customer == null)
            {
                return Results.StatusCode(502);
            }
            customerId = createResult.Customer.Id;
        }

        var subscriptionResult = await maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            ProductHandle = request.ProductHandle,
            CustomerId = customerId,
            CreditCardAttributes = new MaxioCreditCardAttributes
            {
                PaymentType = "credit_card",
                FullNumber = "1",
                ExpirationMonth = "12",
                ExpirationYear = "2030"
            }
        });

        if (subscriptionResult?.Subscription == null)
        {
            return Results.StatusCode(502);
        }

        response.Subscription = subscriptionResult.Subscription;
        return Results.Created("/api/my-subscriptions", response);
    }
}
