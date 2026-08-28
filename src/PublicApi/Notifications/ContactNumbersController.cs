using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController(OrderNotificationApplicationService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ContactNumberCreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ContactNumberCreatedResponse>> Register(
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var id = await service.RegisterContactNumberAsync(ShopperId(), request.MobileNumber, cancellationToken);
        return Created($"/api/contact-numbers/{id}", new ContactNumberCreatedResponse(id));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ContactNumberResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContactNumberResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await service.GetContactNumbersAsync(ShopperId(), cancellationToken));

    [HttpDelete("{contactNumberId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid contactNumberId, CancellationToken cancellationToken) =>
        await service.DeleteContactNumberAsync(ShopperId(), contactNumberId, cancellationToken)
            ? NoContent()
            : NotFound();

    private string ShopperId() => User.Identity?.Name
        ?? throw new ApiRequestException(StatusCodes.Status401Unauthorized, "Authentication is required.");
}
