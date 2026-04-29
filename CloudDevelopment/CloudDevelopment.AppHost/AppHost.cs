var builder = DistributedApplication.CreateBuilder(args);


var redis = builder.AddRedis("redis");


builder.AddProject<Projects.GenerationService>("generation-service")
    .WithReference(redis)      
    .WaitFor(redis);            

builder.Build().Run();