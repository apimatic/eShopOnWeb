using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content. The text is redacted at
/// the provider as well, while the record that a message was sent — and its
/// outcome — survives.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeleteNotificationContentEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<DeleteNotificationContentResponse>
{
    private readonly IOrderNotificationService _notificationService;

    public DeleteNotificationContentEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpDelete("api/notifications/{notificationId}/content")]
    [SwaggerOperation(
        Summary = "Disposes of a notification's content",
        Description = "Redacts the message text locally and at the provider",
        OperationId = "notifications.deleteContent",
        Tags = new[] { "NotificationEndpoints" })
    ]
    public override async Task<ActionResult<DeleteNotificationContentResponse>> HandleAsync(
        [FromRoute(Name = "notificationId")] int request, CancellationToken cancellationToken = default)
    {
        try
        {
            var notification = await _notificationService.RedactContentAsync(request, cancellationToken);
            return new DeleteNotificationContentResponse
            {
                NotificationId = notification.Id,
                ContentRedacted = notification.ContentRedacted,
                Status = notification.Status
            };
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (SmsProviderException)
        {
            return StatusCode(502, "The messaging provider could not redact the message content.");
        }
    }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
    public string Status { get; set; } = string.Empty;
}
