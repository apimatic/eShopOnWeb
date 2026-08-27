using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's contact numbers. It can never be used to remove
/// another shopper's number.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeleteContactNumberEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithoutResult
{
    private readonly IRepository<ContactNumber> _contactNumbers;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumbers)
    {
        _contactNumbers = contactNumbers;
    }

    [HttpDelete("api/contact-numbers/{contactNumberId}")]
    [SwaggerOperation(Summary = "Deletes one of the caller's contact numbers", Tags = new[] { "ContactNumberEndpoints" })]
    public override async Task<ActionResult> HandleAsync(
        [FromRoute(Name = "contactNumberId")] int contactNumberId, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (buyerId is null) return Unauthorized();

        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return NotFound();
        }

        await _contactNumbers.DeleteAsync(contactNumber, cancellationToken);
        return NoContent();
    }
}
