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

        // 【異動】多撈 revision
        string sql = "SELECT id, name, l, w, items, status, revision, updated_at FROM projects WHERE user_id = @userId";
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

        var rows = new List<(int id, string name, object l, object w, string status, int revision, string createdAt, List<System.Text.Json.JsonElement> items)>();
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
                    reader["revision"] == DBNull.Value ? 1 : Convert.ToInt32(reader["revision"]),
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
            revision   = r.revision,     // 【新增】
            created_at = r.createdAt,
            items      = r.items.Select(it =>
            {
                int fid = it.TryGetProperty("furniture_id", out var fidEl) ? fidEl.GetInt32() : 0;
                // 【新增】把 item_id 一起回傳，前端修改專案時原封不動送回來即可精準對應
                int iid = it.TryGetProperty("item_id", out var iidEl) ? iidEl.GetInt32() : 0;
                var (fname, imageUrl) = furnitureMap.TryGetValue(fid, out var info) ? info : ("", "");
                return (object)new {
                    item_id = iid,
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
                string itemsJson = NormalizeItems(data.itemsRaw).ToString(Newtonsoft.Json.Formatting.None);
                string sql = @"INSERT INTO projects (user_id, name, l, w, items, status, revision)
                               VALUES (@user_id, @name, @l, @w, @items, 'draft', 1)";

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
                    project_id = cmd.LastInsertedId,
                    revision = 1
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
        // id 保持不變；改完後前端可再呼叫一次 PATCH /confirm 重新送 MR。
        // 【異動】每次成功修改 revision +1，並把新的 revision 回傳給呼叫端
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

                var oldCmd = new MySqlCommand("SELECT items FROM projects WHERE id = @id", conn);
                oldCmd.Parameters.AddWithValue("@id", id);
                var oldItems = JArray.Parse(oldCmd.ExecuteScalar()?.ToString() ?? "[]");
                var newItems = NormalizeItems(data.itemsRaw, oldItems);
                string itemsJson = newItems.ToString(Newtonsoft.Json.Formatting.None);
                string sql = "UPDATE projects SET name = @name, items = @items, revision = revision + 1 WHERE id = @id";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", data.name);
                cmd.Parameters.AddWithValue("@items", itemsJson);
                cmd.ExecuteNonQuery();

                int newRevision = ReadRevision(conn, id);

                return Results.Ok(new
                {
                    message = "專案更新成功",
                    revision = newRevision,   // 【新增】呼叫端把它記起來，就不會被自己的修改觸發重載
                    data = new
                    {
                        _id = id,
                        name = data.name,
                        items = data.itemsRaw,        // 維持原樣：回傳前端送來的原始內容，前端不用改
                        items_saved = newItems        // 【新增】實際存進資料庫的內容（含 item_id），前端要用再用
                    }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 4. 確認待定清單 → 變成專案送入 MR ────────────────────
        // PATCH /api/projects/1/confirm
        group.MapMethods("/{id}/confirm", new[] { "PATCH" }, (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE projects SET status = 'confirmed', revision = revision + 1 WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                int rows = cmd.ExecuteNonQuery();
                return rows > 0
                    ? Results.Ok(new { success = true, message = "已確認，MR 可讀取此專案", revision = ReadRevision(conn, id) })
                    : Results.NotFound(new { success = false, message = "找不到此專案" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 5. 刪除待定清單 ────────────────────────────────────────
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
        // 【異動】每一件多回傳 item_id 與 furniture_id；整包多回傳 revision
        group.MapGet("/{id}/models", (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                // 1. 查 items 欄位（連同 revision 一起讀，避免先讀清單後讀版本產生時間差）
                var projCmd = new MySqlCommand(
                    "SELECT items, revision FROM projects WHERE id = @id", conn);
                projCmd.Parameters.AddWithValue("@id", id);

                string? rawItems = null;
                int revision = 1;
                using (var projReader = projCmd.ExecuteReader())
                {
                    if (!projReader.Read())
                        return Results.NotFound(new { message = "找不到此專案" });
                    rawItems = projReader["items"]?.ToString();
                    revision = projReader["revision"] == DBNull.Value ? 1 : Convert.ToInt32(projReader["revision"]);
                }
                if (rawItems == null)
                    return Results.NotFound(new { message = "找不到此專案" });

                // 2. 解析 items，取出 furniture_id 清單
                var items = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(rawItems)
                            ?? new List<Dictionary<string, object>>();

                if (items.Count == 0)
                    return Results.Ok(new { revision, furnitures = Array.Empty<object>() });

                var furnitureIds = items
                    .Select(i => i.TryGetValue("furniture_id", out var v) ? Convert.ToInt32(v) : 0)
                    .Where(fid => fid > 0)
                    .ToList();

                if (furnitureIds.Count == 0)
                    return Results.Ok(new { revision, furnitures = Array.Empty<object>() });

                // 3. 用 IN 查詢 furnitures 的 model_url
                var paramNames = furnitureIds.Select((_, i) => $"@fid{i}").ToList();
                var furnitureCmd = new MySqlCommand(
                    $"SELECT id, model_url FROM furnitures WHERE id IN ({string.Join(", ", paramNames)})", conn);
                for (int i = 0; i < furnitureIds.Count; i++)
                    furnitureCmd.Parameters.AddWithValue($"@fid{i}", furnitureIds[i]);

                var urlMap = new Dictionary<int, string>();
                using (var reader = furnitureCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int fid = Convert.ToInt32(reader["id"]);
                        string url = reader["model_url"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(url))
                            urlMap[fid] = url;
                    }
                }

                // 4. 讀取 items 裡已儲存的座標（缺欄位當 0）
                var result = new List<object>();
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    int fid = it.TryGetValue("furniture_id", out var v) ? Convert.ToInt32(v) : 0;
                    if (fid <= 0 || !urlMap.ContainsKey(fid)) continue;
                    int iid = it.TryGetValue("item_id", out var idv) && idv != null ? Convert.ToInt32(idv) : 0;
                    double GetNum(string key) =>
                        it.TryGetValue(key, out var val) && val != null ? Convert.ToDouble(val.ToString()) : 0;
                    result.Add(new {
                        item_id = iid,          // 【新增】眼鏡端請用這個當唯一識別
                        furniture_id = fid,     // 【新增】哪一款家具（同款可能有多件）
                        index = i,              // 舊欄位保留，僅供相容，勿再用來存座標
                        url = urlMap[fid],
                        x = GetNum("x"), y = GetNum("y"), z = GetNum("z"), ry = GetNum("ry")
                    });
                }

                return Results.Ok(new { revision, furnitures = result });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 7. 給 MR 眼鏡用：擺放完回傳家具座標 ──────────────────────
        // PUT /api/projects/{id}/positions
        // Body: { "positions": [ { "item_id": 3, "x": 1.5, "y": 0, "z": 2.3, "ry": 90 } ] }
        // 【異動】優先用 item_id 比對；沒帶 item_id 才退回用 index（相容舊版 Unity）
        group.MapPut("/{id}/positions", (int id, PositionUpdateData data, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var projCmd = new MySqlCommand("SELECT items FROM projects WHERE id = @id", conn);
                projCmd.Parameters.AddWithValue("@id", id);
                var rawItems = projCmd.ExecuteScalar()?.ToString();
                if (rawItems == null)
                    return Results.NotFound(new { message = "找不到此專案" });

                var items = JArray.Parse(rawItems);
                int applied = 0;
                var skipped = new List<object>();

                foreach (var pos in data.positions)
                {
                    JObject? target = null;
                    int reqId = pos.item_id ?? 0;

                    if (reqId > 0)
                    {
                        // 有帶 item_id：只認 item_id。找不到代表這件已被網頁刪掉，直接略過，
                        // 絕對不可以退回用 index，否則會把座標寫到別件家具身上。
                        target = items.OfType<JObject>()
                                      .FirstOrDefault(o => (o.Value<int?>("item_id") ?? 0) == reqId);
                        if (target == null)
                            skipped.Add(new { item_id = reqId, reason = "此家具已不在專案中" });
                    }
                    else
                    {
                        // 舊版 Unity：沒帶 item_id，只能用 index
                        if (pos.index >= 0 && pos.index < items.Count)
                            target = items[pos.index] as JObject;
                        if (target == null)
                            skipped.Add(new { index = pos.index, reason = "index 超出範圍" });
                    }

                    if (target == null) continue;

                    target["x"]  = pos.x;
                    target["y"]  = pos.y;
                    target["z"]  = pos.z;
                    target["ry"] = pos.ry;
                    applied++;
                }

                var updateCmd = new MySqlCommand(
                    "UPDATE projects SET items = @items, revision = revision + 1 WHERE id = @id", conn);
                updateCmd.Parameters.AddWithValue("@id", id);
                updateCmd.Parameters.AddWithValue("@items", items.ToString(Newtonsoft.Json.Formatting.None));
                updateCmd.ExecuteNonQuery();

                return Results.Ok(new
                {
                    success  = true,
                    message  = "座標已儲存",
                    applied,                        // 成功寫入幾件
                    skipped,                        // 哪幾件沒寫入、原因
                    revision = ReadRevision(conn, id)  // 眼鏡端請更新 lastRevision，避免被自己的存檔觸發重載
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 8. 眼鏡端上傳截圖 ──────────────────────────────────────
        // POST /api/projects/{id}/media  (multipart/form-data: file, type?)
        group.MapPost("/{id}/media", async (int id, HttpRequest request, DbService db) =>
        {
            try
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new { success = false, message = "請使用 multipart/form-data 格式上傳" });

                var form = await request.ReadFormAsync();
                var file = form.Files["file"];
                if (file == null || file.Length == 0)
                    return Results.BadRequest(new { success = false, message = "缺少上傳檔案 file" });

                string type = form["type"].ToString();
                if (string.IsNullOrWhiteSpace(type))
                    type = "screenshot";

                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                if (!allowedExts.Contains(ext))
                    return Results.BadRequest(new { success = false, message = "檔案格式不支援，僅允許 jpg、jpeg、png、webp" });

                using var conn = db.GetConnection();
                conn.Open();

                var checkCmd = new MySqlCommand("SELECT id FROM projects WHERE id = @id", conn);
                checkCmd.Parameters.AddWithValue("@id", id);
                if (checkCmd.ExecuteScalar() == null)
                    return Results.NotFound(new { success = false, message = "找不到此專案" });

                string folderPath = Path.Combine("wwwroot", "uploads", "projects", id.ToString());
                Directory.CreateDirectory(folderPath);

                string fileName = $"{Guid.NewGuid()}{ext}";
                string fullPath = Path.Combine(folderPath, fileName);
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                string relativePath = $"/uploads/projects/{id}/{fileName}";

                var insertCmd = new MySqlCommand(
                    "INSERT INTO project_media (project_id, type, file_path) VALUES (@project_id, @type, @file_path)", conn);
                insertCmd.Parameters.AddWithValue("@project_id", id);
                insertCmd.Parameters.AddWithValue("@type", type);
                insertCmd.Parameters.AddWithValue("@file_path", relativePath);
                insertCmd.ExecuteNonQuery();

                string url = $"{request.Scheme}://{request.Host}{relativePath}";

                return Results.Ok(new
                {
                    success = true,
                    message = "上傳成功",
                    id = insertCmd.LastInsertedId,
                    url
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 9. 取得專案的所有截圖 ──────────────────────────────────
        // GET /api/projects/{id}/media
        group.MapGet("/{id}/media", (int id, HttpRequest request, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var cmd = new MySqlCommand(
                    "SELECT id, type, file_path, created_at FROM project_media WHERE project_id = @project_id ORDER BY created_at DESC", conn);
                cmd.Parameters.AddWithValue("@project_id", id);

                var data = new List<object>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string filePath = reader["file_path"]?.ToString() ?? "";
                        data.Add(new
                        {
                            id = Convert.ToInt32(reader["id"]),
                            type = reader["type"]?.ToString() ?? "",
                            url = $"{request.Scheme}://{request.Host}{filePath}",
                            created_at = reader["created_at"]?.ToString() ?? ""
                        });
                    }
                }

                return Results.Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 10. 刪除截圖 ───────────────────────────────────────────
        // DELETE /api/projects/{id}/media/{mediaId}
        group.MapDelete("/{id}/media/{mediaId}", (int id, int mediaId, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var checkCmd = new MySqlCommand(
                    "SELECT file_path FROM project_media WHERE id = @mediaId AND project_id = @project_id", conn);
                checkCmd.Parameters.AddWithValue("@mediaId", mediaId);
                checkCmd.Parameters.AddWithValue("@project_id", id);
                var filePathObj = checkCmd.ExecuteScalar();
                if (filePathObj == null)
                    return Results.NotFound(new { success = false, message = "找不到此截圖" });

                string filePath = filePathObj.ToString() ?? "";
                string fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", filePath.TrimStart('/'));
                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                var deleteCmd = new MySqlCommand("DELETE FROM project_media WHERE id = @mediaId", conn);
                deleteCmd.Parameters.AddWithValue("@mediaId", mediaId);
                deleteCmd.ExecuteNonQuery();

                return Results.Ok(new { success = true, message = "截圖已刪除" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // ── 11.【新增】給眼鏡端輪詢用：只回傳版本號 ─────────────────
        // GET /api/projects/{id}/revision
        // 回應：{ "projectId": 29, "revision": 15, "updatedAt": "..." }
        // 這支只讀一列、回傳幾十 bytes，眼鏡端每 5 秒呼叫一次不會有負擔。
        // revision 跟上次不同 → 再去呼叫 GET /{id}/models 拿完整清單。
        group.MapGet("/{id}/revision", (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var cmd = new MySqlCommand(
                    "SELECT revision, updated_at FROM projects WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return Results.NotFound(new { message = "找不到此專案（可能已被刪除）" });

                return Results.Ok(new
                {
                    projectId = id,
                    revision  = reader["revision"] == DBNull.Value ? 1 : Convert.ToInt32(reader["revision"]),
                    updatedAt = reader["updated_at"]?.ToString() ?? ""
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }

    // 讀取專案目前的 revision（寫入後回傳給呼叫端用）
    private static int ReadRevision(MySqlConnection conn, int id)
    {
        using var cmd = new MySqlCommand("SELECT revision FROM projects WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? 1 : Convert.ToInt32(v);
    }

    // 統一 items 格式：item_id + furniture_id + 座標
    //
    // item_id 的規則：
    //   1. 前端有送 item_id 且該筆舊資料存在 → 沿用（最精準）
    //   2. 前端沒送 item_id → 依 furniture_id 排隊，接回還沒被認領的舊資料（相容現在的前端）
    //   3. 都對不上 → 視為新加入的家具，給一個這個專案內沒用過的新編號
    // 編號只在同一個專案內唯一，且不會因為別件被刪掉而改變。
    private static JArray NormalizeItems(string? raw, JArray? oldItems = null)
    {
        var parsed = JArray.Parse(string.IsNullOrWhiteSpace(raw) ? "[]" : raw);
        var incoming = parsed.OfType<JObject>()
                             .Where(t => (t.Value<int?>("furniture_id") ?? 0) > 0)
                             .ToList();

        // 舊資料：建立 item_id 索引，並算出下一個可用編號
        var oldById = new Dictionary<int, JObject>();
        int nextId = 1;
        if (oldItems != null)
        {
            foreach (var t in oldItems.OfType<JObject>())
            {
                int oid = t.Value<int?>("item_id") ?? 0;
                if (oid <= 0) continue;
                oldById[oid] = t;
                if (oid >= nextId) nextId = oid + 1;
            }
        }

        // 第一輪：前端有送 item_id 的，精準配對
        var matched = new JObject?[incoming.Count];
        var claimed = new HashSet<int>();
        for (int i = 0; i < incoming.Count; i++)
        {
            int reqId = incoming[i].Value<int?>("item_id") ?? 0;
            if (reqId > 0 && !claimed.Contains(reqId) && oldById.TryGetValue(reqId, out var hit))
            {
                matched[i] = hit;
                claimed.Add(reqId);
            }
        }

        // 第二輪：沒送 item_id 的，依 furniture_id 排隊接回「還沒被第一輪認領」的舊資料
        var pool = new Dictionary<int, Queue<JObject>>();
        if (oldItems != null)
        {
            foreach (var t in oldItems.OfType<JObject>())
            {
                int oid = t.Value<int?>("item_id") ?? 0;
                if (oid > 0 && claimed.Contains(oid)) continue;
                int fid = t.Value<int?>("furniture_id") ?? 0;
                if (fid <= 0) continue;
                if (!pool.TryGetValue(fid, out var q)) pool[fid] = q = new Queue<JObject>();
                q.Enqueue(t);
            }
        }

        var result = new JArray();
        for (int i = 0; i < incoming.Count; i++)
        {
            var t = incoming[i];
            int fid = t.Value<int?>("furniture_id") ?? 0;

            var old = matched[i];
            if (old == null && pool.TryGetValue(fid, out var q) && q.Count > 0)
                old = q.Dequeue();

            int itemId = old?.Value<int?>("item_id") ?? 0;
            if (itemId <= 0) itemId = nextId++;
            
            // 規則 = 這次送來的座標「全部是 0 或根本沒帶」→ 沿用舊座標；
            //        有任何一個非 0 → 視為刻意指定，以這次送來的為準。
            // （原本的寫法只要前端有帶 x 欄位就會整組覆蓋，前端若送 0 會把
            //   使用者在眼鏡裡調好的位置歸零，這裡一併修掉。）
            double In(string key) => t.Value<double?>(key) ?? 0;
            bool incomingHasCoords =
                In("x") != 0 || In("y") != 0 || In("z") != 0 || In("ry") != 0;

            var src = (old != null && !incomingHasCoords) ? old : t;

            result.Add(new JObject {
                ["item_id"]      = itemId,
                ["furniture_id"] = fid,
                ["x"]  = src.Value<double?>("x")  ?? 0,
                ["y"]  = src.Value<double?>("y")  ?? 0,
                ["z"]  = src.Value<double?>("z")  ?? 0,
                ["ry"] = src.Value<double?>("ry") ?? 0
            });
        }
        return result;
    }
}
