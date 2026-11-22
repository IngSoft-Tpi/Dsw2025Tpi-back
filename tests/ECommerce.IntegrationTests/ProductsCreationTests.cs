using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Dsw2025Tpi.Application.Dtos.ProductModel;

namespace ECommerce.IntegrationTests;

[Collection("IntegrationTests")]
public class ProductsCreationTests(CustomWebApplicationFactory factory)
{
    private const string Route = "/api/products";

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden(
        string? role, HttpStatusCode expectedStatus)
    {
        using var client = factory.CreateApiClient(role);
        using var response = await client.PostAsJsonAsync(Route, NewProductRequest());

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        using var client = factory.CreateApiClient("admin");
        var request = NewProductRequest();

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.IsType<ProductResponse>(
            await response.Content.ReadFromJsonAsync<ProductResponse>());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Sku == request.Sku);
        Assert.Equal(created.Id, product.Id);
        Assert.Equal(request, new ProductRequest(product.Sku, product.Name, product.CurrentUnitPrice,
            product.InternalCode, product.Description, product.StockQuantity));
    }

    [Fact]
    public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsBadRequest()
    {
        using var client = factory.CreateApiClient("admin");
        var request = NewProductRequest();

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            db.Products.Add(new Product(request.Sku, "existing-" + Guid.NewGuid().ToString("N"),
                "Producto existente", request.Description, request.CurrentUnitPrice, request.StockQuantity));
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"Ya existe un producto con el Sku {request.Sku}", error.GetProperty("error").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var persisted = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var product = await persisted.Products.AsNoTracking().SingleAsync(p => p.Sku == request.Sku);
        Assert.Equal("Producto existente", product.Name);
    }

    private static ProductRequest NewProductRequest()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new ProductRequest($"sku-{suffix}", "Producto de prueba", 25.50m,
            $"code-{suffix}", "Descripción de prueba", 10);
    }
}