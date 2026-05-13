using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Tests;

// Boots the real API in memory for testing.
// Swaps SQL Server for an in-memory database so no real DB is needed.
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // EF Core 9+ breaking change: DbContextOptions no longer holds provider config directly.
            // Must remove IDbContextOptionsConfiguration instead.
            // https://github.com/dotnet/efcore/issues/35126
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IDbContextOptionsConfiguration<TodoDbContext>));

            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<TodoDbContext>(options =>
                options.UseInMemoryDatabase("TestDb"));
        });
    }

    // 2. Seed after the app is fully built
    // https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests
    protected override void ConfigureClient(HttpClient client)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        db.Database.EnsureCreated();
        SeedDatabase(db);
    }

    private static void SeedDatabase(TodoDbContext db)
    {
        db.Todos.RemoveRange(db.Todos);
        db.SaveChanges();

        db.Todos.AddRange(
            new Todo { Id = 1, Title = "Buy groceries", IsComplete = false },
            new Todo { Id = 2, Title = "Walk the dog", IsComplete = false },
            new Todo { Id = 3, Title = "Read a book", IsComplete = true }
        );
        db.SaveChanges();
    }
}