using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Application.Dtos;
using static Dsw2025Tpi.Application.Dtos.OrderModel;
using static Dsw2025Tpi.Application.Dtos.ProductModel;

namespace ECommerce.IntegrationTests;

[Collection("IntegrationTests")]
public class EndToEndFlowsTests(CustomWebApplicationFactory factory)
{
    private const string AuthLoginRoute = "/api/authenticate/login";
    private const string AuthRegisterRoute = "/api/authenticate/register";
    private const string ProductsRoute = "/api/products";
    private const string ProductsAdminRoute = "/api/products/admin";
    private const string OrdersRoute = "/api/orders";

    [Fact]
    public async Task E2E_ProductCreationAndLifecycleFlow_AdminLogsInCreatesUpdatesAndTogglesProduct()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var createRequest = new ProductRequest(
            $"SKU-E2E-{suffix}",
            $"Monitor Curvo {suffix}",
            350000m,
            $"INT-E2E-{suffix}",
            "Monitor E2E de alta tasa de refresco",
            15
        );

        using var createResponse = await client.PostAsJsonAsync(ProductsRoute, createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdProduct = Assert.IsType<ProductResponse>(
            await createResponse.Content.ReadFromJsonAsync<ProductResponse>());
        Assert.NotEqual(Guid.Empty, createdProduct.Id);
        Assert.Equal(createRequest.Sku, createdProduct.Sku);
        Assert.Equal(createRequest.StockQuantity, createdProduct.StockQuantity);

        using var getResponse = await client.GetAsync($"{ProductsRoute}/{createdProduct.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetchedProduct = Assert.IsType<ProductResponse>(
            await getResponse.Content.ReadFromJsonAsync<ProductResponse>());
        Assert.Equal(createdProduct.Id, fetchedProduct.Id);
        Assert.Equal(createRequest.Name, fetchedProduct.Name);

        var updateRequest = createRequest with
        {
            Name = $"Monitor Curvo Pro {suffix}",
            CurrentUnitPrice = 385000m,
            StockQuantity = 20
        };
        using var updateResponse = await client.PutAsJsonAsync($"{ProductsRoute}/{createdProduct.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedProduct = Assert.IsType<ProductResponseUpdate>(
            await updateResponse.Content.ReadFromJsonAsync<ProductResponseUpdate>());
        Assert.Equal(updateRequest.Name, updatedProduct.Name);
        Assert.Equal(updateRequest.CurrentUnitPrice, updatedProduct.CurrentUnitPrice);
        Assert.Equal(updateRequest.StockQuantity, updatedProduct.StockQuantity);
        Assert.True(updatedProduct.IsActive);

        using var toggleResponse = await client.PatchAsync($"{ProductsRoute}/{createdProduct.Id}", null);
        Assert.Equal(HttpStatusCode.NoContent, toggleResponse.StatusCode);

        using var adminListResponse = await client.GetAsync(
            $"{ProductsAdminRoute}?search={suffix}&status=disabled&pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);
        var adminPage = Assert.IsType<ResponsePaginationAdmin>(
            await adminListResponse.Content.ReadFromJsonAsync<ResponsePaginationAdmin>());
        var disabledItem = Assert.Single(adminPage.ProductsItems, p => p.Id == createdProduct.Id);
        Assert.False(disabledItem.IsActive);
    }

    [Fact]
    public async Task E2E_ProductsListingAndCatalogFilteringFlow_PublicAndAdminViewsStayConsistent()
    {
        using var adminClient = await CreateAuthenticatedAdminClientAsync();
        using var publicClient = factory.CreateApiClient();
        var batchTag = "CAT" + Guid.NewGuid().ToString("N")[..10];

        var activeOne = await CreateProductViaHttpAsync(adminClient, $"SKU-A1-{batchTag}", $"Teclado Mecanico {batchTag}", 45000m, 12);
        var activeTwo = await CreateProductViaHttpAsync(adminClient, $"SKU-A2-{batchTag}", $"Mouse Inalambrico {batchTag}", 28000m, 8);
        var toDisable = await CreateProductViaHttpAsync(adminClient, $"SKU-IN-{batchTag}", $"Auriculares {batchTag}", 62000m, 5);

        using var disableResponse = await adminClient.PatchAsync($"{ProductsRoute}/{toDisable.Id}", null);
        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

        using var publicListResponse = await publicClient.GetAsync(
            $"{ProductsRoute}?search={batchTag}&pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, publicListResponse.StatusCode);
        var publicPage = Assert.IsType<ResponsePagination>(
            await publicListResponse.Content.ReadFromJsonAsync<ResponsePagination>());
        Assert.Equal(2, publicPage.Total);
        Assert.Contains(publicPage.ProductsItems, p => p.Id == activeOne.Id);
        Assert.Contains(publicPage.ProductsItems, p => p.Id == activeTwo.Id);
        Assert.DoesNotContain(publicPage.ProductsItems, p => p.Id == toDisable.Id);

        using var publicPageOneResponse = await publicClient.GetAsync(
            $"{ProductsRoute}?search={batchTag}&pageNumber=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, publicPageOneResponse.StatusCode);
        var publicPageOne = Assert.IsType<ResponsePagination>(
            await publicPageOneResponse.Content.ReadFromJsonAsync<ResponsePagination>());
        Assert.Single(publicPageOne.ProductsItems);
        Assert.Equal(2, publicPageOne.Total);

        using var adminAllResponse = await adminClient.GetAsync(
            $"{ProductsAdminRoute}?search={batchTag}&pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, adminAllResponse.StatusCode);
        var adminAllPage = Assert.IsType<ResponsePaginationAdmin>(
            await adminAllResponse.Content.ReadFromJsonAsync<ResponsePaginationAdmin>());
        Assert.Equal(3, adminAllPage.Total);
        Assert.Contains(adminAllPage.ProductsItems, p => p.Id == toDisable.Id && !p.IsActive);
    }

    [Fact]
    public async Task E2E_OrderCreationAndCheckoutFlow_CustomerRegistersBrowsesOrdersAndStockDecrements()
    {
        using var adminClient = await CreateAuthenticatedAdminClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var initialStock = 10;
        var orderedQuantity = 3;
        var unitPrice = 12500m;

        var product = await CreateProductViaHttpAsync(
            adminClient,
            $"SKU-ORD-{suffix}",
            $"Placa de Video {suffix}",
            unitPrice,
            initialStock);

        var (customerClient, customerId) = await RegisterAndAuthenticateCustomerAsync(suffix);

        using (customerClient)
        {
            using var catalogResponse = await customerClient.GetAsync(
                $"{ProductsRoute}?search={suffix}&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            var catalogPage = Assert.IsType<ResponsePagination>(
                await catalogResponse.Content.ReadFromJsonAsync<ResponsePagination>());
            var catalogProduct = Assert.Single(catalogPage.ProductsItems, p => p.Id == product.Id);
            Assert.Equal(initialStock, catalogProduct.StockQuantity);

            var orderRequest = new OrderRequest(
                customerId,
                "Av. Sarmiento 1050, San Miguel de Tucumán",
                "Av. Sarmiento 1050, San Miguel de Tucumán",
                "Entrega en horario comercial",
                [new OrderItemModel(catalogProduct.Id, orderedQuantity)]
            );

            using var createOrderResponse = await customerClient.PostAsJsonAsync(OrdersRoute, orderRequest);
            Assert.Equal(HttpStatusCode.Created, createOrderResponse.StatusCode);
            var createdOrder = Assert.IsType<OrderResponse>(
                await createOrderResponse.Content.ReadFromJsonAsync<OrderResponse>());
            Assert.NotEqual(Guid.Empty, createdOrder.Id);
            Assert.Equal(customerId, createdOrder.CustomerId);
            Assert.Equal(unitPrice * orderedQuantity, createdOrder.TotalAmount);
            var createdItem = Assert.Single(createdOrder.OrderItems);
            Assert.Equal(product.Id, createdItem.ProductId);
            Assert.Equal(orderedQuantity, createdItem.Quantity);

            using var verifyOrderResponse = await adminClient.GetAsync($"{OrdersRoute}/{createdOrder.Id}");
            Assert.Equal(HttpStatusCode.OK, verifyOrderResponse.StatusCode);
            var persistedOrder = Assert.IsType<OrderResponse>(
                await verifyOrderResponse.Content.ReadFromJsonAsync<OrderResponse>());
            Assert.Equal(createdOrder.Id, persistedOrder.Id);
            Assert.Equal(unitPrice * orderedQuantity, persistedOrder.TotalAmount);

            using var verifyProductResponse = await customerClient.GetAsync($"{ProductsRoute}/{product.Id}");
            Assert.Equal(HttpStatusCode.OK, verifyProductResponse.StatusCode);
            var updatedProduct = Assert.IsType<ProductResponse>(
                await verifyProductResponse.Content.ReadFromJsonAsync<ProductResponse>());
            Assert.Equal(initialStock - orderedQuantity, updatedProduct.StockQuantity);
        }
    }

    private async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
    {
        var client = factory.CreateApiClient();
        using var response = await client.PostAsJsonAsync(
            AuthLoginRoute,
            new LoginModel("admin@example.com", "Admin123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(HttpClient Client, Guid CustomerId)> RegisterAndAuthenticateCustomerAsync(string suffix)
    {
        var client = factory.CreateApiClient();
        var registerRequest = new RegisterModel(
            $"cliente.{suffix}@test.utn.edu.ar",
            "Cliente123!",
            $"cliente_{suffix}"
        );

        using var response = await client.PostAsJsonAsync(AuthRegisterRoute, registerRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("token").GetString();
        var userIdText = payload.GetProperty("user").GetProperty("id").GetString();

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(Guid.TryParse(userIdText, out var customerId));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, customerId);
    }

    private static async Task<ProductResponse> CreateProductViaHttpAsync(
        HttpClient adminClient,
        string sku,
        string name,
        decimal price,
        int stock)
    {
        var request = new ProductRequest(sku, name, price, $"INT-{sku}", $"Descripción de {name}", stock);
        using var response = await adminClient.PostAsJsonAsync(ProductsRoute, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return Assert.IsType<ProductResponse>(
            await response.Content.ReadFromJsonAsync<ProductResponse>());
    }
}
