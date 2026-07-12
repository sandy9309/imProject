using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddSingleton<DbService>();

var app = builder.Build();
app.Urls.Add("http://0.0.0.0:5050");

app.UseCors("AllowAll");

// 靜態檔案：提供 wwwroot/models/ 下的 .glb 模型
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".glb"] = "model/gltf-binary";
provider.Mappings[".gltf"] = "model/gltf+json";
app.UseStaticFiles(new StaticFileOptions {
    ContentTypeProvider = provider
});

app.MapAuthEndpoints();
app.MapPasswordResetEndpoints();
app.MapFurnitureEndpoints();
app.MapProjectEndpoints();
app.MapCartEndpoints();
app.MapUserEndpoints();

app.Run();