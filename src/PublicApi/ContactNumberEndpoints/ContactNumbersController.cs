using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/contact-numbers")]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly IOrderNotificationService _service;

    public ContactNumbersController(IOrderNotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType<ContactNumberCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ContactNumberCreatedResponse>> Register(
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var contact = await _service.RegisterContactNumberAsync(BuyerId(), request.Number, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new ContactNumberCreatedResponse(
            contact.ContactNumberId,
            contact.Number,
            contact.CreatedAt));
    }

    [HttpGet]
    public Task<IReadOnlyList<ContactNumberView>> List(CancellationToken cancellationToken) =>
        _service.GetContactNumbersAsync(BuyerId(), cancellationToken);

    [HttpDelete("{contactNumberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        await _service.RemoveContactNumberAsync(BuyerId(), contactNumberId, cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)!;
}

public sealed record RegisterContactNumberRequest(string Number);
public sealed record ContactNumberCreatedResponse(int ContactNumberId, string Number, System.DateTimeOffset CreatedAt);
