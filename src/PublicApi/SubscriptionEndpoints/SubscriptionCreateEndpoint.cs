using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (HttpContext httpContext, IMaxioService maxioService) =>
            {
                var request = new CreateSubscriptionRequest();

                if (httpContext.Request.ContentLength > 0)
                {
                    using var reader = new System.IO.StreamReader(httpContext.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    var json = JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("ProductHandle", out var handle))
                    {
                        request.ProductHandle = handle.GetString() ?? string.Empty;
                    }
                }

                var email = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                var firstName = httpContext.User.FindFirstValue("given_name") ?? email.Split('@')[0];
                var lastName = httpContext.User.FindFirstValue("family_name") ?? "";
                request.Email = email;
                request.FirstName = firstName;
                request.LastName = lastName;

                return await HandleAsync(request, maxioService);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            response.ErrorMessage = "User email not found in token.";
            return Results.Unauthorized();
        }

        var result = await maxioService.CreateSubscriptionAsync(request.Email, request.FirstName, request.LastName, request.ProductHandle);

        response.Success = result.Success;
        response.ErrorMessage = result.ErrorMessage;

        if (result.Subscription != null)
        {
            response.Subscription = new SubscriptionDto
            {
                Id = result.Subscription.Id,
                State = result.Subscription.State,
                PlanName = result.Subscription.PlanName,
                PlanHandle = result.Subscription.PlanHandle,
                Price = result.Subscription.Price,
                CurrentPeriodEndsAt = result.Subscription.CurrentPeriodEndsAt,
                NextBillingDate = result.Subscription.NextBillingDate,
                CreatedAt = result.Subscription.CreatedAt
            };
        }

        return result.Success ? Results.Ok(response) : Results.BadRequest(response);
    }
}
