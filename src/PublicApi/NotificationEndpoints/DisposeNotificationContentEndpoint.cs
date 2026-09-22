using System.Net;
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
/// Operator action: dispose of a message's content. Afterwards the text is no longer retrievable
/// from the provider either, while the fact a message was sent and its outcome survive. Restricted
/// to administrators.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DisposeNotificationContentEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult
{
    private readonly IOrderNotificationService _orderNotificationService;

    public DisposeNotificationContentEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpDelete("api/notifications/{notificationId}/content")]
    [SwaggerOperation(
        Summary = "Disposes of a message's content (operator)",
        Description = "Removes the message text at the provider and locally; the record and outcome survive",
        OperationId = "notifications.disposeContent",
        Tags = new[] { "NotificationEndpoints" })]
    public override async Task<ActionResult> HandleAsync([FromRoute] int notificationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var disposed = await _orderNotificationService.DisposeContentAsync(notificationId, cancellationToken);
            return disposed ? NoContent() : NotFound();
        }
        catch (SmsProviderException)
        {
            // Do not claim success: the provider-side text was not disposed of.
            return StatusCode((int)HttpStatusCode.BadGateway, "The provider could not dispose of the message content. Please retry.");
        }
    }
}
