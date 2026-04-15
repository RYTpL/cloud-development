using Amazon.Runtime;
using Amazon.S3;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using FileService.Models;
using FileService.Services;
using Serilog;
using Serilog.Formatting.Compact;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(new CompactJsonFormatter()));

var localStackUrl = builder.Configuration["LocalStack__ServiceUrl"] ?? "http://localhost:4566";

// AWS S3 через LocalStack
builder.Services.AddSingleton<IAmazonS3>(_ =>
    new AmazonS3Client(
        new BasicAWSCredentials("test", "test"),
        new AmazonS3Config
        {
            ServiceURL = localStackUrl,
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true // обязательно для LocalStack
        }));

// AWS SNS через LocalStack
builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
    new AmazonSimpleNotificationServiceClient(
        new BasicAWSCredentials("test", "test"),
        new AmazonSimpleNotificationServiceConfig
        {
            ServiceURL = localStackUrl,
            AuthenticationRegion = "us-east-1"
        }));

builder.Services.AddSingleton<S3FileStorageService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// При старте — создаём бакет и подписываемся на SNS
app.Lifetime.ApplicationStarted.Register(async () =>
{
    var storage = app.Services.GetRequiredService<S3FileStorageService>();
    var sns = app.Services.GetRequiredService<IAmazonSimpleNotificationService>();
    var config = app.Services.GetRequiredService<IConfiguration>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    // Создаём бакет если не существует
    await storage.EnsureBucketExistsAsync();

    // Создаём SNS топик
    var topicName = config["Sns__TopicName"] ?? "contracts-topic";
    var createTopicResponse = await sns.CreateTopicAsync(topicName);
    var topicArn = createTopicResponse.TopicArn;
    logger.LogInformation("SNS топик: {TopicArn}", topicArn);

    // Подписываемся на топик через HTTP
    var fileServiceUrl = config["FileService__Url"] ?? "http://localhost:5100";
    await sns.SubscribeAsync(new SubscribeRequest
    {
        TopicArn = topicArn,
        Protocol = "http",
        Endpoint = $"{fileServiceUrl}/sns"
    });
    logger.LogInformation("FileService подписан на SNS топик");
});

// Эндпоинт для получения сообщений от SNS
app.MapPost("/sns", async (
    HttpRequest request,
    S3FileStorageService storage,
    ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    logger.LogInformation("Получено сообщение от SNS");

    try
    {
        // SNS оборачивает сообщение в свой конверт
        var snsMessage = JsonSerializer.Deserialize<JsonElement>(body);

        // Тип сообщения — подтверждение подписки или уведомление
        var messageType = snsMessage.GetProperty("Type").GetString();

        if (messageType == "SubscriptionConfirmation")
        {
            // Подтверждаем подписку
            var subscribeUrl = snsMessage.GetProperty("SubscribeURL").GetString();
            logger.LogInformation("Подтверждение подписки: {Url}", subscribeUrl);
            using var http = new HttpClient();
            await http.GetAsync(subscribeUrl);
            return Results.Ok();
        }

        if (messageType == "Notification")
        {
            var message = snsMessage.GetProperty("Message").GetString();
            var contract = JsonSerializer.Deserialize<SoftwareProjectContract>(message!);
            if (contract is not null)
            {
                await storage.SaveContractAsync(contract);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка обработки SNS сообщения");
    }

    return Results.Ok();
});

app.Run();