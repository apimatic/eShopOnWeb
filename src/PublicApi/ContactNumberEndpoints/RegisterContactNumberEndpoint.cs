using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here (not when a later message fails), and what gets stored is the
/// provider's own canonical E.164 form, not whatever the caller typed.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IRepository<ContactNumber>>
{
    private readonly IPhoneNumberValidationService _phoneValidation;

    public RegisterContactNumberEndpoint(IPhoneNumberValidationService phoneValidation)
    {
        _phoneValidation = phoneValidation;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.AssignBuyer(buyerId);
                return await HandleAsync(request, repository);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IRepository<ContactNumber> repository)
    {
        var validation = await _phoneValidation.ValidateAsync(request.PhoneNumber);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalE164))
        {
            return Results.BadRequest(new { error = "The phone number is not a usable destination and was not registered." });
        }

        var canonical = validation.CanonicalE164!;

        // Reuse an existing registration of the same canonical number for this shopper.
        var existing = await repository.ListAsync(new ContactNumbersByBuyerSpecification(request.BuyerId));
        var duplicate = existing.FirstOrDefault(cn => cn.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return Results.Ok(new RegisterContactNumberResponse
            {
                ContactNumberId = duplicate.Id,
                PhoneNumber = duplicate.PhoneNumber,
                AlreadyRegistered = true
            });
        }

        var contactNumber = new ContactNumber(request.BuyerId, canonical);
        contactNumber = await repository.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
