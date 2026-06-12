var builder = WebApplication.CreateBuilder(args);

// 1. 註冊服務 (Service Registration)
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
// 這裡要把剛剛寫的 DbService 加入依賴注入容器
builder.Services.AddSingleton<DbService>(); 

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.Urls.Add("http://0.0.0.0:5050");

// 2. 啟動中間件
app.UseCors("AllowAll");

// 3. 掛載所有模組化後的路由
app.MapAuthEndpoints();      // 會員模組
app.MapFurnitureEndpoints(); // 家具模組
app.MapProjectEndpoints();   // 專案模組
app.MapCartEndpoints();      // 購物車模組

app.UseCors();
app.Run();