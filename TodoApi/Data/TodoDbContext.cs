using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>().HasData(
            new Todo { Id = 1, Title = "Buy groceries", IsComplete = false },
            new Todo { Id = 2, Title = "Walk the dog", IsComplete = false },
            new Todo { Id = 3, Title = "Read a book", IsComplete = true }
        );
    }
}