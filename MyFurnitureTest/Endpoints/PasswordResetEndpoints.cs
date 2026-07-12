using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;
using MyProject.Models;
using Newtonsoft.Json;

public static class PasswordResetEndpoints
{
    // 共用一個 HttpClient（官方建議不要每次 new）
    private static readonly HttpClient _http = new HttpClient();

    // 前端重設密碼頁的網址（CRA dev server 是 3000 埠）
    // 部署到學校機器後改成 "http://163.13.202.116:3000"
    private const string FrontendBaseUrl = "http://localhost:3000";

    // token 有效時間（分鐘）
    private const int TokenExpiryMinutes = 30;

    public static void MapPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // 1. 申請重設密碼：輸入 email，產生一次性 token
        group.MapPost("/forgot-password", async (ForgotPasswordRequest data, DbService db, IConfiguration config) => {
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

                    string resetLink = $"{FrontendBaseUrl}/reset-password?token={token}";

                    // 開發用：不論有沒有接寄信，都在 console 印一份方便測試
                    Console.WriteLine("==================================================");
                    Console.WriteLine($"[忘記密碼] {data.email} 的重設連結（{TokenExpiryMinutes} 分鐘內有效）：");
                    Console.WriteLine(resetLink);
                    Console.WriteLine("==================================================");

                    // 有設定 Brevo API key 才寄信；沒設定就只印 console（開發模式）
                    string? apiKey = config["Brevo:ApiKey"];
                    string? senderEmail = config["Brevo:SenderEmail"];
                    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(senderEmail)) {
                        var mail = new {
                            sender = new { name = "家具擺設系統", email = senderEmail },
                            to = new[] { new { email = data.email } },
                            subject = "重設密碼",
                            htmlContent = $@"
                                <p>你好，我們收到你重設密碼的申請。</p>
                                <p><a href=""{resetLink}"">點此重設密碼</a>（{TokenExpiryMinutes} 分鐘內有效）</p>
                                <p>如果這不是你本人的操作，請忽略這封信，你的密碼不會被更改。</p>"
                        };

                        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                        req.Headers.Add("api-key", apiKey);
                        req.Content = new StringContent(JsonConvert.SerializeObject(mail), Encoding.UTF8, "application/json");

                        var resp = await _http.SendAsync(req);
                        if (!resp.IsSuccessStatusCode) {
                            // 寄信失敗不讓整個 API 掛掉，印錯誤方便除錯
                            Console.WriteLine($"[Brevo 寄信失敗] {resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
                        }
                    }
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
