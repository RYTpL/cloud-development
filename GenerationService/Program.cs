using Amazon.SimpleNotificationService;
using GenerationService.Options;
using GenerationService.Services;
using FileService.Services;
using Serilog;
using Serilog.Formatting.Compact;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(new CompactJsonFormatter()));

builder.AddRedisDistributedCache("redis");

builder.Services.AddSingleton<ContractGeneratorService>();
builder.Services.AddSingleton<ContractCacheService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<CacheOptions>(
    builder.Configuration.GetSection("CacheOptions"));
builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
{
    var config = builder.Configuration;

    return new AmazonSimpleNotificationServiceClient(
        config["AWS:AccessKey"],
        config["AWS:SecretKey"],
        new AmazonSimpleNotificationServiceConfig
        {
            ServiceURL = config["AWS:ServiceURL"]
        });
});
builder.Services.AddSingleton<SnsPublisherService>();
builder.Services.AddHttpClient();

var app = builder.Build();

var replicaName = builder.Configuration["REPLICA_NAME"] ?? "unknown";

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/contracts/{id:int}", async (
    int id,
    ContractCacheService cacheService,
    ILogger<Program> logger) =>
{
    logger.LogInformation(
        "Request handled by replica {ReplicaName}",
        replicaName);

    var contract = await cacheService.GetOrCreateAsync(id);

    return Results.Ok(contract);
});

app.MapGet("/contracts", async (
    ContractGeneratorService generator,
    SnsPublisherService publisher,
    IHttpClientFactory factory) =>
{
    var contract = generator.Generate(Random.Shared.Next(1, 100000));

    // publish в SNS
    await publisher.PublishAsync(contract);

    // отправка в FileService
    var client = factory.CreateClient();

    await client.PostAsJsonAsync(
        "http://fileservice:8080/sns/contracts",
        contract);

    return Results.Ok(contract);
});

app.MapPost("/sns/contracts", async (
    JsonElement contract,
    S3StorageService storage) =>
{
    await storage.SaveContractAsync(contract);

    return Results.Ok();
});
app.MapDefaultEndpoints();

app.Run();
