using MySql.Data.MySqlClient;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cart");

        // GET /api/cart?userId={id} — 取得購物車（含家具完整資料）
        group.MapGet("/", (HttpContext http, DbService db) =>
        {
            string? userIdStr = http.Request.Query["userId"];
            if (string.IsNullOrEmpty(userIdStr))
                return Results.BadRequest(new { message = "缺少 userId" });

            var list = new List<object>();
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var cmd = new MySqlCommand(@"
                    SELECT c.id AS cart_item_id,
                           f.id, f.name, f.category,
                           f.width, f.length_cm, f.height,
                           f.price, f.image_url, f.model_url
                    FROM cart_items c
                    JOIN furnitures f ON c.product_id = f.id
                    WHERE c.user_id = @userId
                    ORDER BY c.added_at DESC", conn);
                cmd.Parameters.AddWithValue("@userId", int.Parse(userIdStr));

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new {
                        id         = Convert.ToInt32(reader["cart_item_id"]),
                        product_id = Convert.ToInt32(reader["id"]),
                        name       = reader["name"]?.ToString() ?? "",
                        category   = reader["category"]?.ToString() ?? "",
                        width      = Convert.ToDecimal(reader["width"]),
                        length_cm  = Convert.ToDecimal(reader["length_cm"]),
                        height     = Convert.ToDecimal(reader["height"]),
                        price      = Convert.ToDecimal(reader["price"]),
                        image_url  = reader["image_url"]?.ToString() ?? "",
                        model_url  = reader["model_url"]?.ToString() ?? ""
                    });
                }
                return Results.Ok(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // POST /api/cart — 加入家具到購物車（避免重複加入）
        // Body: { "user_id": 1, "product_id": 3 }
        group.MapPost("/", (CartItemData data, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var checkCmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM cart_items WHERE user_id = @userId AND product_id = @productId", conn);
                checkCmd.Parameters.AddWithValue("@userId", data.user_id);
                checkCmd.Parameters.AddWithValue("@productId", data.product_id);
                long count = (long)(checkCmd.ExecuteScalar() ?? 0L);

                if (count > 0)
                    return Results.Conflict(new { success = false, message = "此家具已在購物車中" });

                var cmd = new MySqlCommand(
                    "INSERT INTO cart_items (user_id, product_id) VALUES (@userId, @productId)", conn);
                cmd.Parameters.AddWithValue("@userId", data.user_id);
                cmd.Parameters.AddWithValue("@productId", data.product_id);
                cmd.ExecuteNonQuery();

                return Results.Ok(new { success = true, message = "已加入購物車", id = cmd.LastInsertedId });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // DELETE /api/cart/{id} — 移除單筆（cart_items.id）
        group.MapDelete("/{id:int}", (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var cmd = new MySqlCommand("DELETE FROM cart_items WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                int rows = cmd.ExecuteNonQuery();
                return rows > 0
                    ? Results.Ok(new { success = true, message = "已從購物車移除" })
                    : Results.NotFound(new { success = false, message = "找不到此購物車項目" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // DELETE /api/cart?userId={id} — 清空該使用者的購物車
        group.MapDelete("/", (HttpContext http, DbService db) =>
        {
            string? userIdStr = http.Request.Query["userId"];
            if (string.IsNullOrEmpty(userIdStr))
                return Results.BadRequest(new { message = "缺少 userId" });

            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var cmd = new MySqlCommand("DELETE FROM cart_items WHERE user_id = @userId", conn);
                cmd.Parameters.AddWithValue("@userId", int.Parse(userIdStr));
                cmd.ExecuteNonQuery();

                return Results.Ok(new { success = true, message = "購物車已清空" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }
}

public record CartItemData(int user_id, int product_id);
