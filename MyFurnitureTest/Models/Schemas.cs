using Newtonsoft.Json.Linq;
namespace MyProject.Models;

public record ProjectData(int user_id, string name, float? l, float? w, string? itemsRaw);
public record ProjectUpdateData(string name, string? itemsRaw);
public record LoginRequest(string? username, string? email, string password);
public record RegisterRequest(string username, string email, string phone, string password);
public record FurnitureItemRequest(int furniture_id, float x, float y, float z);
public record ForgotPasswordRequest(string email);
public record ResetPasswordRequest(string token, string password);

public class PositionUpdateData { public List<PositionItem> positions { get; set; } = new(); }
public class PositionItem
{
    public int index { get; set; }
    public double x { get; set; }
    public double y { get; set; }
    public double z { get; set; }
    public double ry { get; set; }
}