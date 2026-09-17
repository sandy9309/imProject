using MySql.Data.MySqlClient;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        // PUT /api/users/{id} — 更新姓名與電話
        group.MapPut("/{id:int}", (int id, UpdateUserRequest data, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var checkCmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE username = @name AND user_id != @id", conn);
                checkCmd.Parameters.AddWithValue("@name", data.name);
                checkCmd.Parameters.AddWithValue("@id", id);
                long count = (long)(checkCmd.ExecuteScalar() ?? 0L);

                if (count > 0)
                    return Results.Conflict(new { success = false, message = "此名稱已被使用" });

                var cmd = new MySqlCommand(
                    "UPDATE users SET username = @name, phone = @phone WHERE user_id = @id", conn);
                cmd.Parameters.AddWithValue("@name", data.name);
                cmd.Parameters.AddWithValue("@phone", data.phone);
                cmd.Parameters.AddWithValue("@id", id);

                int rows = cmd.ExecuteNonQuery();
                return rows > 0
                    ? Results.Ok(new { success = true, message = "資料更新成功" })
                    : Results.NotFound(new { success = false, message = "找不到此使用者" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // PUT /api/users/{id}/password — 修改密碼
        group.MapPut("/{id:int}/password", (int id, ChangePasswordRequest data, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var selectCmd = new MySqlCommand(
                    "SELECT password FROM users WHERE user_id = @id", conn);
                selectCmd.Parameters.AddWithValue("@id", id);

                string? storedHash = null;
                using (var reader = selectCmd.ExecuteReader())
                {
                    if (reader.Read())
                        storedHash = reader["password"].ToString();
                }

                if (storedHash == null)
                    return Results.NotFound(new { success = false, message = "找不到此使用者" });

                if (!BCrypt.Net.BCrypt.Verify(data.currentPassword, storedHash))
                    return Results.Json(new { success = false, message = "目前密碼不正確" }, statusCode: 401);

                string newHash = BCrypt.Net.BCrypt.HashPassword(data.newPassword);
                var updateCmd = new MySqlCommand(
                    "UPDATE users SET password = @password WHERE user_id = @id", conn);
                updateCmd.Parameters.AddWithValue("@password", newHash);
                updateCmd.Parameters.AddWithValue("@id", id);
                updateCmd.ExecuteNonQuery();

                return Results.Ok(new { success = true, message = "密碼修改成功" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // DELETE /api/users/{id} — 永久刪除帳號
        group.MapDelete("/{id:int}", (int id, DbService db) =>
        {
            using var conn = db.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                var deleteCartCmd = new MySqlCommand("DELETE FROM cart_items WHERE user_id = @id", conn, transaction);
                deleteCartCmd.Parameters.AddWithValue("@id", id);
                deleteCartCmd.ExecuteNonQuery();

                var deleteProjectsCmd = new MySqlCommand("DELETE FROM projects WHERE user_id = @id", conn, transaction);
                deleteProjectsCmd.Parameters.AddWithValue("@id", id);
                deleteProjectsCmd.ExecuteNonQuery();

                var deleteUserCmd = new MySqlCommand("DELETE FROM users WHERE user_id = @id", conn, transaction);
                deleteUserCmd.Parameters.AddWithValue("@id", id);
                deleteUserCmd.ExecuteNonQuery();

                transaction.Commit();
                return Results.Ok(new { success = true, message = "帳號已永久刪除" });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Results.Problem(ex.Message);
            }
        });
    }
}

public record UpdateUserRequest(string name, string phone);
public record ChangePasswordRequest(string currentPassword, string newPassword);
