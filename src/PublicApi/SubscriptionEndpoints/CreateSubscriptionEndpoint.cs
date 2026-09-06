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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, MaxioSubscriptionService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, maxioService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService maxioService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService maxioService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                userId = "anonymous";
            }

            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? userId;
            if (!email.Contains("@"))
            {
                email = userId + "@eshop.local";
            }
            var firstName = httpContext.User.FindFirst("given_name")?.Value ?? "";
            var lastName = httpContext.User.FindFirst("family_name")?.Value ?? "";

            var customer = await maxioService.GetOrCreateCustomerAsync(userId, email, firstName, lastName);
            var subscription = await maxioService.CreateSubscriptionAsync(customer.Id, request.ProductHandle);

            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt;
            response.CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt;
            response.NextAssessmentAt = subscription.NextAssessmentAt;
            response.ProductHandle = subscription.ProductHandle;
            response.ProductName = subscription.ProductName;
            response.Success = true;
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }

        return Results.Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
