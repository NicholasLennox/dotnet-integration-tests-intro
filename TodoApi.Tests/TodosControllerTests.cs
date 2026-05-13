using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TodoApi.Models;

namespace TodoApi.Tests;

public class TodosControllerTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public TodosControllerTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/todos");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_ReturnsSeedData()
    {
        var response = await _client.GetAsync("/api/todos");
        var todos = await response.Content.ReadFromJsonAsync<List<Todo>>();

        todos.Should().NotBeEmpty();
        todos.Should().Contain(t => t.Title == "Buy groceries");
    }

    [Fact]
    public async Task GetById_ReturnsCorrectTodo()
    {
        var response = await _client.GetAsync("/api/todos/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var todo = await response.Content.ReadFromJsonAsync<Todo>();
        todo!.Title.Should().Be("Buy groceries");
    }

    [Fact]
    public async Task GetById_WithInvalidId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/todos/999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ThenGetById_ReturnsCreatedTodo()
    {
        // Arrange
        var newTodo = new Todo { Title = "Buy milk", IsComplete = false };

        // Act: POST it
        var createResponse = await _client.PostAsJsonAsync("/api/todos", newTodo);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: GET it back
        var created = await createResponse.Content.ReadFromJsonAsync<Todo>();
        var getResponse = await _client.GetAsync($"/api/todos/{created!.Id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<Todo>();
        fetched!.Title.Should().Be("Buy milk");
    }

    [Fact]
    public async Task Delete_RemovesTodo()
    {
        // Arrange: use a seeded todo
        var deleteResponse = await _client.DeleteAsync("/api/todos/3");

        // Assert it's gone
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync("/api/todos/3");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}