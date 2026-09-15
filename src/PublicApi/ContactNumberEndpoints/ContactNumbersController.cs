using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ContactNumbersController : ControllerBase
{
    private readonly CatalogContext _context;
    private readonly IMessageProvider _provider;
    private readonly OrderNotificationService _notifications;

    public ContactNumbersController(CatalogContext context, IMessageProvider provider,
        OrderNotificationService notifications)
    {
        _context = context;
        _provider = provider;
        _notifications = notifications;
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return BadRequest(new { error = "phoneNumber is required." });

        PhoneNumberValidation validation;
        try
        {
            validation = await _provider.ValidatePhoneNumberAsync(request.PhoneNumber,
                request.CountryCode, cancellationToken);
        }
        catch (MessageProviderException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Phone-number validation is temporarily unavailable." });
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalPhoneNumber))
            return BadRequest(new { error = "The messaging provider does not consider this a valid destination.",
                validationErrors = validation.ValidationErrors });

        var buyerId = User.Identity!.Name!;
        var duplicate = await _context.ContactNumbers.AnyAsync(x =>
            x.BuyerId == buyerId && x.PhoneNumber == validation.CanonicalPhoneNumber,
            cancellationToken);
        if (duplicate) return Conflict(new { error = "That contact number is already registered." });

        var contact = new ContactNumber(buyerId, validation.CanonicalPhoneNumber);
        _context.ContactNumbers.Add(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}",
            new { contactNumberId = contact.Id, phoneNumber = contact.PhoneNumber });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var numbers = await _context.ContactNumbers
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new { contactNumberId = x.Id, phoneNumber = x.PhoneNumber })
            .ToListAsync(cancellationToken);
        return Ok(numbers);
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contact = await _context.ContactNumbers.SingleOrDefaultAsync(x =>
            x.Id == contactNumberId && x.BuyerId == buyerId, cancellationToken);
        if (contact is null) return NotFound();

        if (!await _notifications.CancelOutstandingFollowUpsForContactAsync(contact.Id, cancellationToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The number was not removed because a scheduled message could not yet be cancelled. Retry the request." });

        _context.ContactNumbers.Remove(contact);
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}
