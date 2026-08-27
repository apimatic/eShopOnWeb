using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider; what is stored is the provider's canonical form, not what was typed.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<CreateContactNumberRequest>
    .WithActionResult<CreateContactNumberResponse>
{
    private readonly IContactNumberService _contactNumberService;

    public CreateContactNumberEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    [HttpPost("api/contact-numbers")]
    [SwaggerOperation(
        Summary = "Registers a contact number for the caller",
        Description = "Validates the number with the messaging provider and stores its canonical form",
        OperationId = "contact-numbers.create",
        Tags = new[] { "ContactNumberEndpoints" })
    ]
    public override async Task<ActionResult<CreateContactNumberResponse>> HandleAsync(CreateContactNumberRequest request,
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity!.Name!;
        var contactNumber = await _contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, cancellationToken);

        return new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
    }
}
