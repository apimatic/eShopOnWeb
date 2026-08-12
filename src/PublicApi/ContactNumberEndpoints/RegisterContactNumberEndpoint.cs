using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    // Top-level identifier of the created resource, so the flow can be driven end to end.
    public int ContactNumberId { get; set; }

    // The provider's canonical form of the number that was stored.
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider
/// up front; a number the provider does not consider a usable destination is rejected here, and the
/// provider's canonical E.164 form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user,
                IPhoneNumberValidator validator, IRepository<ContactNumber> repository,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, validator, repository, cancellationToken);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user,
        IPhoneNumberValidator validator, IRepository<ContactNumber> repository, CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var lookup = await validator.LookupAsync(request.PhoneNumber, cancellationToken);
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            // Rejected here rather than at the moment a message fails to go out.
            return Results.BadRequest(new { message = "The number provided is not a usable SMS destination." });
        }

        var contactNumber = new ContactNumber(buyerId, lookup.PhoneNumber!);
        await repository.AddAsync(contactNumber, cancellationToken);

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
