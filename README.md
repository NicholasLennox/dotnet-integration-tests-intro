# TodoApi - Integration Testing Demo

A minimal .NET 10 Web API demonstrating how to structure a multi-project solution and write integration tests against it. The focus is on getting the test setup right - the API itself is deliberately simple.



## What is in this project

```
TodoApi/                  ← The Web API (controllers, models, DbContext)
TodoApi.Tests/            ← The integration test project
docker-compose.yml        ← Runs a local SQL Server database
coverlet.runsettings      ← Coverage configuration
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
touch coverlet.runsettings
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



## Part 6 - Code coverage

Coverage tells you which lines of your code were actually executed during the test run. The xunit template already includes `coverlet.collector` so collection is built in - you just need to tell it what to measure and how to display it.

### Install the report generator

This is a global dotnet tool that turns the raw coverage data into a readable HTML report. Install it once on your machine:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

### coverlet.runsettings

Create a file called `coverlet.runsettings` at the root of the solution alongside the `.slnx`. Despite the unfamiliar extension, it is just an XML file - you can open and edit it like any other. The `.runsettings` extension is what tells the .NET test runner to treat it as test configuration.

Without any configuration, coverlet measures everything in the assembly - migrations, models, DbContext, generated code - and your numbers will be meaningless noise. This project uses an `Include` filter to measure only the controllers, which is the only code worth tracking:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Include>[TodoApi]TodoApi.Controllers.*</Include>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

`[TodoApi]` is the assembly name - the name of your API project. `TodoApi.Controllers.*` matches every class in that namespace. When you adapt this for your own project, change both values to match your assembly and controller namespace.

> **Important:** You will find suggestions online to add `CompilerGeneratedAttribute` to an `ExcludeByAttribute` list. Do not do this. The C# compiler compiles async methods into state machines and marks them with that attribute. Adding it will silently exclude all your async action methods from coverage - you will end up with a report that only shows the constructor and nothing else.

### Running coverage locally

```bash
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/TestResults/**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:Html
```

Then open `coverage/index.html` in a browser. On Windows:

```bash
start coverage/index.html
```

The test command runs your tests and produces a `coverage.cobertura.xml` file inside `TestResults/`. The `reportgenerator` command reads that XML and produces the HTML report. The `coverage/` folder is gitignored - it is generated output and does not belong in the repo.

### Reading the report

The report shows line coverage and branch coverage per method. Line coverage tells you whether a line was executed at all. Branch coverage tells you whether both paths of a conditional were tested - for example, whether you tested both the found and not-found cases of a `GetById` endpoint.

A method at 0% means you have no test that reaches it at all. A branch at 50% means you tested one side of an `if` but not the other. Use the report as a map of what is not yet tested, not as a score to maximise.



## Part 7 - GitHub Actions

### What is GitHub Actions?

GitHub Actions is a CI/CD platform built into GitHub. CI stands for Continuous Integration - the practice of automatically running your tests every time code is pushed, so that broken code cannot quietly sit in the repo unnoticed.

You define workflows in YAML files inside `.github/workflows/`. GitHub reads those files and runs them on its own servers whenever the trigger conditions are met - a push, a pull request, a schedule, or manually. Each workflow runs in a clean virtual machine, so it has no memory of previous runs and no access to your local environment.

The practical result for this project: every time someone pushes to `main` or opens a pull request, GitHub spins up a Linux machine, checks out the code, builds it, runs the tests, and reports back. If any test fails, the PR is blocked. The coverage report is saved as a downloadable artifact attached to the run.

### The workflow file

Create `.github/workflows/tests.yml` - the folder structure matters, GitHub will not find it anywhere else.

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
        run: dotnet test --no-build --settings coverlet.runsettings --collect:"XPlat Code Coverage"

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

### What each part means

**`on`** defines when the workflow runs. `push` to `main` runs it on every direct commit. `pull_request` targeting `main` runs it whenever a PR is opened or updated against `main`.

**`jobs`** is the list of things to run. Each job gets its own clean virtual machine. This workflow has one job called `test`.

**`runs-on: ubuntu-latest`** tells GitHub which operating system to use. Ubuntu is the most common choice - it is fast, free, and .NET runs on it without any extra setup.

**`steps`** are the individual commands that run in sequence. If any step fails, the rest are skipped and the workflow is marked as failed.

**`uses`** refers to a pre-built action from the GitHub Actions marketplace. `actions/checkout@v4` clones your repo onto the runner. `actions/setup-dotnet@v4` installs the .NET SDK. `actions/upload-artifact@v4` saves files so you can download them after the run.

**`run`** is a shell command, exactly as you would type it in a terminal. The `|` character allows multiple commands on separate lines.

### What happens on a pull request

When you open a PR targeting `main`, GitHub runs the workflow and reports the result directly on the PR page as a status check. If tests pass, the check shows green. If any test fails, it shows red and GitHub can be configured to block the merge entirely.

After a successful run, the coverage report is available as a downloadable artifact under the Actions tab. Click the workflow run, scroll to Artifacts, and download `coverage-report`. Open `index.html` locally to see the full report.

The workflow does not need Docker running. Because the tests use an in-memory database, they run fine on the GitHub Actions runner with no extra infrastructure.

### What comes next

The natural next step is extending this workflow to build a Docker image and push it to the GitHub Container Registry after tests pass. That turns this into a full CI pipeline - test, build, store. It is a separate addition and does not require changing anything here.

It is also possible to have the coverage percentage posted as a comment directly on the PR rather than just as a downloadable artifact. That requires a third-party action and a small amount of extra configuration - worth adding once the basics are solid.



## What is deliberately missing

**No service layer.** Controllers talk directly to the DbContext. Adding a service or repository layer is a natural next step - it will not break the tests because they test at the HTTP boundary, not the internal structure.

**No DTOs.** Models are returned directly from the API. In your own project you should decouple your API contract from your database model.

**No authentication.** JWT support can be added later. When you do, you will need to configure the factory to issue test tokens, but the overall structure stays the same.

**No global error handling.** Unhandled exceptions surface as 500s. Production code should handle this with middleware.

The tests work fine without any of it. Get the test setup right first, then add complexity on top.