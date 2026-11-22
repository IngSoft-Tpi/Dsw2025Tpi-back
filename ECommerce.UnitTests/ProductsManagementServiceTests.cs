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
    public class ProductsManagementServiceTests
    {
        private readonly Mock<IRepository> _repositoryMock;
        private readonly ProductsManagementService _service;
        public ProductsManagementServiceTests()
        {
            _repositoryMock = new Mock<IRepository>();
            _service = new ProductsManagementService(_repositoryMock.Object);
        }
        [Fact]
        public async Task AddProduct_WithValidData()
        {
            var request = new ProductModel.ProductRequest("SKU-01", "Mouse", 100m, "INT-01", "Desc", 10);
            _repositoryMock.Setup(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>())).ReturnsAsync((Product?)null);
            _repositoryMock.Setup(r => r.Add(It.IsAny<Product>())).ReturnsAsync((Product p) => p);
           
            var result = await _service.AddProduct(request);
            
            Assert.NotNull(result);
            Assert.Equal(request.Sku, result.Sku);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Once);
        }
        [Fact]
        public async Task AddProduct_WithDuplicateSku()
        {
            var request = new ProductModel.ProductRequest("SKU-DUP", "Mouse", 100m, "INT-01", "Desc", 10);
            var existing = new Product(request.Sku, "INT-OLD", "Existente", "Desc", 100m, 5);
            _repositoryMock.Setup(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>())).ReturnsAsync(existing);
         
            await Assert.ThrowsAsync<DuplicatedEntityException>(() => _service.AddProduct(request));
            _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        }
        [Theory]
        [InlineData(-1, 100)]
        [InlineData(10, 0)]
        [InlineData(10, -50)]
        public async Task AddProduct_WithInvalidStockOrPrice(int stock, decimal price)
        {
            var request = new ProductModel.ProductRequest("SKU-INV", "Teclado", price, "INT-02", "Desc", stock);
            
            await Assert.ThrowsAnyAsync<Exception>(() => _service.AddProduct(request));
            _repositoryMock.Verify(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>()), Times.Never);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        }
    }
}
