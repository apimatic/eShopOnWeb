using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var userName = user.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var result = await subscriptionService.CreateSubscriptionAsync(userName, request.ProductHandle);
                    var response = new CreateSubscriptionResponse(request.CorrelationId())
                    {
                        SubscriptionId = result.SubscriptionId,
                        Status = result.Status,
                        NextBillingDate = result.NextBillingDate,
                        ProductName = result.ProductName
                    };

                    return Results.Created($"api/subscriptions/{result.SubscriptionId}", response);
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

    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Enrolls the authenticated user in a subscription plan",
        OperationId = "subscription.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        try
        {
            var result = await _subscriptionService.CreateSubscriptionAsync("", request.ProductHandle);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                SubscriptionId = result.SubscriptionId,
                Status = result.Status,
                NextBillingDate = result.NextBillingDate,
                ProductName = result.ProductName
            };

            return Results.Created($"api/subscriptions/{result.SubscriptionId}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public int SubscriptionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public string ProductName { get; set; } = string.Empty;
}
