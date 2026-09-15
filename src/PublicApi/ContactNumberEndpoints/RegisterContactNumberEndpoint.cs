using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here, and what is stored is the provider's canonical form of it.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ClaimsPrincipal>
{
    private readonly IPhoneNumberValidator _validator;
    private readonly IRepository<ContactNumber> _contactNumbers;

    public RegisterContactNumberEndpoint(IPhoneNumberValidator validator, IRepository<ContactNumber> contactNumbers)
    {
        _validator = validator;
        _contactNumbers = contactNumbers;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<RegisterContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new RegisterContactNumberResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        var validation = await _validator.ValidateAsync(request.PhoneNumber);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            // Rejected up front — not a usable destination — rather than at the moment a message fails.
            return Results.BadRequest("The phone number is not a usable SMS destination.");
        }

        // Store the provider's canonical form. Avoid duplicating a number the shopper already has on file.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var already = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (already is not null)
        {
            response.ContactNumberId = already.Id;
            response.PhoneNumber = already.PhoneNumber;
            return Results.Ok(response);
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber!);
        await _contactNumbers.AddAsync(contactNumber);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
