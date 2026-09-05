using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, subscriptionService, httpContext);
            })
           .Produces<CreateSubscriptionResponse>()
           .WithName("CreateSubscription")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ??
                           httpContext.User.FindFirst("email")?.Value ??
                           httpContext.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                return Results.BadRequest("User email not found in token claims");
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(userEmail, request.ProductHandle);
            response.Subscription = subscription;
            return Results.Created($"api/subscriptions/{subscription?.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
