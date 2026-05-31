using System.Text.Json.Serialization;

namespace Kronan.McparIs.Models;

public sealed class ProductSearchResult
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }

    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }

    [JsonPropertyName("hits")]
    public List<ProductSummary> Hits { get; set; } = [];
}

public class ProductSummary
{
    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("discountedPrice")]
    public decimal DiscountedPrice { get; set; }

    [JsonPropertyName("discountPercent")]
    public decimal DiscountPercent { get; set; }

    [JsonPropertyName("onSale")]
    public bool OnSale { get; set; }

    [JsonPropertyName("priceInfo")]
    public string? PriceInfo { get; set; }

    [JsonPropertyName("chargedByWeight")]
    public bool ChargedByWeight { get; set; }

    [JsonPropertyName("pricePerKilo")]
    public decimal? PricePerKilo { get; set; }

    [JsonPropertyName("baseComparisonUnit")]
    public string? BaseComparisonUnit { get; set; }

    [JsonPropertyName("temporaryShortage")]
    public bool TemporaryShortage { get; set; }
}

public sealed class ProductDetail : ProductSummary
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("qtyPerBaseCompUnit")]
    public decimal? QtyPerBaseCompUnit { get; set; }

    [JsonPropertyName("countryOfOrigin")]
    public string? CountryOfOrigin { get; set; }

    [JsonPropertyName("tags")]
    public List<ProductTag> Tags { get; set; } = [];
}

public sealed class ProductTag
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("showOnProductCard")]
    public bool ShowOnProductCard { get; set; }
}

public sealed class Category
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("backgroundImage")]
    public string? BackgroundImage { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("children")]
    public List<CategoryChild> Children { get; set; } = [];
}

public sealed class CategoryChild
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("children")]
    public List<CategoryLeaf> Children { get; set; } = [];
}

public sealed class CategoryLeaf
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
