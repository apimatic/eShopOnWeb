using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is
/// validated with the messaging provider first, and the provider's canonical
/// form is what gets stored.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user,
             ITextMessageProvider messageProvider, IRepository<ContactNumber> contactNumberRepository,
             CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, messageProvider, contactNumberRepository, cancellationToken);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user,
        ITextMessageProvider messageProvider, IRepository<ContactNumber> contactNumberRepository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "phoneNumber is required." });
        }

        PhoneNumberValidation validation;
        try
        {
            validation = await messageProvider.ValidatePhoneNumberAsync(request.PhoneNumber, cancellationToken);
        }
        catch (TextMessageProviderException)
        {
            // Never log the number or the provider's detail text.
            return Results.Problem("The phone number could not be validated with the provider.", statusCode: StatusCodes.Status502BadGateway);
        }

        if (!validation.IsValid || validation.CanonicalNumber == null)
        {
            return Results.BadRequest(new { error = "The provider does not consider this a usable destination.", reason = validation.ValidationError });
        }

        var contactNumber = new ContactNumber(user.Identity!.Name!, validation.CanonicalNumber);
        contactNumber = await contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
