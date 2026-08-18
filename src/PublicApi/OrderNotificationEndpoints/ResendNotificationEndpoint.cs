using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Idempotent on the caller-supplied
/// key — a repeat under the same key sends nothing and returns the earlier result; a fresh key sends anew.
/// Returns the identifier of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
                await HandleAsync(notificationId, request, service))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        try
        {
            var resend = await service.ResendAsync(notificationId, request.IdempotencyKey);
            if (resend is null)
            {
                return Results.NotFound();
            }

            var response = new ResendNotificationResponse
            {
                NotificationId = resend.Id,
                Status = resend.ProviderStatus ?? "pending",
                MessageSid = resend.MessageSid
            };
            return Results.Ok(response);
        }
        catch (SmsGatewayException ex)
        {
            return GatewayErrorMapper.Map(ex);
        }
    }
}
