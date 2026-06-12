namespace MyProject.Models;

// 專案資料模型 (原本在 Program.cs 最下面)
// items 改成可為 null，建立時可以先傳空陣列
public record ProjectData(int user_id, string name, float l, float w, object? items);
// 登入用的資料模型
public record LoginRequest(string? username, string? email, string password);

// 註冊用的資料模型
public record RegisterRequest(string username, string email, string phone,string password);
// 加入家具到專案（含 VR 座標）
public record FurnitureItemRequest(int furniture_id, float x, float y, float z);