using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public SubscribeEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
           .Produces<SubscribeResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        throw new NotSupportedException("Use HandleAsync with HttpContext overload.");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? throw new UnauthorizedAccessException("No user identity found in token.");

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(userId, request.ProductHandle);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = subscription
            };

            return Results.Created($"/api/my-subscriptions", response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            string detail = "Subscription creation rejected by billing service.";
            if (ex.Error.TryGetErrorListResponse1(out var errList))
            {
                detail = $"Validation error: {errList}";
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                detail = $"Maxio API error: {(int)raw.StatusCode} - {raw.ReadAsString()}";
            }

            return Results.Problem(
                detail: detail,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Subscription creation failed");
        }
        catch (SdkException<RawError> ex)
        {
            return Results.Problem(
                detail: $"Maxio API error: {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}",
                statusCode: StatusCodes.Status502BadGateway,
                title: "Billing service error");
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Subscription creation failed");
        }
    }
}
