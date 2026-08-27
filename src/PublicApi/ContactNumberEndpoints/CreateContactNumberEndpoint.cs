using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the registered number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// with the provider up front and stored in the provider's canonical form.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<CreateContactNumberRequest>
    .WithActionResult<CreateContactNumberResponse>
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IPhoneNumberValidator _phoneNumberValidator;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumbers, IPhoneNumberValidator phoneNumberValidator)
    {
        _contactNumbers = contactNumbers;
        _phoneNumberValidator = phoneNumberValidator;
    }

    [HttpPost("api/contact-numbers")]
    [SwaggerOperation(Summary = "Registers a contact number for the caller", Tags = new[] { "ContactNumberEndpoints" })]
    public override async Task<ActionResult<CreateContactNumberResponse>> HandleAsync(
        [FromBody] CreateContactNumberRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (buyerId is null) return Unauthorized();

        var validation = await _phoneNumberValidator.ValidateAsync(request.PhoneNumber, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = validation.Error ?? "The phone number is not a usable destination." });
        }

        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (existing.Any(c => c.PhoneNumber == validation.CanonicalNumber))
        {
            return Conflict(new { error = "This number is already registered." });
        }

        var contactNumber = await _contactNumbers.AddAsync(
            new ContactNumber(buyerId, validation.CanonicalNumber!), cancellationToken);

        return new CreateContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
    }
}
