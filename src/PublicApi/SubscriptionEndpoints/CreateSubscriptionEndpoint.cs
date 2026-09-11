using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioClient maxioClient, HttpContext httpContext) =>
            {
                var response = new CreateSubscriptionResponse(request.CorrelationId());

                var userId = httpContext.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(userId))
                    return Results.Unauthorized();

                var email = userId;

                var existingCustomer = await maxioClient.FindCustomerByReferenceAsync(userId);

                MaxioCustomerDto customer;
                if (existingCustomer != null)
                {
                    customer = existingCustomer;
                }
                else
                {
                    var displayName = userId.Contains('@') ? userId.Split('@')[0] : userId;
                    customer = await maxioClient.CreateCustomerAsync(new MaxioCustomerCreateAttributes
                    {
                        FirstName = displayName,
                        LastName = "User",
                        Email = email,
                        Reference = userId
                    });
                }

                MaxioSubscriptionDto subscription;
                try
                {
                    subscription = await maxioClient.CreateSubscriptionAsync(
                        request.ProductHandle,
                        userId,
                        new MaxioCustomerCreateAttributes
                        {
                            FirstName = customer.FirstName,
                            LastName = customer.LastName,
                            Email = customer.Email,
                            Reference = userId
                        },
                        customerAlreadyExists: existingCustomer != null);
                }
                catch (MaxioApiException ex)
                {
                    var errorResponse = new CreateSubscriptionResponse(request.CorrelationId())
                    {
                        Message = $"Subscription creation failed: {string.Join("; ", ex.Errors)}"
                    };
                    return Results.UnprocessableEntity(errorResponse);
                }

                response.SubscriptionId = subscription.Id;
                response.State = subscription.State;
                response.PlanName = subscription.Product?.Name ?? "Unknown";
                response.Price = subscription.ProductPriceInCents / 100m;
                response.NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;
                response.ActivatedAt = subscription.ActivatedAt;
                response.Message = $"Successfully subscribed to {response.PlanName} (${response.Price:F2}/{subscription.Product?.IntervalUnit ?? "month"}).";

                return Results.Created(
                    $"api/subscriptions/{subscription.Id}",
                    response);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(401)
            .Produces<CreateSubscriptionResponse>(422)
            .WithTags("SubscriptionEndpoints");
    }
}
