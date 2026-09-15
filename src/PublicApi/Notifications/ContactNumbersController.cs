using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly IPhoneNumberValidator _validator;
    private readonly IOrderNotificationService _notifications;

    public ContactNumbersController(CatalogContext db, IPhoneNumberValidator validator,
        IOrderNotificationService notifications)
    {
        _db = db;
        _validator = validator;
        _notifications = notifications;
    }

    [HttpPost]
    [ProducesResponseType<ContactNumberCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ContactNumberCreatedResponse>> RegisterAsync(
        RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        PhoneNumberValidation validation;
        try
        {
            validation = await _validator.ValidateAsync(request.Number, cancellationToken);
        }
        catch (ProviderRequestException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Phone-number validation is temporarily unavailable.");
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Phone-number validation is temporarily unavailable.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            return BadRequest(new ValidationProblemDetails(new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["number"] = validation.ValidationErrors.Count == 0
                    ? new[] { "The messaging provider does not consider this a valid destination." }
                    : validation.ValidationErrors.ToArray()
            }));
        }

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.BuyerId == buyerId && x.CanonicalNumber == validation.CanonicalNumber && x.IsActive,
            cancellationToken);
        if (existing is not null)
            return Conflict(new ProblemDetails { Title = "That contact number is already registered." });

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber);
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}", new ContactNumberCreatedResponse(contact.Id));
    }

    [HttpGet]
    public async Task<ActionResult<System.Collections.Generic.IReadOnlyList<ContactNumberDto>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contacts = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberDto(x.Id, x.CanonicalNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return Ok(contacts);
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> RemoveAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(x =>
            x.Id == contactNumberId && x.BuyerId == buyerId && x.IsActive, cancellationToken);
        if (contact is null) return NotFound();

        contact.Remove();
        await _db.SaveChangesAsync(cancellationToken);
        await _notifications.CancelScheduledAsync(null, contact.Id, cancellationToken);
        return NoContent();
    }
}
