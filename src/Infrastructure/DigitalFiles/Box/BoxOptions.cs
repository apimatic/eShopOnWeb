using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.DigitalFiles.Box;

/// <summary>
/// Settings for the Box account that holds the digital product files, bound from the <c>Box</c> section.
/// </summary>
public sealed class BoxOptions
{
    public const string SectionName = "Box";

    /// <summary><c>Box:AccessToken</c> — the Box access token. Never logged.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary><c>Box:DigitalProductsFolderName</c> — the folder, directly under the account root, holding the files.</summary>
    [Required(AllowEmptyStrings = false)]
    public string DigitalProductsFolderName { get; set; } = "eshop-digital-products";

    /// <summary><c>Box:DigitalProductsFolderId</c> — optional; when set, used instead of looking the folder up by name.</summary>
    public string? DigitalProductsFolderId { get; set; }

    /// <summary>Upper bound for one HTTP attempt to Box (until response headers).</summary>
    [Range(1, 300)]
    public int AttemptTimeoutSeconds { get; set; } = 15;

    /// <summary>Upper bound for one Box call including retries (metadata calls, and opening a download).</summary>
    [Range(1, 600)]
    public int CallBudgetSeconds { get; set; } = 40;

    /// <summary>How long a download may go without receiving any data before it is abandoned.</summary>
    [Range(1, 600)]
    public int StallTimeoutSeconds { get; set; } = 30;

    /// <summary>Retries for Box reads (all calls in this integration are GETs).</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>Page size when listing a folder (Box allows at most 1000).</summary>
    [Range(1, 1000)]
    public int ListPageSize { get; set; } = 1000;

    /// <summary>Most pages read when listing a folder; a listing that stops here is reported as incomplete.</summary>
    [Range(1, 100)]
    public int MaxListPages { get; set; } = 10;

    /// <summary>How long the SDK may reuse the token before reading <c>Box:AccessToken</c> again (rotation without restart).</summary>
    [Range(10, 3600)]
    public int TokenCacheSeconds { get; set; } = 300;
}
