using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, subscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        await Task.Delay(10);
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = httpContext.User.FindFirst("given_name")?.Value ?? "User";
            var lastName = httpContext.User.FindFirst("family_name")?.Value ?? "";

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(email))
            {
                return Results.BadRequest(new { error = "Email not found in token" });
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new { error = "ProductHandle is required" });
            }

            var customer = await subscriptionService.GetOrCreateCustomerAsync(email, firstName, lastName, userId);
            if (customer?.Customer?.Id == null)
            {
                return Results.BadRequest(new { error = "Failed to create or retrieve customer from Maxio" });
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(customer.Customer.Id, request.ProductHandle);
            if (subscription?.Subscription?.Id == null)
            {
                return Results.BadRequest(new { error = "Failed to create subscription in Maxio" });
            }

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = new SubscriptionDto
                {
                    Id = subscription.Subscription.Id,
                    CustomerId = subscription.Subscription.CustomerId,
                    ProductId = subscription.Subscription.ProductId,
                    ProductHandle = subscription.Subscription.ProductHandle,
                    State = subscription.Subscription.State,
                    CurrentPeriodEndsAt = subscription.Subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.Subscription.NextAssessmentAt,
                    ActivatedAt = subscription.Subscription.ActivatedAt,
                    CreatedAt = subscription.Subscription.CreatedAt
                }
            };

            return Results.Created($"api/subscriptions/{subscription.Subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse()
    {
    }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
