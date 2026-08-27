using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

[ApiController]
[Route("api/contact-numbers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioGateway _twilio;
    private readonly IOrderNotificationService _notificationService;
    private readonly TimeProvider _timeProvider;

    public ContactNumbersController(IRepository<ContactNumber> contactNumbers, ITwilioGateway twilio,
        IOrderNotificationService notificationService, TimeProvider timeProvider)
    {
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateContactNumberResponse>> CreateAsync(CreateContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        PhoneNumberValidationResult validation;
        try
        {
            validation = await _twilio.ValidatePhoneNumberAsync(request.MobileNumber, cancellationToken);
        }
        catch
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The phone-number validation provider is unavailable.");
        }

        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            ModelState.AddModelError(nameof(request.MobileNumber), "The provider does not consider this a valid destination.");
            return ValidationProblem(ModelState);
        }

        var buyerId = User.Identity!.Name!;
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ActiveContactNumberByOwnerAndValueSpec(buyerId, validation.CanonicalNumber), cancellationToken);
        if (existing != null)
        {
            return Conflict(new { message = "That contact number is already registered." });
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalNumber, _timeProvider.GetUtcNow());
        contact = await _contactNumbers.AddAsync(contact, cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}",
            new CreateContactNumberResponse(contact.Id, contact.CanonicalNumber));
    }

    [HttpGet]
    public async Task<ActionResult<ContactNumberResponse[]>> ListAsync(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contacts = await _contactNumbers.ListAsync(new ActiveContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return Ok(contacts.Select(contact =>
            new ContactNumberResponse(contact.Id, contact.CanonicalNumber, contact.CreatedAt)).ToArray());
    }

    [HttpDelete("{contactNumberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var contact = await _contactNumbers.FirstOrDefaultAsync(
            new ActiveContactNumberByOwnerAndIdSpec(buyerId, contactNumberId), cancellationToken);
        if (contact == null)
        {
            return NotFound();
        }

        try
        {
            await _notificationService.CancelPendingFollowUpsForContactAsync(contact.Id, cancellationToken);
        }
        catch (FollowUpCancellationException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider could not confirm cancellation of a scheduled message; the number was not removed.");
        }

        contact.Remove(_timeProvider.GetUtcNow());
        await _contactNumbers.UpdateAsync(contact, cancellationToken);
        return NoContent();
    }
}

public sealed class CreateContactNumberRequest
{
    [Required, MaxLength(64)]
    public string MobileNumber { get; set; } = string.Empty;
}

public sealed record CreateContactNumberResponse(int ContactNumberId, string CanonicalNumber);
public sealed record ContactNumberResponse(int ContactNumberId, string CanonicalNumber, DateTimeOffset CreatedAt);
