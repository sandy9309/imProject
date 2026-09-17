using MySql.Data.MySqlClient;
using MyProject.Models;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // 1. 註冊邏輯
        group.MapPost("/register", async (RegisterRequest data, DbService db) => {
            try {
                using var conn = db.GetConnection();
                conn.Open();

                // 檢查帳號是否已存在
                string checkSql = "SELECT COUNT(*) FROM users WHERE username = @uname";
                using (var checkCmd = new MySqlCommand(checkSql, conn)) {
                    checkCmd.Parameters.AddWithValue("@uname", data.username);
                    if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0) 
                        return Results.Conflict(new { message = "帳號已存在" });
                }

                // 插入新使用者
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(data.password);
                string sql = "INSERT INTO users (username, password, email, phone) VALUES (@uname, @pword, @mail, @phone)";
                using (var cmd = new MySqlCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@uname", data.username);
                    cmd.Parameters.AddWithValue("@pword", hashedPassword);
                    cmd.Parameters.AddWithValue("@mail", data.email);
                    cmd.Parameters.AddWithValue("@phone", data.phone);
                    cmd.ExecuteNonQuery();
                }

                return Results.Ok(new { message = "註冊成功！" });
            } catch (Exception ex) {
                return Results.BadRequest(new { message = "註冊出錯", error = ex.Message });
            }
        });

        // 2. 登入邏輯
        group.MapPost("/login", async (LoginRequest data, DbService db) => {
            try {
                // 判斷前端傳來的是 username 還是 email 
                string identifier = data.username ?? data.email ?? "";
                
                using var conn = db.GetConnection();
                conn.Open();
                
                // 只用帳號查使用者，密碼另外用 BCrypt 驗證
                string sql = "SELECT user_id, username, email, phone, password, created_at FROM users WHERE email = @uname OR username = @uname";
                using (var cmd = new MySqlCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@uname", identifier);

                    using (var readerDb = cmd.ExecuteReader()) {
                        if (readerDb.Read()) {
                            string storedHash = readerDb["password"].ToString() ?? "";
                            if (BCrypt.Net.BCrypt.Verify(data.password, storedHash)) {
                                string myToken = Guid.NewGuid().ToString();
                                return Results.Ok(new {
                                    success = true,
                                    message = "登入成功",
                                    token = myToken,
                                    user_id = readerDb["user_id"],
                                    username = readerDb["username"],
                                    email = readerDb["email"],
                                    phone = readerDb["phone"],
                                    joinDate = readerDb["created_at"] == DBNull.Value
                                        ? null
                                        : Convert.ToDateTime(readerDb["created_at"]).ToString("yyyy-MM-dd")
                                });
                            }
                        }
                    }
                }
                
                // 如果沒讀到資料，表示帳密錯誤
                return Results.Json(new { success = false, message = "帳號或密碼錯誤" }, statusCode: 401);
            } catch (Exception ex) {
                return Results.BadRequest(new { success = false, message = "登入出錯", error = ex.Message });
            }
        });
    }
}