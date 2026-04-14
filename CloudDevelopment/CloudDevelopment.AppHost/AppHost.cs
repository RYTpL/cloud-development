var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

// LocalStack — эмулятор AWS (SNS + S3)
var localstack = builder.AddContainer("localstack", "localstack/localstack")
    .WithEnvironment("SERVICES", "sns,s3")
    .WithEnvironment("DEFAULT_REGION", "us-east-1")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "http");

var localstackEndpoint = localstack.GetEndpoint("http");

// FileService
var fileService = builder.AddProject<Projects.FileService>("file-service")
    .WithEnvironment("LocalStack__ServiceUrl", localstackEndpoint)
    .WithEnvironment("Sns__TopicName", "contracts-topic")
    .WithHttpEndpoint(port: 5100, name: "filehttp")
    .WaitFor(localstack);

// Передаём URL FileService в GenerationService через переменную окружения
var fileServiceEndpoint = fileService.GetEndpoint("filehttp");

var generation1 = builder.AddProject<Projects.GenerationService>("generation-service-1")
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("LocalStack__ServiceUrl", localstackEndpoint)
    .WithEnvironment("Sns__TopicName", "contracts-topic")
    .WaitFor(localstack)
    .WaitFor(fileService);

var generation2 = builder.AddProject<Projects.GenerationService>("generation-service-2")
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("LocalStack__ServiceUrl", localstackEndpoint)
    .WithEnvironment("Sns__TopicName", "contracts-topic")
    .WaitFor(localstack)
    .WaitFor(fileService);

var generation3 = builder.AddProject<Projects.GenerationService>("generation-service-3")
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("LocalStack__ServiceUrl", localstackEndpoint)
    .WithEnvironment("Sns__TopicName", "contracts-topic")
    .WaitFor(localstack)
    .WaitFor(fileService);

builder.AddProject<Projects.ApiGateway>("api-gateway")
    .WithReference(generation1)
    .WithReference(generation2)
    .WithReference(generation3)
    .WaitFor(generation1)
    .WaitFor(generation2)
    .WaitFor(generation3);

builder.Build().Run();