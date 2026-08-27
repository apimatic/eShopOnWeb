using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// with the provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository, ISmsService smsService)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsService = smsService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());

        var validation = await _smsService.ValidatePhoneNumberAsync(request.PhoneNumber);
        if (!validation.IsValid)
        {
            response.Error = validation.Error;
            return Results.BadRequest(response);
        }

        var spec = new ContactNumbersByBuyerSpecification(request.BuyerId);
        var existing = await _contactNumberRepository.ListAsync(spec);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate is not null)
        {
            response.ContactNumberId = duplicate.Id;
            response.PhoneNumber = duplicate.PhoneNumber;
            return Results.Ok(response);
        }

        var contactNumber = await _contactNumberRepository.AddAsync(
            new ContactNumber(request.BuyerId, validation.CanonicalNumber!));

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
