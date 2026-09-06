using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null)
                {
                    return Results.Unauthorized();
                }

                var requestWithUser = new CreateSubscriptionRequest
                {
                    ProductHandle = request.ProductHandle,
                    UserId = userIdClaim.Value
                };

                return await HandleAsync(requestWithUser);
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.UserId))
            {
                return Results.Unauthorized();
            }

            var response = await _subscriptionService.CreateSubscriptionAsync(request.UserId, request.ProductHandle);
            return Results.Created($"/api/subscriptions/{response.Id}", response);
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
    public string? UserId { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public decimal PricePerBillingCycle { get; set; }
    public int BillingIntervalDays { get; set; }
    public string? BillingInterval { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
}
