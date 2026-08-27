using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// provider up front and stored in the provider's canonical (E.164) form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IPhoneNumberValidator, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext, IPhoneNumberValidator validator, IRepository<ContactNumber> contactNumberRepository) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name;
                return await HandleAsync(request, validator, contactNumberRepository);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IPhoneNumberValidator validator, IRepository<ContactNumber> contactNumberRepository)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.BuyerId) || string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(response);
        }

        var validated = await validator.ValidateAsync(request.PhoneNumber, request.CountryCode);
        if (!validated.Valid || string.IsNullOrEmpty(validated.PhoneNumber))
        {
            response.ValidationErrors = validated.ValidationErrors;
            return Results.BadRequest(response);
        }

        var existing = await contactNumberRepository.CountAsync(new ContactNumberByBuyerAndNumberSpecification(request.BuyerId, validated.PhoneNumber));
        if (existing > 0)
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contactNumber = await contactNumberRepository.AddAsync(new ContactNumber(request.BuyerId, validated.PhoneNumber, validated.NationalFormat));

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        response.NationalFormat = contactNumber.NationalFormat;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Two-letter ISO country code, used when the number is in national format.</summary>
    public string? CountryCode { get; set; }

    public string? BuyerId { get; set; }
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? NationalFormat { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}
