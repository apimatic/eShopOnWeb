using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content. The text is erased at the provider as well
/// as locally; the fact that a message was sent, and what became of it, survives.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeleteNotificationContentEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult
{
    private readonly INotificationManagementService _notificationManagementService;

    public DeleteNotificationContentEndpoint(INotificationManagementService notificationManagementService)
    {
        _notificationManagementService = notificationManagementService;
    }

    [HttpDelete("api/notifications/{notificationId}/content")]
    [SwaggerOperation(
        Summary = "Disposes of a notification's content (operator)",
        Description = "Erases the message text at the provider and locally; the record and its outcome survive",
        OperationId = "notifications.deleteContent",
        Tags = new[] { "NotificationEndpoints" })
    ]
    public override async Task<ActionResult> HandleAsync(int notificationId,
        CancellationToken cancellationToken = default)
    {
        await _notificationManagementService.DisposeContentAsync(notificationId, cancellationToken);
        return NoContent();
    }
}
