using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly IOrderNotificationService _service;
    public ContactNumbersController(IOrderNotificationService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(typeof(CreateContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateContactNumberResponse>> Create(RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var contact = await _service.RegisterContactNumberAsync(BuyerId, request.PhoneNumber,
            request.CountryCode, cancellationToken);
        return Created($"/api/contact-numbers/{contact.ContactNumberId}",
            new CreateContactNumberResponse(contact.ContactNumberId, contact.PhoneNumber, contact.CreatedAt));
    }

    [HttpGet]
    public Task<IReadOnlyList<ContactNumberResult>> List(CancellationToken cancellationToken) =>
        _service.GetContactNumbersAsync(BuyerId, cancellationToken);

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken) =>
        await _service.DeleteContactNumberAsync(BuyerId, contactNumberId, cancellationToken)
            ? NoContent()
            : NotFound();

    private string BuyerId => User.Identity?.Name ?? string.Empty;
}

public sealed record RegisterContactNumberRequest(string PhoneNumber, string? CountryCode = null);
public sealed record CreateContactNumberResponse(int ContactNumberId, string PhoneNumber,
    System.DateTimeOffset CreatedAt);
