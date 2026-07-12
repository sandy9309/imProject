using Newtonsoft.Json.Linq;
namespace MyProject.Models;

public record ProjectData(int user_id, string name, float? l, float? w, string? itemsRaw);
public record ProjectUpdateData(string name, string? itemsRaw);
public record LoginRequest(string? username, string? email, string password);
public record RegisterRequest(string username, string email, string phone, string password);
public record FurnitureItemRequest(int furniture_id, float x, float y, float z);
public record ForgotPasswordRequest(string email);
public record ResetPasswordRequest(string token, string password);