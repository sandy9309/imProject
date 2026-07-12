using System.Security.Cryptography;
using MySql.Data.MySqlClient;
using MyProject.Models;

public static class PasswordResetEndpoints
{
    // 前端重設密碼頁的網址，開發時是 React dev server，部署後改成正式網址
    private const string FrontendBaseUrl = "http://localhost:5173";

    // token 有效時間（分鐘）
    private const int TokenExpiryMinutes = 30;

    public static void MapPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // 1. 申請重設密碼：輸入 email，產生一次性 token
        group.MapPost("/forgot-password", async (ForgotPasswordRequest data, DbService db) => {
            try {
                using var conn = db.GetConnection();
                conn.Open();

                // 用 email 找使用者
                int userId = -1;
                string findSql = "SELECT user_id FROM users WHERE email = @mail";
                using (var findCmd = new MySqlCommand(findSql, conn)) {
                    findCmd.Parameters.AddWithValue("@mail", data.email);
                    var result = findCmd.ExecuteScalar();
                    if (result != null) userId = Convert.ToInt32(result);
                }

                if (userId != -1) {
                    // 產生 32 bytes 的加密隨機 token（64 字元 hex）
                    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLower();

                    // 讓同一位使用者之前還沒用過的 token 全部失效（只留最新一組）
                    string invalidateSql = "UPDATE password_reset_tokens SET used = 1 WHERE user_id = @uid AND used = 0";
                    using (var invalidateCmd = new MySqlCommand(invalidateSql, conn)) {
                        invalidateCmd.Parameters.AddWithValue("@uid", userId);
                        invalidateCmd.ExecuteNonQuery();
                    }

                    // 存入新 token，30 分鐘後到期
                    string insertSql = "INSERT INTO password_reset_tokens (user_id, token, expires_at) VALUES (@uid, @token, @expires)";
                    using (var insertCmd = new MySqlCommand(insertSql, conn)) {
                        insertCmd.Parameters.AddWithValue("@uid", userId);
                        insertCmd.Parameters.AddWithValue("@token", token);
                        insertCmd.Parameters.AddWithValue("@expires", DateTime.Now.AddMinutes(TokenExpiryMinutes));
                        insertCmd.ExecuteNonQuery();
                    }

                    // ===== 開發階段：直接印出重設連結，之後這段換成寄信 API（Resend）=====
                    string resetLink = $"{FrontendBaseUrl}/reset-password?token={token}";
                    Console.WriteLine("==================================================");
                    Console.WriteLine($"[忘記密碼] {data.email} 的重設連結（{TokenExpiryMinutes} 分鐘內有效）：");
                    Console.WriteLine(resetLink);
                    Console.WriteLine("==================================================");
                    // ====================================================================
                }

                // 不管 email 存不存在都回同一句話，避免被拿來猜哪些 email 有註冊
                return Results.Ok(new { success = true, message = "如果這個 Email 有註冊過，重設連結已寄出，請查收信箱" });
            } catch (Exception ex) {
                return Results.BadRequest(new { success = false, message = "申請重設出錯", error = ex.Message });
            }
        });

        // 2. 執行重設密碼：驗證 token，更新密碼
        group.MapPost("/reset-password", async (ResetPasswordRequest data, DbService db) => {
            try {
                if (string.IsNullOrEmpty(data.password) || data.password.Length < 6)
                    return Results.BadRequest(new { success = false, message = "密碼長度至少 6 個字元" });

                using var conn = db.GetConnection();
                conn.Open();

                // 查 token：必須存在、沒用過、沒過期
                int userId = -1;
                string checkSql = "SELECT user_id FROM password_reset_tokens WHERE token = @token AND used = 0 AND expires_at > NOW()";
                using (var checkCmd = new MySqlCommand(checkSql, conn)) {
                    checkCmd.Parameters.AddWithValue("@token", data.token);
                    var result = checkCmd.ExecuteScalar();
                    if (result != null) userId = Convert.ToInt32(result);
                }

                if (userId == -1)
                    return Results.BadRequest(new { success = false, message = "連結無效或已過期，請重新申請" });

                // 更新密碼（與註冊相同，用 BCrypt 雜湊）
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(data.password);
                string updateSql = "UPDATE users SET password = @pword WHERE user_id = @uid";
                using (var updateCmd = new MySqlCommand(updateSql, conn)) {
                    updateCmd.Parameters.AddWithValue("@pword", hashedPassword);
                    updateCmd.Parameters.AddWithValue("@uid", userId);
                    updateCmd.ExecuteNonQuery();
                }

                // 標記 token 已使用（一次性）
                string markSql = "UPDATE password_reset_tokens SET used = 1 WHERE token = @token";
                using (var markCmd = new MySqlCommand(markSql, conn)) {
                    markCmd.Parameters.AddWithValue("@token", data.token);
                    markCmd.ExecuteNonQuery();
                }

                return Results.Ok(new { success = true, message = "密碼已重設，請用新密碼登入" });
            } catch (Exception ex) {
                return Results.BadRequest(new { success = false, message = "重設密碼出錯", error = ex.Message });
            }
        });
    }
}
