using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, subscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = httpContext.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
            var lastName = httpContext.User.FindFirst(ClaimTypes.Surname)?.Value ?? "Account";

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(email))
            {
                return Results.BadRequest("Email claim not found in token");
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                userId, email, firstName, lastName, request.PlanHandle);

            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.ProductName = subscription.ProductName;
            response.ProductHandle = subscription.ProductHandle;
            response.PricePerMonth = subscription.PricePerMonth;
            response.NextBillingDate = subscription.NextAssessmentAt;

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
