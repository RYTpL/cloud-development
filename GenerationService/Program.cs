using System.Text.Json;
using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using GenerationService.Messaging;
using GenerationService.Models;
using GenerationService.Services;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(new CompactJsonFormatter()));

// Redis
builder.AddRedisDistributedCache("redis");

// AWS SNS через LocalStack
var localStackUrl = builder.Configuration["LocalStack__ServiceUrl"] ?? "http://localhost:4566";
builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
    new AmazonSimpleNotificationServiceClient(
        new BasicAWSCredentials("test", "test"),
        new AmazonSimpleNotificationServiceConfig
        {
            ServiceURL = localStackUrl,
            AuthenticationRegion = "us-east-1"
        }));

builder.Services.AddSingleton<SnsPublisher>();
builder.Services.AddSingleton<ContractGeneratorService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Эндпоинт GET /contracts/{id} — с кэшированием
app.MapGet("/contracts/{id}", async (
    string id,
    IDistributedCache cache,
    ContractGeneratorService generator,
    SnsPublisher sns,
    ILogger<Program> logger) =>
{
    var cacheKey = $"contract:{id}";
    var cached = await cache.GetStringAsync(cacheKey);

    if (cached is not null)
    {
        logger.LogInformation("Cache HIT для ключа {CacheKey}", cacheKey);
        var cachedContract = JsonSerializer.Deserialize<SoftwareProjectContract>(cached);
        return Results.Ok(cachedContract);
    }

    logger.LogInformation("Cache MISS для ключа {CacheKey}. Генерация...", cacheKey);
    var contract = generator.Generate();

    var options = new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    };
    await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(contract), options);

    // Публикуем в SNS
    await sns.PublishAsync(contract);

    logger.LogInformation("Контракт {ContractId} сохранён в кэш", contract.Id);
    return Results.Ok(contract);
});

// Эндпоинт GET /contracts — без кэша
app.MapGet("/contracts", async (
    ContractGeneratorService generator,
    SnsPublisher sns,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Генерация нового контракта");
    var contract = generator.Generate();

    await sns.PublishAsync(contract);

    return Results.Ok(contract);
});

app.MapDefaultEndpoints();

app.Run();