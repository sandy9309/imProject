using MyProject.Models;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects");

        // ── 1. 取得專案列表（可依 status 篩選）──────────────────────
        // GET /api/projects?userId=5              → 全部
        // GET /api/projects?userId=5&status=draft → 只看待定中
        // GET /api/projects?userId=5&status=confirmed → 只看已確認
        group.MapGet("/", (HttpContext http, DbService db) =>
        {
            var q = http.Request.Query;
            string? userIdStr = q["userId"];
            string? status    = q["status"];

            if (string.IsNullOrEmpty(userIdStr))
                return Results.BadRequest(new { message = "缺少 userId" });

            var projects = new List<object>();
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                string sql = "SELECT id, name, l, w, items, status, updated_at FROM projects WHERE user_id = @userId";
                using var cmd = new MySqlCommand();
                cmd.Connection = conn;
                cmd.Parameters.AddWithValue("@userId", int.Parse(userIdStr));

                if (!string.IsNullOrEmpty(status))
                {
                    sql += " AND status = @status";
                    cmd.Parameters.AddWithValue("@status", status);
                }
                sql += " ORDER BY id DESC";
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string rawItems = reader["items"]?.ToString() ?? "[]";
                    projects.Add(new
                    {
                        id         = Convert.ToInt32(reader["id"]),
                        name       = reader["name"]?.ToString() ?? "",
                        l          = reader["l"],
                        w          = reader["w"],
                        status     = reader["status"]?.ToString() ?? "draft",
                        created_at = reader["updated_at"]?.ToString() ?? "",
                        items      = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawItems)
                    });
                }
                return Results.Ok(new { success = true, data = projects });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 2. 建立新的待定清單（status 預設 draft）───────────────
        // POST /api/projects
        // Body: { "user_id": 5, "name": "我的客廳", "l": 500, "w": 400, "items": [] }
        group.MapPost("/", (ProjectData data, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();
                string itemsJson = string.IsNullOrEmpty(data.itemsRaw) ? "[]" : data.itemsRaw;          
                string sql = @"INSERT INTO projects (user_id, name, l, w, items, status) 
                               VALUES (@user_id, @name, @l, @w, @items, 'draft')";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@user_id", data.user_id);
                cmd.Parameters.AddWithValue("@name", data.name);
                cmd.Parameters.AddWithValue("@l", data.l ?? 0);
                cmd.Parameters.AddWithValue("@w", data.w ?? 0);
                cmd.Parameters.AddWithValue("@items", itemsJson);
                cmd.ExecuteNonQuery();

                return Results.Ok(new {
                    success = true,
                    message = "待定清單建立成功",
                    project_id = cmd.LastInsertedId
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = "建立失敗", error = ex.Message });
            }
        });

        // ── 3. 更新清單內容（加/改家具、改名）────────────────────
        // PUT /api/projects/1
        group.MapPut("/{id}", (int id, ProjectUpdateData data, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var checkCmd = new MySqlCommand(
                    "SELECT status FROM projects WHERE id = @id", conn);
                checkCmd.Parameters.AddWithValue("@id", id);
                string? currentStatus = checkCmd.ExecuteScalar()?.ToString();

                if (currentStatus == null)
                    return Results.NotFound(new { error = "找不到該專案空間" });

                if (currentStatus == "confirmed")
                    return Results.BadRequest(new { success = false, message = "已確認的專案無法修改" });

                string itemsJson = string.IsNullOrEmpty(data.itemsRaw) ? "[]" : data.itemsRaw;                string sql = "UPDATE projects SET name = @name, items = @items WHERE id = @id";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", data.name);
                cmd.Parameters.AddWithValue("@items", itemsJson);
                cmd.ExecuteNonQuery();

                return Results.Ok(new
                {
                    message = "專案更新成功",
                    data = new
                    {
                        _id = id,
                        name = data.name,
                        items = data.itemsRaw
                    }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 4. 確認待定清單 → 變成專案送入 VR ────────────────────
        // PATCH /api/projects/1/confirm
        group.MapMethods("/{id}/confirm", new[] { "PATCH" }, (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE projects SET status = 'confirmed' WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                int rows = cmd.ExecuteNonQuery();
                return rows > 0
                    ? Results.Ok(new { success = true, message = "已確認，VR 可讀取此專案" })
                    : Results.NotFound(new { success = false, message = "找不到此專案" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 5. 刪除待定清單 ────────────────────────────────────────
        // DELETE /api/projects/1
        group.MapDelete("/{id}", (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();
                var cmd = new MySqlCommand("DELETE FROM projects WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                int rows = cmd.ExecuteNonQuery();
                return rows > 0
                    ? Results.Ok(new { success = true, message = "刪除成功" })
                    : Results.NotFound(new { success = false, message = "找不到該專案" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 6. 給 Unity 用：取得專案內所有家具的 model_url ──────────
        // GET /api/projects/{id}/models
        group.MapGet("/{id}/models", (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                // 1. 查 items 欄位
                var projCmd = new MySqlCommand(
                    "SELECT items FROM projects WHERE id = @id", conn);
                projCmd.Parameters.AddWithValue("@id", id);

                var rawItems = projCmd.ExecuteScalar()?.ToString();
                if (rawItems == null)
                    return Results.NotFound(new { message = "找不到此專案" });

                // 2. 解析 items，取出 furniture_id 清單
                var items = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(rawItems)
                            ?? new List<Dictionary<string, object>>();

                if (items.Count == 0)
                    return Results.Ok(new { furnitures = Array.Empty<object>() });

                var furnitureIds = items
                    .Select(i => i.TryGetValue("furniture_id", out var v) ? Convert.ToInt32(v) : 0)
                    .Where(fid => fid > 0)
                    .ToList();

                if (furnitureIds.Count == 0)
                    return Results.Ok(new { furnitures = Array.Empty<object>() });

                // 3. 用 IN 查詢 furnitures 的 model_url
                var paramNames = furnitureIds.Select((_, i) => $"@fid{i}").ToList();
                var furnitureCmd = new MySqlCommand(
                    $"SELECT id, model_url FROM furnitures WHERE id IN ({string.Join(", ", paramNames)})", conn);
                for (int i = 0; i < furnitureIds.Count; i++)
                    furnitureCmd.Parameters.AddWithValue($"@fid{i}", furnitureIds[i]);

                var urlMap = new Dictionary<int, string>();
                using var reader = furnitureCmd.ExecuteReader();
                while (reader.Read())
                {
                    int fid = Convert.ToInt32(reader["id"]);
                    string url = reader["model_url"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(url))
                        urlMap[fid] = url;
                }

                // 4. 依 items 順序組回傳陣列
                var result = furnitureIds
                    .Where(fid => urlMap.ContainsKey(fid))
                    .Select(fid => (object)new { url = urlMap[fid], x = 0, y = 0, z = 0 })
                    .ToList();

                return Results.Ok(new { furnitures = result });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }
}