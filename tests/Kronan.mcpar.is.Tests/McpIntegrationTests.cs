using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Kronan.McparIs.KronanApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kronan.McparIs.Tests;

public sealed class McpIntegrationTests
{
    [Theory]
    [InlineData("missing", 401)]
    [InlineData("expired", 401)]
    [InlineData("wrong-audience", 401)]
    [InlineData("wrong-issuer", 401)]
    [InlineData("wrong-signature", 401)]
    [InlineData("outsider", 403)]
    public async Task Invalid_or_unlisted_callers_never_reach_upstream(string kind, int status)
    {
        using var factory = new McpFactory(); using var client = factory.CreateClient();
        if (kind != "missing") client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(kind));
        var response = await Call(client, "GetShoppingNote", new { });
        Assert.Equal(status, (int)response.StatusCode); Assert.Equal(0, factory.Upstream.Calls);
        if (status == 401) Assert.Contains("oauth-protected-resource", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Discovery_is_public_and_describes_configured_resource()
    {
        using var factory = new McpFactory(); using var client = factory.CreateClient();
        var metadata = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp");
        Assert.Equal("https://kronan.example/mcp", metadata.GetProperty("resource").GetString());
        Assert.Equal("https://issuer.example/", metadata.GetProperty("authorization_servers")[0].GetString());
    }

    [Fact]
    public async Task Health_and_ready_report_the_project_version()
    {
        using var factory = new McpFactory(); using var client = factory.CreateClient();
        var health = await client.GetFromJsonAsync<JsonElement>("/health");
        var ready = await client.GetFromJsonAsync<JsonElement>("/ready");
        Assert.Equal("healthy", health.GetProperty("status").GetString());
        Assert.Equal("ready", ready.GetProperty("status").GetString());
        Assert.Equal("0.1.3", health.GetProperty("version").GetString());
        Assert.Equal(health.GetProperty("version").GetString(), ready.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Registered_tools_include_ui_metadata_and_annotations()
    {
        using var factory = new McpFactory(); using var client = Authenticated(factory);
        using var response = await Rpc(client, "tools/list", new { });
        var root = await Json(response); var tools = root.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "SearchProducts", "GetProduct", "ListCategories", "GetShoppingNote", "PrepareShoppingChange", "AddShoppingItems", "UpdateShoppingItem", "RemoveShoppingItem", "ShowShoppingNote", "ShowProducts", "PrepareApiOperation",
            "ListAddresses", "GetCategoryProducts", "GetActiveCheckout", "AddActiveCheckoutToOrder", "CompleteCheckout", "UpdateCheckoutLines", "PreviewCheckoutLines", "GetIdentity",
            "ListOrders", "GetOrder", "DeleteOrderLines", "ToggleOrderLineSubstitution", "LowerOrderLineQuantities", "GetActiveOrder", "GetOrderLineSummary",
            "GetGiftCardBalance", "ListGiftCardTransactions",
            "ListProductLists", "CreateProductList", "GetProductList", "UpdateProductList", "DeleteProductList", "BatchAddProductListItems", "ClearProductList", "SortProductListItems", "UpdateProductListItem",
            "ListProductPurchaseStats", "SetProductPurchaseIgnored",
            "GetProductByBarcode", "GetProductsBatch", "ListProductsByTag", "ListFavoriteProducts", "ListProductsOnSale", "ListProductTags",
            "ListRecipes", "GetRecipe", "FavoriteRecipe", "UnfavoriteRecipe", "ListFavoriteRecipes", "SearchRecipes", "GetApiSchema",
            "AddShoppingNoteItem", "ReorderShoppingNoteLines", "DeleteArchivedShoppingNoteLine", "ClearShoppingNote", "CheckShoppingNoteStoreOrderEligibility", "ListArchivedShoppingNoteLines", "GetStoreProduct", "ListScanAndGoStores", "SearchStoreProducts", "SortShoppingNoteByStore", "ToggleShoppingNoteLineCompletion",
            "ListDeliverySlots", "ReserveDeliverySlot", "ListPickupSlots", "ReservePickupSlot"
        };
        var actual = tools.Select(tool => tool.GetProperty("name").GetString() ?? "").ToHashSet(StringComparer.Ordinal);
        var difference = actual.Where(name => !expected.Contains(name)).Concat(expected.Where(name => !actual.Contains(name))).Order();
        Assert.True(expected.SetEquals(actual), $"Unexpected registered tools: {string.Join(", ", difference)}");
        var show = tools.Single(t => t.GetProperty("name").GetString() == "ShowShoppingNote");
        Assert.Equal("ui://kronan/shopping-v1.html", show.GetProperty("_meta").GetProperty("ui").GetProperty("resourceUri").GetString());
        Assert.False(show.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.Equal("oauth2", show.GetProperty("_meta").GetProperty("securitySchemes")[0].GetProperty("type").GetString());
        var lists = tools.Single(t => t.GetProperty("name").GetString() == "ListProductLists");
        Assert.Equal("account:read", lists.GetProperty("_meta").GetProperty("securitySchemes")[0].GetProperty("scopes")[0].GetString());

        foreach (var tool in tools.Where(tool => tool.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("request", out _)))
        {
            var request = tool.GetProperty("inputSchema").GetProperty("properties").GetProperty("request");
            var types = request.GetProperty("type").ValueKind == JsonValueKind.Array
                ? request.GetProperty("type").EnumerateArray().Select(value => value.GetString()).ToArray()
                : [request.GetProperty("type").GetString()];
            Assert.Contains("object", types);
            Assert.True(request.GetProperty("properties").TryGetProperty("path", out _));
            Assert.True(request.GetProperty("properties").TryGetProperty("query", out _));
            Assert.True(request.GetProperty("properties").TryGetProperty("body", out _));
        }
    }

    [Fact]
    public async Task Structured_operation_request_is_published_as_an_object_and_reaches_the_endpoint()
    {
        using var factory = new McpFactory(); using var client = Authenticated(factory);
        using var response = await Call(client, "GetProductList", new { request = new { path = new { token = "saved-list" } } });
        var result = (await Json(response)).GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.Equal(1, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Component_resource_is_available_without_exposing_credentials()
    {
        using var factory = new McpFactory(); using var client = Authenticated(factory);
        using var response = await Rpc(client, "resources/read", new { uri = "ui://kronan/shopping-v1.html" });
        var root = await Json(response); var resource = root.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal("text/html;profile=mcp-app", resource.GetProperty("mimeType").GetString());
        Assert.Contains("ui/initialize", resource.GetProperty("text").GetString());
        Assert.DoesNotContain("synthetic-test-token", root.GetRawText());
    }

    [Fact]
    public async Task A_prepared_batch_and_its_replay_make_one_upstream_write()
    {
        using var factory = new McpFactory(); using var client = Authenticated(factory);
        var items = new[] { new { text = "Butter", quantity = 1 }, new { text = "Milk", quantity = 1 } };
        using var preparedResponse = await Call(client, "PrepareShoppingChange", new { change = new { action = "add", items } });
        var prepared = await Json(preparedResponse);
        var id = prepared.GetProperty("result").GetProperty("structuredContent").GetProperty("prepared").GetProperty("operationId").GetString();
        for (var i = 0; i < 2; i++)
        {
            using var response = await Call(client, "AddShoppingItems", new { operationId = id, items });
            var result = (await Json(response)).GetProperty("result");
            Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean());
            Assert.True(result.GetProperty("structuredContent").TryGetProperty("note", out _));
        }
        Assert.Equal(1, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Invalid_update_returns_sanitized_structured_error_without_dispatch()
    {
        using var factory = new McpFactory(); using var client = Authenticated(factory);
        using var response = await Call(client, "PrepareShoppingChange", new { change = new { action = "update", lineToken = Guid.NewGuid() } });
        var result = (await Json(response)).GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("invalid_input", result.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Read_scope_does_not_authorize_mutation_even_with_valid_membership()
    {
        using var factory = new McpFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(scope: "shopping:read"));
        using var response = await Call(client, "PrepareShoppingChange", new { change = new { action = "add", items = new[] { new { text = "Butter" } } } });
        var root = await Json(response);
        Assert.True(root.TryGetProperty("error", out _) || root.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(0, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Another_household_member_cannot_execute_someones_prepared_change()
    {
        using var factory = new McpFactory(); using var first = Authenticated(factory);
        var items = new[] { new { text = "Butter", quantity = 1 } };
        using var prepared = await Call(first, "PrepareShoppingChange", new { change = new { action = "add", items } });
        var id = (await Json(prepared)).GetProperty("result").GetProperty("structuredContent").GetProperty("prepared").GetProperty("operationId").GetString();
        using var second = factory.CreateClient(); second.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(subject: "member-two"));
        using var response = await Call(second, "AddShoppingItems", new { operationId = id, items });
        var result = (await Json(response)).GetProperty("result");
        Assert.Equal("forbidden", result.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Entra_oid_and_short_scp_authorize_fully_qualified_scopes()
    {
        using var factory = new McpFactory(entra: true); using var client = Authenticated(factory);
        using var response = await Call(client, "PrepareShoppingChange", new { change = new { action = "add", items = new[] { new { text = "Butter" } } } });
        var result = (await Json(response)).GetProperty("result");
        Assert.True(result.GetProperty("structuredContent").TryGetProperty("prepared", out _));
        Assert.Equal(0, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Entra_membership_requires_oid_even_if_sub_matches_an_allowed_member()
    {
        using var factory = new McpFactory(entra: true); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token("omit-oid"));
        using var response = await Call(client, "GetShoppingNote", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Upstream.Calls);
    }

    [Fact]
    public async Task A_foreign_line_token_never_reaches_the_delete_endpoint()
    {
        using var factory = new McpFactory(); using var client = Authenticated(factory);
        var lineToken = Guid.NewGuid();
        using var prepared = await Call(client, "PrepareShoppingChange", new { change = new { action = "remove", lineToken } });
        var id = (await Json(prepared)).GetProperty("result").GetProperty("structuredContent").GetProperty("prepared").GetProperty("operationId").GetString();
        using var response = await Call(client, "RemoveShoppingItem", new { operationId = id, lineToken });
        Assert.Equal("line_not_found", (await Json(response)).GetProperty("result").GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, factory.Upstream.Calls); // Only the ownership read, no DELETE.
    }

    private static HttpClient Authenticated(McpFactory factory)
    {
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token()); return client;
    }
    private static Task<HttpResponseMessage> Call(HttpClient client, string name, object args) => Rpc(client, "tools/call", new { name, arguments = args });
    private static Task<HttpResponseMessage> Rpc(HttpClient client, string method, object args)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = args }) };
        request.Headers.Accept.Add(new("application/json")); request.Headers.Accept.Add(new("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        return client.SendAsync(request);
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        if (text.StartsWith("event:", StringComparison.Ordinal) || text.StartsWith("data:", StringComparison.Ordinal))
            text = text.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
        using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
    }
}

internal sealed class McpFactory(bool entra = false) : WebApplicationFactory<Program>
{
    private const string Issuer = "https://issuer.example/";
    private readonly RsaSecurityKey key = new(RSA.Create(2048)) { KeyId = "test-key" };
    public StubHandler Upstream { get; } = new((_, _) => Task.FromResult(StubHandler.Json(TestSupport.NoteJson)));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var setting in new Dictionary<string, string>
        {
            ["Authentication:Authority"] = Issuer, ["Authentication:Audience"] = "kronan-test", ["Authentication:Resource"] = "https://kronan.example/mcp",
            ["Authentication:SubjectClaim"] = entra ? "oid" : "sub",
            ["Authentication:CatalogScope"] = entra ? "https://kronan.example/mcp/catalog:read" : "catalog:read",
            ["Authentication:ShoppingReadScope"] = entra ? "https://kronan.example/mcp/shopping:read" : "shopping:read",
            ["Authentication:ShoppingWriteScope"] = entra ? "https://kronan.example/mcp/shopping:write" : "shopping:write",
            ["Authentication:AllowedSubjects:0"] = "member-one", ["Authentication:AllowedSubjects:1"] = "member-two",
            ["Kronan:ApiKey"] = "synthetic-test-token", ["Limits:StartupCooldownSeconds"] = "0", ["Limits:PacingMilliseconds"] = "0"
        }) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                options.Configuration.SigningKeys.Add(key);
                options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
            });
            services.AddHttpClient<KronanClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
        });
    }
    public string Token(string kind = "valid", string scope = "catalog:read shopping:read shopping:write account:read account:write checkout:commit payments:read", string subject = "member-one")
    {
        var now = DateTime.UtcNow;
        var signingKey = kind == "wrong-signature" ? new RsaSecurityKey(RSA.Create(2048)) { KeyId = "other-key" } : key;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = kind == "wrong-issuer" ? "https://wrong.example/" : Issuer,
            Audience = kind == "wrong-audience" ? "other-api" : "kronan-test",
            Subject = new ClaimsIdentity(entra && kind != "omit-oid"
                ? [new Claim("sub", "pairwise-client-subject"), new Claim("oid", kind == "outsider" ? "outsider" : subject), new Claim("scp", scope)]
                : [new Claim("sub", kind == "outsider" ? "outsider" : subject), new Claim("scope", scope)]),
            NotBefore = now.AddHours(-1), IssuedAt = now.AddHours(-1), Expires = kind == "expired" ? now.AddMinutes(-2) : now.AddMinutes(15),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        });
    }
}
