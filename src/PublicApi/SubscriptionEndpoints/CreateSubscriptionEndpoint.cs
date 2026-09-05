using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create Subscription
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService service) =>
            {
                return await HandleAsync(request, service, user);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService service)
    {
        throw new NotImplementedException("Use overload with user parameter");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService service, ClaimsPrincipal user)
    {
        try
        {
            var response = new CreateSubscriptionResponse(request.CorrelationId());

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateSubscriptionAsync(userId, request.ProductHandle);

            if (!result.IsSuccess)
            {
                response.Success = false;
                response.Message = result.Message;
                return Results.BadRequest(response);
            }

            response.Success = true;
            response.Message = result.Message;
            response.Subscription = result.Data;

            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
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

    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SubscriptionDto? Subscription { get; set; }
}
