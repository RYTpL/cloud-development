using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using FileService.Options;
using FileService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── AWS / LocalStack конфигурация ──────────────────────────────
var s3Opts = builder.Configuration.GetSection("S3").Get<S3Options>()!;
var snsOpts = builder.Configuration.GetSection("Sns").Get<SnsOptions>()!;

var credentials = new BasicAWSCredentials(s3Opts.AccessKey, s3Opts.SecretKey);
var region = RegionEndpoint.GetBySystemName(s3Opts.Region);

// S3
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = new AmazonS3Config { RegionEndpoint = region };
    if (!string.IsNullOrEmpty(s3Opts.ServiceUrl))
    {
        // LocalStack или другой совместимый эндпоинт
        config.ServiceURL = s3Opts.ServiceUrl;
        config.ForcePathStyle = true;   // LocalStack требует path-style URL
    }
    return new AmazonS3Client(credentials, config);
});

// SNS
builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
{
    var config = new AmazonSimpleNotificationServiceConfig { RegionEndpoint = region };
    if (!string.IsNullOrEmpty(snsOpts.ServiceUrl))
        config.ServiceURL = snsOpts.ServiceUrl;
    return new AmazonSimpleNotificationServiceClient(credentials, config);
});

// SQS
builder.Services.AddSingleton<IAmazonSQS>(_ =>
{
    var config = new AmazonSQSConfig { RegionEndpoint = region };
    if (!string.IsNullOrEmpty(snsOpts.ServiceUrl))
        config.ServiceURL = snsOpts.ServiceUrl;
    return new AmazonSQSClient(credentials, config);
});

builder.Services.Configure<S3Options>(builder.Configuration.GetSection("S3"));
builder.Services.Configure<SnsOptions>(builder.Configuration.GetSection("Sns"));

builder.Services.AddSingleton<S3FileStorageService>();
builder.Services.AddHostedService<SnsConsumerService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Создаём бакет при старте
using (var scope = app.Services.CreateScope())
{
    var storage = scope.ServiceProvider.GetRequiredService<S3FileStorageService>();
    await storage.EnsureBucketExistsAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapDefaultEndpoints();

app.Run();

public partial class Program { }
