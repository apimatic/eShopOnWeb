using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscriptionCreateRequest request, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new SubscriptionCreateResponse(request.CorrelationId());

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(
                request.Email!,
                request.FirstName!,
                request.LastName!,
                request.ProductHandle!,
                request.Reference,
                default);

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductName = subscription.ProductName,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt
            };

            return Results.Created("api/my-subscriptions", response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            string detail = "Failed to create subscription.";
            if (ex.Error.TryGetErrorListResponse1(out var errorList) && errorList.Errors != null)
            {
                detail += $" Validation: {string.Join(", ", errorList.Errors)}";
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                detail += $" {raw.ReadAsString()}";
            }
            return Results.Problem(detail: detail, statusCode: 422);
        }
        catch (SdkException<RawError> ex)
        {
            var statusCode = (int)ex.Error.StatusCode;
            if (statusCode == 404)
            {
                return Results.NotFound(new { error = "Customer or product not found." });
            }
            return Results.Problem(
                detail: $"Maxio API error ({statusCode}): {ex.Error.ReadAsString()}",
                statusCode: 502);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: $"An error occurred: {ex.Message}",
                statusCode: 500);
        }
    }
}
