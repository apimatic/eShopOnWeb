using System;
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
/// Operator: disposes of a message's content. The text is redacted at the provider as well
/// as locally; the record that a message was sent, and its outcome, survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest>
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
            (int notificationId) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId));
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request)
    {
        try
        {
            var notification = await _orderNotificationService.RedactContentAsync(request.NotificationId);
            var response = new DeleteNotificationContentResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                ContentRedacted = notification.ContentRedacted,
                Status = notification.Status
            };
            return Results.Ok(response);
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (SmsProviderException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public class DeleteNotificationContentRequest : BaseRequest
{
    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
    public string Status { get; set; } = string.Empty;
}
