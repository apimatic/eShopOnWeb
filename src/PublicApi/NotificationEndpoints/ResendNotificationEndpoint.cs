using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Re-sends a message that did not reach the shopper (operator action). The
/// caller-supplied idempotency key makes repeats of the same request safe:
/// they return the message the first request produced without sending again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        var response = new ResendNotificationResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        try
        {
            var result = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);

            response.NotificationId = result.Notification.Id;
            response.OrderId = result.Notification.OrderId;
            response.Status = result.Notification.ProviderStatus;
            response.ProviderMessageSid = result.Notification.ProviderMessageSid;
            response.WasDuplicate = result.WasDuplicate;
            return result.WasDuplicate ? Results.Ok(response) : Results.Created($"api/notifications/{result.Notification.Id}", response);
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.Conflict(ex.Message);
        }
    }
}
