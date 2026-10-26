using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var pgUser = builder.AddParameter("pg-user", value: "postgres", publishValueAsDefault: true);
var pgPassword = builder.AddParameter("pg-password", value: "postgres", secret: true);

var postgresServer = builder
    .AddPostgres("postgres-server", userName: pgUser, password: pgPassword, port: 5432)
    .WithImage("postgres", "17-alpine")
    .WithContainerName("urlshortener-postgres")
    .WithDataVolume("url-shortener-local-infra_postgres_data")
    .WithLifetime(ContainerLifetime.Persistent);

var postgresDb = postgresServer.AddDatabase("Postgres", databaseName: "urlshortener");

var redis = builder
    .AddRedis("Redis", port: 6379)
    .WithImage("redis", "7.4-alpine")
    .WithContainerName("urlshortener-redis")
    .WithDataVolume("url-shortener-local-infra_redis_data")
    .WithLifetime(ContainerLifetime.Persistent);

var api = builder
    .AddProject<Projects.UrlShortener_API>("urlshortener-api", "http")
    .WithReference(postgresDb)
    .WaitFor(postgresDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "API Root (8080)";
    })
    .WithUrl("http://localhost:8080/scalar/v1", "Scalar Embedded UI")
    .WithUrl("http://localhost:8080/health", "Health JSON");

builder
    .AddScalarApiReference(options =>
    {
        options
            .WithTheme(ScalarTheme.BluePlanet)
            .DisableDefaultFonts()
            .AllowSelfSignedCertificates()
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    })
    .WithApiReference(
        api,
        options =>
        {
            options
                .AddDocument("v1", "urlshortener-api-v1")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json");
        }
    );

builder.Build().Run();
