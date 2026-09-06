using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioApiClient maxioClient) =>
            {
                if (string.IsNullOrEmpty(request.ProductHandle))
                {
                    return Results.BadRequest(new { error = "ProductHandle is required" });
                }

                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
                var firstName = httpContext.User.FindFirst("FirstName")?.Value ?? "Customer";
                var lastName = httpContext.User.FindFirst("LastName")?.Value ?? "Customer";

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, maxioClient, userId, email, firstName, lastName);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiClient maxioClient)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiClient maxioClient,
        string userId, string email, string firstName, string lastName)
    {
        try
        {
            var customer = await maxioClient.GetOrCreateCustomer(userId, email, firstName, lastName);
            if (customer == null)
            {
                return Results.BadRequest(new { error = "Failed to create or retrieve customer" });
            }

            var subscription = await maxioClient.CreateSubscription(userId, request.ProductHandle ?? "");
            if (subscription == null)
            {
                return Results.BadRequest(new { error = "Failed to create subscription" });
            }

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductName = subscription.ProductName,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
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
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
