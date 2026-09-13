using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a new subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, MaxioService maxioService) =>
            {
                return await HandleAsync(request, maxioService);
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioService maxioService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
        {
            response.ErrorMessage = "Unable to determine user identity.";
            return Results.Unauthorized();
        }

        var email = httpContext!.User.FindFirstValue(ClaimTypes.Email) ?? $"{userId}@eshop.local";
        var firstName = httpContext.User.FindFirstValue(ClaimTypes.GivenName) ?? "eShop";
        var lastName = httpContext.User.FindFirstValue(ClaimTypes.Surname) ?? "User";

        try
        {
            var customerId = await maxioService.EnsureCustomerAsync(
                email, firstName, lastName, userId, default);

            var subscriptionRef = $"{userId}:{request.ProductHandle}";
            var subscriptionResponse = await maxioService.CreateSubscriptionAsync(
                customerId, request.ProductHandle, subscriptionRef, default);

            var subscription = subscriptionResponse.Subscription;
            if (subscription == null)
            {
                response.ErrorMessage = "Subscription creation returned no data.";
                return Results.StatusCode(502);
            }

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id ?? 0,
                State = subscription.State?.Value ?? "unknown",
                ProductHandle = subscription.Product?.Handle ?? request.ProductHandle,
                ProductName = subscription.Product?.Name ?? request.ProductHandle,
                ProductPriceInCents = subscription.ProductPriceInCents ?? 0,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            string detail = "Subscription creation failed.";
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                detail = string.Join("; ", errorList.Errors);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                detail = raw.ReadAsString();
            }
            _logger.LogError(ex, "Maxio subscription creation failed for user {UserId}: {Detail}", userId, detail);
            response.ErrorMessage = detail;
            return Results.BadRequest(response);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            string detail = "Customer creation failed.";
            if (ex.Error.TryGetCustomerErrorResponse1(out var custError))
            {
                var errors = custError.Errors;
                var allErrors = new System.Collections.Generic.List<string>();
                if (errors?.PerPage != null) allErrors.AddRange(errors.PerPage);
                if (errors?.PricePoint != null) allErrors.AddRange(errors.PricePoint);
                if (allErrors.Count > 0)
                    detail = string.Join("; ", allErrors);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                detail = raw.ReadAsString();
            }
            _logger.LogError(ex, "Maxio customer creation failed for user {UserId}: {Detail}", userId, detail);
            response.ErrorMessage = detail;
            return Results.BadRequest(response);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error for user {UserId}: {Status}", userId, ex.Error.StatusCode);
            response.ErrorMessage = $"Billing service error: {(int)ex.Error.StatusCode}";
            return Results.StatusCode(502);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription for user {UserId}", userId);
            response.ErrorMessage = "An unexpected error occurred.";
            return Results.StatusCode(500);
        }
    }
}
