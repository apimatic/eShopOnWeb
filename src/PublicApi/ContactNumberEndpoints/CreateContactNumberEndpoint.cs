using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
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
    public List<string> ValidationErrors { get; set; } = new();
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, HttpContext>
{
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public CreateContactNumberEndpoint(IPhoneNumberValidator phoneNumberValidator,
        IRepository<ContactNumber> contactNumberRepository)
    {
        _phoneNumberValidator = phoneNumberValidator;
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, HttpContext httpContext)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        PhoneNumberValidationResult validation;
        try
        {
            validation = await _phoneNumberValidator.ValidateAsync(request.PhoneNumber, httpContext.RequestAborted);
        }
        catch (Exception)
        {
            return Results.Json(new { message = "The phone number could not be validated with the provider." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            response.ValidationErrors.AddRange(validation.ValidationErrors);
            return Results.BadRequest(response);
        }

        var buyerId = httpContext.User.Identity!.Name!;
        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber, httpContext.RequestAborted);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
