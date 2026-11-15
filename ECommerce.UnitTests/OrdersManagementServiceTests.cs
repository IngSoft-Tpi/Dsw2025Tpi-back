using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Xunit;
using System.Linq.Expressions;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;


namespace ECommerce.UnitTests
{
    public class OrdersManagementServiceTests
    {
        private readonly Mock<IRepository> _repositoryMock;
        private readonly OrdersManagementService _service;
        public OrdersManagementServiceTests()
        {
            _repositoryMock = new Mock<IRepository>();
            _service = new OrdersManagementService(_repositoryMock.Object);
        }
        [Fact]
        public async Task CreateOrder_WithValidItemsAndStock()
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var customer = new Customer(customerId, "Juan", "juan@test.com", "123456");
            var product = new Product("SKU-1", "INT-1", "Monitor", "LED", 200m, 10) { Id = productId };
            var request = new OrderModel.OrderRequest(
                customerId, "Dir 1", "Dir 2", "Nota",
                new List<OrderModel.OrderItemModel> { new(productId, 3) }
            );
            _repositoryMock.Setup(r => r.GetById<Customer>(customerId)).ReturnsAsync(customer);
            _repositoryMock.Setup(r => r.GetById<Product>(productId)).ReturnsAsync(product);
            _repositoryMock.Setup(r => r.Update(It.IsAny<Product>())).ReturnsAsync((Product p) => p);
            _repositoryMock.Setup(r => r.Add(It.IsAny<Order>())).ReturnsAsync((Order o) => o);

            var result = await _service.AddOrder(request);

            Assert.NotNull(result);
            Assert.Equal(600m, result.TotalAmount);
            Assert.Equal(7, product.StockQuantity);
            _repositoryMock.Verify(r => r.Update(It.Is<Product>(p => p.StockQuantity == 7)), Times.Once);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Once);
        }
        [Fact]
        public async Task CreateOrder_WhenProductStockIsInsufficient()
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var customer = new Customer(customerId, "Juan", "juan@test.com", "123456");
            var product = new Product("SKU-1", "INT-1", "Monitor", "LED", 200m, 2) { Id = productId };
            var request = new OrderModel.OrderRequest(
                customerId, "Dir 1", "Dir 2", "Nota",
                new List<OrderModel.OrderItemModel> { new(productId, 5) }
            );
            _repositoryMock.Setup(r => r.GetById<Customer>(customerId)).ReturnsAsync(customer);
            _repositoryMock.Setup(r => r.GetById<Product>(productId)).ReturnsAsync(product);
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _service.AddOrder(request));
            Assert.Equal(2, product.StockQuantity);
            _repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        }
        [Fact]
        public async Task CreateOrder_WhenCustomerDoesNotExist()
        {
            var nonExistentCustomerId = Guid.NewGuid();
            var request = new OrderModel.OrderRequest(
                nonExistentCustomerId, "Dir 1", "Dir 2", "Nota",
                new List<OrderModel.OrderItemModel> { new(Guid.NewGuid(), 1) }
            );
            _repositoryMock.Setup(r => r.GetById<Customer>(nonExistentCustomerId)).ReturnsAsync((Customer?)null);

            await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.AddOrder(request));
            _repositoryMock.Verify(r => r.GetById<Product>(It.IsAny<Guid>()), Times.Never);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        }
    }
}
