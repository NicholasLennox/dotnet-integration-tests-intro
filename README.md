# TodoApi - Integration Testing Demo

A minimal .NET 10 Web API demonstrating how to structure a multi-project solution and write integration tests against it. The focus is on getting the test setup right - the API itself is deliberately simple.



## What is in this project

```
TodoApi/                  ← The Web API (controllers, models, DbContext)
TodoApi.Tests/            ← The integration test project
docker-compose.yml        ← Runs a local SQL Server database
README.md
.gitignore
```

No services, no DTOs, no AutoMapper. Those belong in your own project. The goal here is a clean foundation for integration testing before any of that complexity is added.



## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- VS Code with the [REST Client extension](https://marketplace.visualstudio.com/items?itemName=humao.rest-client)



## Part 1 - Setting up the solution from scratch

Skip to Part 2 if you are cloning this repo.

### The solution file

The `.slnx` lives at the root - not inside any project folder. This is important. Running `dotnet build`, `dotnet test`, or opening the folder in your editor from the root means the tooling sees all projects at once. If the solution file were inside one of the project folders, the other project would be invisible to it.

### Create the solution and projects

```bash
dotnet new sln -n TodoApi
dotnet new webapi -n TodoApi -o TodoApi --use-controllers -f net10.0
dotnet new xunit -n TodoApi.Tests -o TodoApi.Tests -f net10.0
dotnet sln add **/*.csproj
dotnet add TodoApi.Tests/TodoApi.Tests.csproj reference TodoApi/TodoApi.csproj
```

`dotnet sln add **/*.csproj` picks up all `.csproj` files under the current folder and adds them to the solution. The reference on the last line is what allows the test project to see the API's types - without it, `TodoDbContext`, `Program`, and your models are all invisible to the tests.

### Add packages

```bash
# API project
dotnet add TodoApi/TodoApi.csproj package Microsoft.EntityFrameworkCore.SqlServer
dotnet add TodoApi/TodoApi.csproj package Microsoft.EntityFrameworkCore.Design

# Test project
dotnet add TodoApi.Tests/TodoApi.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add TodoApi.Tests/TodoApi.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
dotnet add TodoApi.Tests/TodoApi.Tests.csproj package FluentAssertions
```

The test project gets its own set of packages. `Mvc.Testing` is what gives you `WebApplicationFactory`. `InMemory` is the EF Core provider used during tests instead of SQL Server.

### Create the files

```bash
mkdir TodoApi/Models TodoApi/Data

touch TodoApi/Models/Todo.cs
touch TodoApi/Data/TodoDbContext.cs

rm TodoApi/Controllers/WeatherForecastController.cs
rm TodoApi/WeatherForecast.cs

touch TodoApi.Tests/TestWebAppFactory.cs
touch TodoApi.Tests/TodosControllerTests.cs
rm TodoApi.Tests/UnitTest1.cs

touch docker-compose.yml
touch .gitignore
```



## Part 2 - Running locally

### Start the database

```bash
docker compose up -d
```

SQL Server starts in the background. The first run pulls the image so give it a moment.

### Apply migrations

Run from the root of the solution:

```bash
dotnet ef migrations add InitialCreate --project TodoApi/TodoApi.csproj
dotnet ef database update --project TodoApi/TodoApi.csproj
```

`database update` applies the migration and runs the seed data defined in `TodoDbContext.OnModelCreating`.

### Run the API

```bash
dotnet run --project TodoApi/TodoApi.csproj
```

Check the terminal output for the port - it will be something like `https://localhost:5001`.



## Part 3 - Manual testing

Create `todo.http` at the root. The REST Client extension lets you run each request directly from VS Code with the click of a button above each `###` block.

```http
@baseUrl = https://localhost:5001

### Get all todos
GET {{baseUrl}}/api/todos

### Get a single todo
GET {{baseUrl}}/api/todos/1

### Create a todo
POST {{baseUrl}}/api/todos
Content-Type: application/json

{
  "title": "Write more tests",
  "isComplete": false
}

### Update a todo
PUT {{baseUrl}}/api/todos/1
Content-Type: application/json

{
  "id": 1,
  "title": "Buy groceries",
  "isComplete": true
}

### Delete a todo
DELETE {{baseUrl}}/api/todos/3
```



## Part 4 - Integration tests

This is the core of the demo. The API code is simple on purpose - the interesting part is how the tests are structured.

### WebApplicationFactory

`WebApplicationFactory<Program>` boots your entire application in memory. No running server, no ports, no process to manage. Your tests get an `HttpClient` that speaks directly to the in-memory host. Every layer of your real application runs - routing, middleware, controllers, EF Core - with one exception: the database provider.

### Why swap the database?

