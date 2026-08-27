using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// with the provider first; the provider's canonical form is what gets stored.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user,
                IRepository<ContactNumber> contactNumberRepository, IPhoneNumberLookup phoneNumberLookup,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, contactNumberRepository, phoneNumberLookup, cancellationToken);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user,
        IRepository<ContactNumber> contactNumberRepository, IPhoneNumberLookup phoneNumberLookup, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var response = new CreateContactNumberResponse(request.CorrelationId());

        var lookup = await phoneNumberLookup.LookupAsync(request.PhoneNumber, cancellationToken);
        if (!lookup.IsValid || lookup.CanonicalNumber == null)
        {
            var reason = lookup.ValidationErrors.Count > 0 ? string.Join(", ", lookup.ValidationErrors) : "not a valid, assignable number";
            throw new InvalidPhoneNumberException(reason);
        }

        var existing = (await contactNumberRepository.ListAsync(cancellationToken))
            .FirstOrDefault(c => c.BuyerId == buyerId && c.PhoneNumber == lookup.CanonicalNumber);
        if (existing != null)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        contactNumber = await contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
