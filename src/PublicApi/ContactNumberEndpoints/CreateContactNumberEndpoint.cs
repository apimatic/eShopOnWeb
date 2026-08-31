using System;
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
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider and stored in the provider's canonical (E.164) form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository, ISmsNotificationClient smsClient) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, contactNumberRepository, smsClient);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository, ISmsNotificationClient smsClient)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "phoneNumber is required." });
        }

        var validation = await smsClient.ValidatePhoneNumberAsync(request.PhoneNumber, request.CountryCode);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new
            {
                message = "The phone number is not a usable destination.",
                validationErrors = validation.ValidationErrors
            });
        }

        var existing = await contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndNumberSpecification(request.BuyerId, validation.E164Number!));
        if (existing is not null)
        {
            return Results.Conflict(new { message = "This number is already registered.", contactNumberId = existing.Id });
        }

        var contactNumber = new ContactNumber(request.BuyerId, validation.E164Number!, validation.NationalFormat);
        await contactNumberRepository.AddAsync(contactNumber);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        response.NationalFormat = contactNumber.NationalFormat;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    public string? PhoneNumber { get; set; }
    public string? CountryCode { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? NationalFormat { get; set; }
}
