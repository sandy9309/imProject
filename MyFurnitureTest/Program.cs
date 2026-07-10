var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddSingleton<DbService>(); 

var app = builder.Build();
app.Urls.Add("http://0.0.0.0:5050");

// ✅ 手動強制加 CORS header，解決 ngrok 擋跨來源的問題
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
    context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization, ngrok-skip-browser-warning");

    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 204;
        return;
    }

    await next();
});

app.UseCors("AllowAll");

app.MapAuthEndpoints();
app.MapFurnitureEndpoints();
app.MapProjectEndpoints();
app.MapCartEndpoints();
app.MapUserEndpoints();

app.Run();