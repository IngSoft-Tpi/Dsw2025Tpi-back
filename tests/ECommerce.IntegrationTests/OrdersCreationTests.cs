using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Dsw2025Tpi.Application.Dtos.OrderModel;

namespace ECommerce.IntegrationTests;

[Collection("IntegrationTests")]
public class OrdersCreationTests(CustomWebApplicationFactory factory)
{
    private const string Route = "/api/orders";

    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        using var client = factory.CreateApiClient();
        var request = NewOrderRequest(Guid.NewGuid(), Guid.NewGuid(), 3);

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
    {
        using var client = factory.CreateApiClient("user");
        var (customerId, productId) = await SeedCustomerAndProductAsync(stock: 10);
        var request = NewOrderRequest(customerId, productId, 3);

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.IsType<OrderResponse>(await response.Content.ReadFromJsonAsync<OrderResponse>());
        Assert.NotNull(response.Headers.Location);
        Assert.Contains(created.Id.ToString(), response.Headers.Location.OriginalString);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var order = await db.Orders.AsNoTracking().Include(o => o.OrderItems)
            .SingleAsync(o => o.Id == created.Id);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(request.ShippingAddress, order.ShippingAddress);
        Assert.Equal(request.BillingAddress, order.BillingAddress);
        Assert.Equal(request.Notes, order.Notes);
        Assert.Equal(75m, order.TotalAmount);

        var item = Assert.Single(order.OrderItems);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(25m, item.UnitPrice);
        Assert.Equal(75m, item.Subtotal);

        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(7, product.StockQuantity);
    }

    [Fact]
    public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
    {
        using var client = factory.CreateApiClient("user");
        var (customerId, productId) = await SeedCustomerAndProductAsync(stock: 2);
        var request = NewOrderRequest(customerId, productId, 3);

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Stock insuficiente para el producto Producto para orden. Cantidad disponible: 2",
            error.GetProperty("error").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        Assert.False(await db.Orders.AnyAsync(o => o.CustomerId == customerId));
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(2, product.StockQuantity);
    }

    private async Task<(Guid CustomerId, Guid ProductId)> SeedCustomerAndProductAsync(int stock)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var customer = new Customer(Guid.NewGuid(), "Cliente de prueba",
            $"cliente-{suffix}@example.test", "5550100");
        var product = new Product($"sku-{suffix}", $"code-{suffix}", "Producto para orden",
            "Descripción de prueba", 25m, stock) { IsActive = true };
        db.Customers.Add(customer);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (customer.Id, product.Id);
    }

    private static OrderRequest NewOrderRequest(Guid customerId, Guid productId, int quantity) =>
        new(customerId, "Dirección de envío", "Dirección de facturación", "Orden de prueba",
            [new OrderItemModel(productId, quantity)]);
}