Your API registers SQL Server in `Program.cs`. Tests should not depend on a real database being available - that makes them slow, fragile, and impossible to run in CI without extra infrastructure. Instead, `TestWebAppFactory` intercepts the DI container during startup and replaces the SQL Server registration with an in-memory EF Core database. Your `TodoDbContext` never knows the difference.

### ConfigureWebHost - swapping the provider

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        // EF Core 9+ stores provider config in IDbContextOptionsConfiguration<T>,
        // not in DbContextOptions<T>. Remove it before registering InMemory.
        // https://github.com/dotnet/efcore/issues/35126
        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IDbContextOptionsConfiguration<TodoDbContext>));

        if (descriptor != null)
            services.Remove(descriptor);

        services.AddDbContext<TodoDbContext>(options =>
            options.UseInMemoryDatabase("TestDb"));
    });
}
```

EF Core throws if two database providers are registered simultaneously, so you must remove the existing one before adding the new one. The type you remove changed in EF Core 9 - it used to be `DbContextOptions<T>`, now it is `IDbContextOptionsConfiguration<T>`. This is a confirmed breaking change and the Microsoft docs have not caught up yet.

### ConfigureClient - EnsureCreated and seeding

```csharp
protected override void ConfigureClient(HttpClient client)
{
    using var scope = Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.EnsureCreated();
    SeedDatabase(db);
}
```

`ConfigureClient` runs after the application is fully built, which means `Services` is the live DI container and is safe to resolve from. `EnsureCreated()` builds the in-memory schema from your model. `SeedDatabase()` inserts a known set of records so every test run starts from the same state.

This is intentionally separate from `ConfigureWebHost`. Trying to seed inside `ConfigureServices` fails because the service provider has not finished building at that point.

### IClassFixture - what is a fixture?

A fixture is shared setup that is created once and reused across multiple tests. `IClassFixture<TestWebAppFactory>` tells xUnit to boot the application once for the entire test class rather than once per test. Booting a web application is relatively expensive - this keeps the suite fast.

```csharp
public class TodosControllerTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public TodosControllerTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }
}
```

xUnit injects the factory via the constructor. `CreateClient()` returns an `HttpClient` already wired to the in-memory host. From here, writing a test is just making HTTP calls and asserting on the response.

### The mental model

```
Test method
  → HttpClient
    → In-memory host (WebApplicationFactory)
      → Middleware pipeline
        → TodosController
          → TodoDbContext
            → In-memory database
```

Everything runs. Only the database at the bottom is swapped out.



## Part 5 - Running the tests

```bash
dotnet test
```

The tests do not need Docker running - they use the in-memory database entirely.



## What is deliberately missing

**No service layer.** Controllers talk directly to the DbContext. Adding a service or repository layer is a natural next step - it will not break the tests because they test at the HTTP boundary, not the internal structure.

**No DTOs.** Models are returned directly from the API. In your own project you should decouple your API contract from your database model.

**No authentication.** JWT support can be added later. When you do, you will need to configure the factory to issue test tokens, but the overall structure stays the same.

**No global error handling.** Unhandled exceptions surface as 500s. Production code should handle this with middleware.

The tests work fine without any of it. Get the test setup right first, then add complexity on top.

## Part 6 - GitHub Actions
 
The workflow lives at `.github/workflows/tests.yml`. It runs on every push to `main` and on every pull request targeting `main`.
 
```yaml
name: Tests
 
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
 
jobs:
  test:
    runs-on: ubuntu-latest
 
    steps:
      - uses: actions/checkout@v4
 
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
 
      - name: Restore dependencies
        run: dotnet restore
 
      - name: Build
        run: dotnet build --no-restore
 
      - name: Run tests with coverage
        run: dotnet test --no-build --collect:"XPlat Code Coverage"
 
      - name: Generate coverage report
        run: |
          dotnet tool install -g dotnet-reportgenerator-globaltool
          reportgenerator -reports:"**/TestResults/**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:Html
 
      - name: Upload coverage report
        uses: actions/upload-artifact@v4
        with:
          name: coverage-report
          path: coverage/
```
 
The workflow does not need Docker. Because the tests use an in-memory database, they run fine on the GitHub Actions runner with no extra infrastructure.
 
After each run the coverage report is uploaded as an artifact. You can download it from the Actions tab in GitHub and open `index.html` locally to browse coverage by file and line.
 
For pull requests, a failing test will block the merge - GitHub marks the PR check as failed and shows which step broke. This is the core value: tests become a gate, not an afterthought.
 
### What comes next
 
The natural next step is extending this workflow to build a Docker image and push it to the GitHub Container Registry. That turns this into a proper CI pipeline - tests pass, image gets built and stored, ready to deploy.