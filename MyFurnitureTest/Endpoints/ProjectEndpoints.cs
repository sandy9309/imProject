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

        var rows = new List<(int id, string name, object l, object w, string status, string createdAt, List<System.Text.Json.JsonElement> items)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                string rawItems = reader["items"]?.ToString() ?? "[]";
                List<System.Text.Json.JsonElement> parsedItems;
                try
                {
                    parsedItems = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(rawItems)
                                  ?? new List<System.Text.Json.JsonElement>();
                }
                catch
                {
                    parsedItems = new List<System.Text.Json.JsonElement>();
                }

                rows.Add((
                    Convert.ToInt32(reader["id"]),
                    reader["name"]?.ToString() ?? "",
                    reader["l"],
                    reader["w"],
                    reader["status"]?.ToString() ?? "draft",
                    reader["updated_at"]?.ToString() ?? "",
                    parsedItems
                ));
            }
        }

        var furnitureIds = rows
            .SelectMany(r => r.items)
            .Where(it => it.TryGetProperty("furniture_id", out _))
            .Select(it => it.GetProperty("furniture_id").GetInt32())
            .Distinct()
            .ToList();

        var furnitureMap = new Dictionary<int, (string name, string imageUrl)>();
        if (furnitureIds.Count > 0)
        {
            var paramNames = furnitureIds.Select((_, i) => $"@fid{i}").ToList();
            using var furnitureCmd = new MySqlCommand(
                $"SELECT id, name, image_url FROM furnitures WHERE id IN ({string.Join(", ", paramNames)})", conn);
            for (int i = 0; i < furnitureIds.Count; i++)
                furnitureCmd.Parameters.AddWithValue($"@fid{i}", furnitureIds[i]);

            using var furnitureReader = furnitureCmd.ExecuteReader();
            while (furnitureReader.Read())
            {
                int fid = Convert.ToInt32(furnitureReader["id"]);
                furnitureMap[fid] = (
                    furnitureReader["name"]?.ToString() ?? "",
                    furnitureReader["image_url"]?.ToString() ?? ""
                );
            }
        }

        var projects = rows.Select(r => new
        {
            id         = r.id,
            name       = r.name,
            l          = r.l,
            w          = r.w,
            status     = r.status,
            created_at = r.createdAt,
            items      = r.items.Select(it =>
            {
                int fid = it.TryGetProperty("furniture_id", out var fidEl) ? fidEl.GetInt32() : 0;
                var (fname, imageUrl) = furnitureMap.TryGetValue(fid, out var info) ? info : ("", "");
                return (object)new {
                    furniture_id = fid,
                    name = fname,
                    image_url = imageUrl
                };
            }).ToList()
        }).ToList();

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
        // 設計：不論專案目前是 draft 或 confirmed，都允許修改 items，
        // id 保持不變；改完後前端可再呼叫一次 PATCH /confirm 重新送 VR。
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

                string itemsJson = string.IsNullOrEmpty(data.itemsRaw) ? "[]" : data.itemsRaw;
                string sql = "UPDATE projects SET name = @name, items = @items WHERE id = @id";

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