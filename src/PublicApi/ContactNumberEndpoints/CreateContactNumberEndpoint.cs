using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// with the provider up front and stored in the provider's canonical form.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<CreateContactNumberRequest>
    .WithActionResult<CreateContactNumberResponse>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberValidator _phoneNumberValidator;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
    }

    [HttpPost("api/contact-numbers")]
    [SwaggerOperation(
        Summary = "Registers a mobile contact number",
        Description = "Validates the number with the provider and stores its canonical form",
        OperationId = "contactNumbers.create",
        Tags = new[] { "ContactNumberEndpoints" })
    ]
    public override async Task<ActionResult<CreateContactNumberResponse>> HandleAsync(CreateContactNumberRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());

        string canonicalNumber;
        try
        {
            var validated = await _phoneNumberValidator.ValidateAndNormalizeAsync(request.PhoneNumber, cancellationToken);
            if (validated == null)
            {
                return BadRequest("The phone number is not a usable destination.");
            }
            canonicalNumber = validated;
        }
        catch (SmsProviderException)
        {
            return StatusCode(502, "The phone number validation provider could not be reached.");
        }

        var ownerId = User.Identity!.Name!;
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        if (existing.Any(c => c.PhoneNumber == canonicalNumber))
        {
            return Conflict("This number is already registered.");
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(ownerId, canonicalNumber), cancellationToken);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return response;
    }
}
