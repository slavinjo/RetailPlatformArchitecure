using CartService.Domain.Dtos;
using CartService.Infrastructure;
using CartService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace CartService.IntegrationTests;

/// <summary>
/// Integration tests against real PostgreSQL via Testcontainers.
/// Uses WebApplicationFactory for HTTP-level testing.
/// </summary>
[TestClass]
public class CartApiIntegrationTests
{
    private static PostgreSqlContainer? _postgresContainer;
    private static string? _connectionString;
    private static WebApplicationFactory<Program>? _factory;
    private static HttpClient? _client;

    // Seed product IDs (matching DatabaseSeeder)
    private static readonly Guid AvailableProduct = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000001");
    private static readonly Guid UnavailableProduct = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000005");

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithDatabase("cartdb")
            .WithUsername("cart")
            .WithPassword("cart")
            .Build();

        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        // One shared app instance for the whole class. Tests run in parallel
        // (MethodLevel), so the host MUST be started here — a lazy start from
        // several tests at once would run migrations/seed concurrently.
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace DB context registration with the test container connection
                    services.RemoveAll(typeof(DbContextOptions<CartDbContext>));
                    services.AddDbContext<CartDbContext>(options =>
                        options.UseNpgsql(_connectionString!));
                });
            });

        // Starts the host (migrations + seed) exactly once; HttpClient is
        // thread-safe, so parallel tests can share it.
        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
        if (_postgresContainer is not null)
            await _postgresContainer.DisposeAsync();
    }

    #region Health Checks

    [TestMethod]
    public async Task HealthLive_WhenAppRunning_Returns200()
    {
        // Act
        var response = await _client!.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task HealthReady_WhenDbConnected_Returns200()
    {
        // Act
        var response = await _client!.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    #endregion

    #region Create Cart

    [TestMethod]
    public async Task CreateCart_WhenPost_Returns201WithCartId()
    {
        // Act
        var response = await _client!.PostAsync("/carts", null);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CartResponse>();
        body.Should().NotBeNull();
        body!.CartId.Should().NotBe(Guid.Empty);
        body.Items.Should().BeEmpty();
    }

    #endregion

    #region Full Flow

    [TestMethod]
    public async Task FullFlow_CreateAddGetRemove_VerifiesEmpty()
    {
        // 1. Create cart
        var createResponse = await _client!.PostAsync("/carts", null);
        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var cart = await createResponse.Content.ReadFromJsonAsync<CartResponse>();
        cart.Should().NotBeNull();
        var cartId = cart!.CartId;

        // 2. Add item
        var addRequest = new AddItemRequest { ProductId = AvailableProduct, Quantity = 2 };
        var addResponse = await _client.PostAsync($"/carts/{cartId}/items",
            JsonContent.Create(addRequest));
        addResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var afterAdd = await addResponse.Content.ReadFromJsonAsync<CartResponse>();
        afterAdd!.Items.Should().HaveCount(1);
        afterAdd.Items.First().Quantity.Should().Be(2);
        afterAdd.TotalAmount.Should().Be(25.00m); // 12.50 * 2

        // 3. Get cart
        var getResponse = await _client.GetAsync($"/carts/{cartId}");
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<CartResponse>();
        fetched!.CartId.Should().Be(cartId);
        fetched.Items.Should().HaveCount(1);

        // 4. Remove item
        var deleteResponse = await _client.DeleteAsync($"/carts/{cartId}/items/{AvailableProduct}");
        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // 5. Verify empty
        var finalResponse = await _client.GetAsync($"/carts/{cartId}");
        var finalCart = await finalResponse.Content.ReadFromJsonAsync<CartResponse>();
        finalCart!.Items.Should().BeEmpty();
        finalCart.TotalItems.Should().Be(0);
    }

    #endregion

    #region Error Cases

    [TestMethod]
    public async Task AddItem_WhenCartNotFound_Returns404()
    {
        // Act
        var request = new AddItemRequest { ProductId = AvailableProduct, Quantity = 1 };
        var response = await _client!.PostAsync($"/carts/{Guid.NewGuid()}/items",
            JsonContent.Create(request));

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task AddItem_WhenProductNotFound_Returns422()
    {
        // Arrange
        var createResponse = await _client!.PostAsync("/carts", null);
        var cart = await createResponse.Content.ReadFromJsonAsync<CartResponse>();

        // Act
        var request = new AddItemRequest { ProductId = Guid.NewGuid(), Quantity = 1 };
        var response = await _client.PostAsync($"/carts/{cart!.CartId}/items",
            JsonContent.Create(request));

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity);
    }

    [TestMethod]
    public async Task AddItem_WhenProductUnavailable_Returns422()
    {
        // Arrange
        var createResponse = await _client!.PostAsync("/carts", null);
        var cart = await createResponse.Content.ReadFromJsonAsync<CartResponse>();

        // Act
        var request = new AddItemRequest { ProductId = UnavailableProduct, Quantity = 1 };
        var response = await _client.PostAsync($"/carts/{cart!.CartId}/items",
            JsonContent.Create(request));

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity);
    }

    [TestMethod]
    public async Task GetCart_WhenCartNotFound_Returns404()
    {
        // Act
        var response = await _client!.GetAsync($"/carts/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    #endregion

    #region Concurrency

    [TestMethod]
    public async Task Concurrency_WhenParallelUpdates_OneGets409()
    {
        // Arrange — create cart and add item
        var createResponse = await _client!.PostAsync("/carts", null);
        var cart = await createResponse.Content.ReadFromJsonAsync<CartResponse>();
        var cartId = cart!.CartId;

        var addRequest = new AddItemRequest { ProductId = AvailableProduct, Quantity = 1 };
        await _client.PostAsync($"/carts/{cartId}/items", JsonContent.Create(addRequest));

        // Act — fire batches of parallel updates until a conflict is observed.
        // A single pair can serialize (both succeed), so retry a few times to
        // avoid flakiness; xmin optimistic concurrency guarantees a 409 as soon
        // as two requests actually overlap.
        var sawConflict = false;
        for (var attempt = 0; attempt < 10 && !sawConflict; attempt++)
        {
            var updates = Enumerable.Range(2, 4).Select(quantity =>
                _client.PutAsync($"/carts/{cartId}/items/{AvailableProduct}",
                    JsonContent.Create(new UpdateQuantityRequest { Quantity = quantity })));

            var responses = await Task.WhenAll(updates);
            sawConflict = responses.Any(r => r.StatusCode == System.Net.HttpStatusCode.Conflict);
        }

        // Assert — at least one request lost the optimistic concurrency race
        sawConflict.Should().BeTrue("parallel updates of the same cart should trigger a 409 concurrency conflict");
    }

    #endregion

    #region Persistence

    [TestMethod]
    public async Task Persistence_WhenDataSaved_SurvivesRefetch()
    {
        // Arrange
        var createResponse = await _client!.PostAsync("/carts", null);
        var cart = await createResponse.Content.ReadFromJsonAsync<CartResponse>();
        var cartId = cart!.CartId;

        var addRequest = new AddItemRequest { ProductId = AvailableProduct, Quantity = 3 };
        await _client.PostAsync($"/carts/{cartId}/items", JsonContent.Create(addRequest));

        // Act — refetch (simulating new request)
        var getResponse = await _client.GetAsync($"/carts/{cartId}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<CartResponse>();

        // Assert
        fetched!.Items.Should().HaveCount(1);
        fetched.Items.First().Quantity.Should().Be(3);
        fetched.TotalAmount.Should().Be(37.50m);
    }

    #endregion

    #region Authorization (feature-flagged)

    [TestMethod]
    public async Task ProtectedEndpoint_WhenAuthEnabledAndNoToken_Returns401()
    {
        // Arrange — a separate host with Auth:Enabled=true (default is off).
        // No token is sent, so JwtBearer challenges before any IdP metadata is
        // needed — the assertion holds without a running Keycloak.
        using var authFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Auth:Enabled", "true");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<CartDbContext>));
                    services.AddDbContext<CartDbContext>(options =>
                        options.UseNpgsql(_connectionString!));
                });
            });
        using var client = authFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/carts/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    #endregion
}
