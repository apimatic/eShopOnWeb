using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SquareEndpoints;

/// <summary>
/// Uploads a product photo (JPEG or PNG, at most 5 MB, multipart field <c>photo</c>) as the photo of the product's
/// Square item. Anything else is rejected before Square is called.
/// </summary>
public class CatalogItemPhotoEndpoint : IEndpoint<IResult, HttpContext, int, SquareCatalogPhotoService>
{
    public const string FormField = "photo";

    /// <summary>Room for the multipart envelope around a maximum-size photo.</summary>
    private const long MultipartOverhead = 64 * 1024;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId:int}/photo",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, HttpContext context, SquareCatalogPhotoService photos) =>
            {
                return await HandleAsync(context, catalogItemId, photos);
            })
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<CatalogItemPhotoResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, int catalogItemId, SquareCatalogPhotoService photos)
    {
        var request = context.Request;
        var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
            bodyLimit.MaxRequestBodySize = SquareImageValidation.MaxBytes + MultipartOverhead;

        if (!request.HasFormContentType)
            return Problem(StatusCodes.Status415UnsupportedMediaType, $"Send multipart/form-data with the image in the '{FormField}' field.");
        if (request.ContentLength > SquareImageValidation.MaxBytes + MultipartOverhead)
            return Problem(StatusCodes.Status413PayloadTooLarge, "The photo must be at most 5 MB.");

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(context.RequestAborted);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "The photo must be at most 5 MB.");
        }
        catch (InvalidDataException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The multipart body could not be read.");
        }

        var file = form.Files.GetFile(FormField);
        if (file is null || file.Length == 0)
            return Problem(StatusCodes.Status400BadRequest, $"Attach the image in the '{FormField}' field.");
        if (file.Length > SquareImageValidation.MaxBytes)
            return Problem(StatusCodes.Status413PayloadTooLarge, "The photo must be at most 5 MB.");

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, context.RequestAborted);
            content = buffer.ToArray();
        }

        var format = SquareImageValidation.Detect(content);
        if (format is null)
            return Problem(StatusCodes.Status415UnsupportedMediaType, "Only JPEG or PNG images are accepted.");

        using var deadline = SquareTimeouts.Deadline(context.RequestAborted, SquareTimeouts.Request);
        var result = await photos.UploadAsync(catalogItemId, content, format.Value, deadline.Token);
        if (result is null)
            return Results.NotFound();

        return Results.Ok(new CatalogItemPhotoResponse
        {
            CatalogItemId = catalogItemId,
            ImageId = result.ImageId,
            ImageUrl = result.ImageUrl,
            SquareItemId = result.SquareItemId,
        });
    }

    private static IResult Problem(int status, string detail) => Results.Problem(detail: detail, statusCode: status);
}

public class CatalogItemPhotoResponse : BaseResponse
{
    public int CatalogItemId { get; set; }
    public string ImageId { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string SquareItemId { get; set; } = string.Empty;
}
