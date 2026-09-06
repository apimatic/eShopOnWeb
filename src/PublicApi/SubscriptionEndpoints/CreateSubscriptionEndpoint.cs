using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioApiClient maxioClient, ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                var response = new CreateSubscriptionResponse(request.CorrelationId());

                try
                {
                    var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                    if (string.IsNullOrEmpty(userName))
                    {
                        return Results.Unauthorized();
                    }

                    var user = await userManager.FindByNameAsync(userName);
                    if (user == null)
                    {
                        return Results.NotFound(new { error = "User not found" });
                    }

                    var maxioCustomerMapping = await subscriptionService.GetOrCreateMaxioCustomerAsync(user);
                    if (maxioCustomerMapping == null)
                    {
                        return Results.BadRequest(new { error = "Failed to create Maxio customer" });
                    }

                    var subscription = await maxioClient.CreateSubscriptionAsync(maxioCustomerMapping.MaxioId, request.ProductId);
                    if (subscription == null)
                    {
                        return Results.BadRequest(new { error = "Failed to create subscription" });
                    }

                    response.Subscription = new SubscriptionDto
                    {
                        Id = subscription.Id,
                        State = subscription.State,
                        CustomerId = subscription.CustomerId,
                        ProductId = subscription.Product.Id,
                        ProductName = subscription.Product.Name,
                        ProductHandle = subscription.Product.Handle,
                        ProductPriceInCents = subscription.Product.PriceInCents,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                        NextAssessmentAt = subscription.NextAssessmentAt,
                        CreatedAt = subscription.CreatedAt,
                        UpdatedAt = subscription.UpdatedAt
                    };

                    return Results.Created($"/api/subscriptions/{subscription.Id}", response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("CreateSubscription");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiClient maxioClient)
    {
        throw new NotImplementedException();
    }
}
