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
/// Operator action on a shopper's behalf: disposes of a message's content so its text is no longer
/// retrievable from the provider either — not merely hidden here — while the fact that a message was
/// sent, and what became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public DeleteNotificationContentEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http) => await HandleAsync(notificationId, http))
            .Produces<DeleteNotificationContentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext http)
    {
        try
        {
            var notification = await _orderNotificationService.RedactContentAsync(notificationId, http.RequestAborted);
            if (notification is null)
                return Results.NotFound();

            return Results.Ok(new DeleteNotificationContentResponse
            {
                NotificationId = notification.Id,
                ContentRedacted = notification.ContentRedacted,
                Status = notification.Status
            });
        }
        catch (SmsGatewayException)
        {
            // The content could not be disposed of at the provider — do not report success.
            return Results.Problem("The messaging provider could not dispose of the message content.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Status { get; set; }
}
