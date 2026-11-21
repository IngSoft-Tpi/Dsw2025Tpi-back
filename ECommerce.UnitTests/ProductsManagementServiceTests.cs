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
        [Fact]
        public async Task GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts()
        {
            var activeProduct = new Product("SKU-ACT", "INT-01", "Teclado Mecanico", "RGB", 150m, 10)
            {
                IsActive = true
            };
            var inactiveProduct = new Product("SKU-INA", "INT-02", "Mouse Inactivo", "Optico", 50m, 5)
            {
                IsActive = false
            };
            var mixedProducts = new List<Product> { activeProduct, inactiveProduct };

            _repositoryMock
                .Setup(r => r.GetAll<Product>(It.IsAny<string[]>()))
                .ReturnsAsync(mixedProducts);
            
            var result = await _service.GetProducts();
          
            Assert.NotNull(result);
            Assert.Single(result); 
            Assert.True(result.First().IsActive); 
            Assert.Equal("SKU-ACT", result.First().Sku); 
            _repositoryMock.Verify(r => r.GetAll<Product>(It.IsAny<string[]>()), Times.Once); 
        }
        [Fact]
        public async Task GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly()
        {
            var productList = new List<Product>();
            for (int i = 1; i <= 15; i++)
            {
                var sku = $"SKU-{i:D2}";
                productList.Add(new Product(sku, $"INT-{i:D2}", $"Producto {i}", "Desc", 100m, 10));
            }
    
            _repositoryMock
                .Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
                .ReturnsAsync(productList);
            
            var filter = new ProductModel.FilterProduct(
                Status: null,
                Search: null,
                PageNumber: 2,
                PageSize: 5
            );
            
            var result = await _service.GetProductsFiltered(filter);
            
            Assert.NotNull(result);
            Assert.Equal(15, result.Total); 
            Assert.Equal(5, result.ProductsItems.Count); 
            Assert.Equal("SKU-06", result.ProductsItems.First().Sku);
            Assert.Equal("SKU-10", result.ProductsItems.Last().Sku);
        }
        [Fact]
        public async Task GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode()
        {
            var p1 = new Product("SKU-MOUSE", "INT-01", "Mouse Gamer", "Mouse optico", 50m, 10);
            var p2 = new Product("SKU-TEC", "INT-02", "Teclado Mecánico", "Teclado RGB", 120m, 5);
            var p3 = new Product("SKU-MON", "INT-03", "Monitor", "Monitor 24 pulgadas", 200m, 8);
            var allProducts = new List<Product> { p1, p2, p3 };
            
            _repositoryMock
                .Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
                .ReturnsAsync((Expression<Func<Product, bool>> predicate, string[] includes) =>
                {
                    return allProducts.Where(predicate.Compile()).ToList();
                });
            var filter = new ProductModel.FilterProduct(
                Status: null,
                Search: "mouse", 
                PageNumber: 1,
                PageSize: 10
            );
            
            var result = await _service.GetProductsFiltered(filter);
            
            Assert.NotNull(result);
            Assert.Single(result.ProductsItems); 
            Assert.Equal("SKU-MOUSE", result.ProductsItems.First().Sku);
            Assert.Equal("Mouse Gamer", result.ProductsItems.First().Name);
        }

    }

}
