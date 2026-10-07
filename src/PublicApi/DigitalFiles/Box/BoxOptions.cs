using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.DigitalFiles.Box;

/// <summary>
/// Settings for the merchant's Box account, bound from the <c>Box</c> configuration section.
/// <c>Box:AccessToken</c> comes from user-secrets in development and from the environment
/// (<c>Box__AccessToken</c>) elsewhere — never from a file in the repository.
/// </summary>
public class BoxOptions
{
    public const string SectionName = "Box";

    /// <summary>Bearer token used for every Box call.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Box:AccessToken is not configured. Set it via user-secrets or the Box__AccessToken environment variable.")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Name of the folder, directly under the account root, that holds the files for sale.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FolderName { get; set; } = "eshop-digital-products";

    /// <summary>
    /// Total budget for one operation against Box (resolving the folder and listing it, or opening a
    /// download up to its response headers). Retries happen inside this budget.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-attempt timeout for a single HTTP call to Box.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A download is abandoned when Box sends no data for this long.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:10:00")]
    public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Items requested per page when listing a folder (Box allows at most 1000).</summary>
    [Range(1, 1000)]
    public int PageSize { get; set; } = 1000;

    /// <summary>Maximum pages read when listing a folder; beyond it the listing is reported as truncated.</summary>
    [Range(1, 1000)]
    public int MaxListingPages { get; set; } = 10;

    /// <summary>How long the resolved folder id is cached.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan FolderIdCacheDuration { get; set; } = TimeSpan.FromMinutes(10);
}
