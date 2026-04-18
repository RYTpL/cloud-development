var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var localstack = builder.AddContainer("localstack", "localstack/localstack")
    .WithEnvironment("SERVICES", "sns,s3")
    .WithEnvironment("DEFAULT_REGION", "us-east-1")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "http");

var localstackEndpoint = localstack.GetEndpoint("http");

var generation1 = builder.AddProject<Projects.GenerationService>("generation-service-1")
    .WithReference(redis)
    .WaitFor(redis);

var generation2 = builder.AddProject<Projects.GenerationService>("generation-service-2")
    .WithReference(redis)
    .WaitFor(redis);

var generation3 = builder.AddProject<Projects.GenerationService>("generation-service-3")
    .WithReference(redis)
    .WaitFor(redis);

builder.AddProject<Projects.ApiGateway>("api-gateway")
    .WithReference(generation1)
    .WithReference(generation2)
    .WithReference(generation3)
    .WaitFor(generation1)
    .WaitFor(generation2)
    .WaitFor(generation3);

builder.AddProject<Projects.FileService>("fileservice");

builder.Build().Run();