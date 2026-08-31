using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Nothing may be sent
/// to the number afterwards.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeleteContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository)
    {
        _contactNumberRepository = contactNumberRepository;
    }

    [HttpDelete("api/contact-numbers/{contactNumberId}")]
    [SwaggerOperation(
        Summary = "Removes a contact number",
        Description = "Removes a contact number owned by the caller",
        OperationId = "contactNumbers.delete",
        Tags = new[] { "ContactNumberEndpoints" })
    ]
    public override async Task<ActionResult> HandleAsync([FromRoute(Name = "contactNumberId")] int request,
        CancellationToken cancellationToken = default)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(request, cancellationToken);
        if (contactNumber == null || contactNumber.OwnerId != User.Identity!.Name)
        {
            return NotFound();
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return NoContent();
    }
}
