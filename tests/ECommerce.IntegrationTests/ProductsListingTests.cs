using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using static Dsw2025Tpi.Application.Dtos.ProductModel;

namespace ECommerce.IntegrationTests;

[Collection("IntegrationTests")]
public class ProductsListingTests(CustomWebApplicationFactory factory)
{
    private const string Route = "/api/products";
    private const string AdminRoute = "/api/products/admin";

    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        await SeedProductsAsync(("Producto público " + Guid.NewGuid().ToString("N"), true));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.GetProperty("productsItems").ValueKind);
        Assert.Equal(JsonValueKind.Number, json.GetProperty("total").ValueKind);

        var page = Assert.IsType<ResponsePagination>(json.Deserialize<ResponsePagination>(JsonOptions));
        Assert.NotEmpty(page.ProductsItems);
        Assert.InRange(page.ProductsItems.Count, 1, 10);
        Assert.True(page.Total >= page.ProductsItems.Count);
    }

    [Fact]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
    {
        var term = "Termino" + Guid.NewGuid().ToString("N");
        var seeded = await SeedProductsAsync(
            ($"Mate {term}", true),
            ($"Bombilla {term}", true),
            ("Yerba sin coincidencia", true));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync($"{Route}?search={term}&pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = Assert.IsType<ResponsePagination>(
            await response.Content.ReadFromJsonAsync<ResponsePagination>());
        Assert.Equal(2, page.Total);
        Assert.Equal(
            seeded.Where(p => p.Name.Contains(term)).Select(p => p.Id).Order(),
            page.ProductsItems.Select(p => p.Id).Order());
        Assert.All(page.ProductsItems, p => Assert.Contains(term, p.Name));
    }

    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        var term = "Admin" + Guid.NewGuid().ToString("N");
        var seeded = await SeedProductsAsync(
            ($"Activo {term}", true),
            ($"Inactivo {term}", false));
        var inactive = seeded.Single(p => !p.IsActive);
        var query = $"?search={term}&pageNumber=1&pageSize=10";

        using var adminClient = factory.CreateApiClient("admin");
        using var adminResponse = await adminClient.GetAsync(AdminRoute + query);

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var adminPage = Assert.IsType<ResponsePaginationAdmin>(
            await adminResponse.Content.ReadFromJsonAsync<ResponsePaginationAdmin>());
        Assert.Equal(2, adminPage.Total);
        var listedInactive = Assert.Single(adminPage.ProductsItems, p => p.Id == inactive.Id);
        Assert.False(listedInactive.IsActive);

        // The public endpoint hides that same inactive product.
        using var publicClient = factory.CreateApiClient();
        using var publicResponse = await publicClient.GetAsync(Route + query);

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var publicPage = Assert.IsType<ResponsePagination>(
            await publicResponse.Content.ReadFromJsonAsync<ResponsePagination>());
        Assert.Equal(1, publicPage.Total);
        Assert.DoesNotContain(publicPage.ProductsItems, p => p.Id == inactive.Id);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<List<Product>> SeedProductsAsync(params (string Name, bool IsActive)[] products)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var entities = products.Select(p =>
        {
            var suffix = Guid.NewGuid().ToString("N");
            return new Product($"sku-{suffix}", $"code-{suffix}", p.Name,
                "Descripción de prueba", 25m, 10) { IsActive = p.IsActive };
        }).ToList();
        db.Products.AddRange(entities);
        await db.SaveChangesAsync();

        return entities;
    }
}
