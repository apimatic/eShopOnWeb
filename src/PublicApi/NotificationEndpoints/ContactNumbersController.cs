using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public ContactNumbersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Register(
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var contact = await _service.RegisterContactNumberAsync(BuyerId(), request.PhoneNumber, cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}", new
        {
            contactNumberId = contact.Id,
            number = contact.CanonicalNumber
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var contacts = await _service.GetContactNumbersAsync(BuyerId(), cancellationToken);
        return Ok(contacts.Select(x => new
        {
            contactNumberId = x.Id,
            number = x.CanonicalNumber,
            createdAt = x.CreatedAt
        }));
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var deleted = await _service.DeleteContactNumberAsync(BuyerId(), contactNumberId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class RegisterContactNumberRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string PhoneNumber { get; init; } = string.Empty;
}
