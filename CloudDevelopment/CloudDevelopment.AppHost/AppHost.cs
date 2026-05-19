var builder = DistributedApplication.CreateBuilder(args);

var redis = builder
    .AddRedis("redis")
    .WithRedisInsight();

// LocalStack Ч просто контейнер, без WithReference
builder.AddContainer("localstack", "localstack/localstack", "latest")
    .WithEnvironment("SERVICES", "s3,sns,sqs")
    .WithEnvironment("DEFAULT_REGION", "us-east-1")
    .WithEnvironment("AWS_ACCESS_KEY_ID", "test")
    .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
    .WithHttpEndpoint(port: 4566, targetPort: 4566)
    .WithHttpEndpoint(port: 4510, targetPort: 4510);

// Generation сервисы Ч передаЄм URL через Environment
var generation1 = builder
    .AddProject("generation-1", "../GenerationService/GenerationService.csproj")
    .WithReference(redis)
    .WithEnvironment("LocalStack__Url", "http://localhost:4566")
    .WithEnvironment("SNS__TopicArn", "arn:aws:sns:us-east-1:000000000000:contracts-topic")
    .WithHttpsEndpoint(port: 7130);

var generation2 = builder
    .AddProject("generation-2", "../GenerationService/GenerationService.csproj")
    .WithReference(redis)
    .WithEnvironment("LocalStack__Url", "http://localhost:4566")
    .WithEnvironment("SNS__TopicArn", "arn:aws:sns:us-east-1:000000000000:contracts-topic")
    .WithHttpsEndpoint(port: 7131);

var generation3 = builder
    .AddProject("generation-3", "../GenerationService/GenerationService.csproj")
    .WithReference(redis)
    .WithEnvironment("LocalStack__Url", "http://localhost:4566")
    .WithEnvironment("SNS__TopicArn", "arn:aws:sns:us-east-1:000000000000:contracts-topic")
    .WithHttpsEndpoint(port: 7132);

// Gateway
var gateway = builder
    .AddProject("api-gateway", "../ApiGateway/ApiGateway.csproj");

// FileService
var fileService = builder
    .AddProject("file-service", "../FileService/FileService.csproj")
    .WithEnvironment("LocalStack__Url", "http://localhost:4566")
    .WithEnvironment("SQS__QueueUrl", "http://localhost:4566/000000000000/contracts-queue")
    .WithEnvironment("S3__BucketName", "contracts");

builder.AddProject("client-wasm", "../Client.Wasm/Client.Wasm.csproj")
    .WithReference(generation1)
    .WaitFor(generation1);

builder.Build().Run();