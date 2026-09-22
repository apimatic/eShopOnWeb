using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates the number up
/// front; one it does not consider a usable destination is rejected here, and the stored value is
/// the provider's own canonical form.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class RegisterContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<RegisterContactNumberRequest>
    .WithActionResult<RegisterContactNumberResponse>
{
    private readonly IContactNumberService _contactNumberService;

    public RegisterContactNumberEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    [HttpPost("api/contact-numbers")]
    [SwaggerOperation(
        Summary = "Registers a mobile number for the signed-in shopper",
        Description = "Registers a mobile number for the signed-in shopper",
        OperationId = "contactNumbers.register",
        Tags = new[] { "ContactNumberEndpoints" })]
    public override async Task<ActionResult<RegisterContactNumberResponse>> HandleAsync(
        [FromBody] RegisterContactNumberRequest request,
        CancellationToken cancellationToken = default)
    {
        var ownerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Unauthorized();
        }

        var result = await _contactNumberService.RegisterAsync(ownerId, request.PhoneNumber, cancellationToken);
        if (!result.Succeeded || result.ContactNumber is null)
        {
            return BadRequest("The provided number is not a usable destination.");
        }

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = result.ContactNumber.Id,
            PhoneNumber = result.ContactNumber.PhoneNumber
        };
        return Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
