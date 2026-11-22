using System.Data.Common;
using System.Net.Http.Headers;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ECommerce.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly DbConnection _keepAliveConnection = new SqliteConnection(
        $"Data Source=ECommerceTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False");

    public HttpClient CreateApiClient(string? role = null)
    {
        var client = CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        if (role is not null)
        {
            var tokens = Services.GetRequiredService<JwtTokenService>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", tokens.GenerateToken($"{role}@example.test", role));
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("JwtConfig:Issuer", "integration-tests");
        builder.UseSetting("JwtConfig:Audience", "integration-tests");
        builder.UseSetting("JwtConfig:Key", "integration-tests-only-signing-key-32-bytes");

        var connectionString = _keepAliveConnection.ConnectionString;
        builder.ConfigureTestServices(services =>
        {
            ConfigureSqlite<Dsw2025TpiContext>(services, connectionString);
            ConfigureSqlite<AuthenticateContext>(services, connectionString);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _keepAliveConnection.Open();
        var connectionString = _keepAliveConnection.ConnectionString;

        using var business = new Dsw2025TpiContext(
            new DbContextOptionsBuilder<Dsw2025TpiContext>().UseSqlite(connectionString).Options);
        using var identity = new AuthenticateContext(
            new DbContextOptionsBuilder<AuthenticateContext>().UseSqlite(connectionString).Options);
        business.Database.EnsureCreated();

        // EnsureCreated skips databases that already have tables from another context.
        identity.GetService<IRelationalDatabaseCreator>().CreateTables();

        var host = base.CreateHost(builder);
        SeedIdentityRolesAndAdmin(host.Services);
        return host;
    }

    private static void SeedIdentityRolesAndAdmin(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();

        foreach (var roleName in new[] { "admin", "user" })
        {
            if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
            {
                roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole(roleName)).GetAwaiter().GetResult();
            }
        }

        const string adminEmail = "admin@example.com";
        if (userManager.FindByEmailAsync(adminEmail).GetAwaiter().GetResult() is null)
        {
            var adminUser = new Microsoft.AspNetCore.Identity.IdentityUser
            {
                UserName = "admin",
                Email = adminEmail,
                EmailConfirmed = true
            };
            var result = userManager.CreateAsync(adminUser, "Admin123!").GetAwaiter().GetResult();
            if (result.Succeeded)
            {
                userManager.AddToRoleAsync(adminUser, "admin").GetAwaiter().GetResult();
            }
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await _keepAliveConnection.DisposeAsync();
        }
    }

    private static void ConfigureSqlite<TContext>(IServiceCollection services, string connectionString)
        where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.AddDbContext<TContext>(options => options.UseSqlite(connectionString));
    }
}