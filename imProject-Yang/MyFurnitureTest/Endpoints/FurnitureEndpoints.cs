using MySql.Data.MySqlClient;

public static class FurnitureEndpoints
{
    public static void MapFurnitureEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/catalog - 給前端編輯器用的家具目錄
        // 支援篩選：keyword, minL/maxL, minW/maxW, minH/maxH, minPrice/maxPrice
        app.MapGet("/api/catalog", (HttpContext http, DbService db) =>
        {
            var q = http.Request.Query;
            string? keyword  = q["keyword"];
            string? minL     = q["minL"];     string? maxL     = q["maxL"];
            string? minW     = q["minW"];     string? maxW     = q["maxW"];
            string? minH     = q["minH"];     string? maxH     = q["maxH"];
            string? minPrice = q["minPrice"]; string? maxPrice = q["maxPrice"];

            var list = new List<object>();
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var sql = @"
                    SELECT id, name, price, image_url, thumb_path,
                           model_url, category, width, length_cm, height, description
                    FROM furnitures
                    WHERE 1=1";

                var conditions = new List<string>();
                using var cmd = new MySqlCommand();
                cmd.Connection = conn;

                if (!string.IsNullOrEmpty(keyword))
                {
                    conditions.Add("(name LIKE @keyword OR description LIKE @keyword)");
                    cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
                }
                if (!string.IsNullOrEmpty(minL))     { conditions.Add("length_cm >= @minL");    cmd.Parameters.AddWithValue("@minL",     decimal.Parse(minL)); }
                if (!string.IsNullOrEmpty(maxL))     { conditions.Add("length_cm <= @maxL");    cmd.Parameters.AddWithValue("@maxL",     decimal.Parse(maxL)); }
                if (!string.IsNullOrEmpty(minW))     { conditions.Add("width >= @minW");    cmd.Parameters.AddWithValue("@minW",     decimal.Parse(minW)); }
                if (!string.IsNullOrEmpty(maxW))     { conditions.Add("width <= @maxW");    cmd.Parameters.AddWithValue("@maxW",     decimal.Parse(maxW)); }
                if (!string.IsNullOrEmpty(minH))     { conditions.Add("height >= @minH");   cmd.Parameters.AddWithValue("@minH",     decimal.Parse(minH)); }
                if (!string.IsNullOrEmpty(maxH))     { conditions.Add("height <= @maxH");   cmd.Parameters.AddWithValue("@maxH",     decimal.Parse(maxH)); }
                if (!string.IsNullOrEmpty(minPrice)) { conditions.Add("price >= @minPrice"); cmd.Parameters.AddWithValue("@minPrice", decimal.Parse(minPrice)); }
                if (!string.IsNullOrEmpty(maxPrice)) { conditions.Add("price <= @maxPrice"); cmd.Parameters.AddWithValue("@maxPrice", decimal.Parse(maxPrice)); }

                if (conditions.Any())
                    sql += " AND " + string.Join(" AND ", conditions);

                sql += " ORDER BY id ASC";
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new {
                        _id       = reader["id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["id"]),
                        name      = reader["name"]?.ToString() ?? "",
                        price     = reader["price"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["price"]),
                        category  = reader["category"]?.ToString() ?? "",
                        thumbnail = reader["thumb_path"]?.ToString() ?? "",
                        modelPath = reader["model_url"]?.ToString() ?? "",
                        dimensions = new {
                            l = reader["length_cm"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["length_cm"]),
                            w = reader["width"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["width"]),
                            h = reader["height"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["height"])
                        }
                    });
                }
                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // GET /api/furnitures - 給篩選清單頁面用
        app.MapGet("/api/furnitures", (HttpContext http, DbService db) =>
        {
            var q = http.Request.Query;
            string? keyword   = q["keyword"];
            string? category  = q["category"];
            string? minWidth  = q["minWidth"];  string? maxWidth  = q["maxWidth"];
            string? minDepth  = q["minDepth"];  string? maxDepth  = q["maxDepth"];
            string? minHeight = q["minHeight"]; string? maxHeight = q["maxHeight"];
            string? minPrice  = q["minPrice"];  string? maxPrice  = q["maxPrice"];

            var list = new List<object>();
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                var sql = @"
                    SELECT id, name, category, width, length_cm, height,
                           price, description, image_url, thumb_path, model_url
                    FROM furnitures
                    WHERE 1=1";

                var conditions = new List<string>();
                using var cmd = new MySqlCommand();
                cmd.Connection = conn;

                if (!string.IsNullOrEmpty(keyword))
                {
                    conditions.Add("(name LIKE @keyword OR description LIKE @keyword)");
                    cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
                }
                if (!string.IsNullOrEmpty(category))
                {
                    conditions.Add("category = @category");
                    cmd.Parameters.AddWithValue("@category", category);
                }
                if (!string.IsNullOrEmpty(minWidth))  { conditions.Add("width >= @minWidth");      cmd.Parameters.AddWithValue("@minWidth",  decimal.Parse(minWidth)); }
                if (!string.IsNullOrEmpty(maxWidth))  { conditions.Add("width <= @maxWidth");      cmd.Parameters.AddWithValue("@maxWidth",  decimal.Parse(maxWidth)); }
                if (!string.IsNullOrEmpty(minDepth))  { conditions.Add("length_cm >= @minDepth");  cmd.Parameters.AddWithValue("@minDepth",  decimal.Parse(minDepth)); }
                if (!string.IsNullOrEmpty(maxDepth))  { conditions.Add("length_cm <= @maxDepth");  cmd.Parameters.AddWithValue("@maxDepth",  decimal.Parse(maxDepth)); }
                if (!string.IsNullOrEmpty(minHeight)) { conditions.Add("height >= @minHeight");    cmd.Parameters.AddWithValue("@minHeight", decimal.Parse(minHeight)); }
                if (!string.IsNullOrEmpty(maxHeight)) { conditions.Add("height <= @maxHeight");    cmd.Parameters.AddWithValue("@maxHeight", decimal.Parse(maxHeight)); }
                if (!string.IsNullOrEmpty(minPrice))  { conditions.Add("price >= @minPrice");      cmd.Parameters.AddWithValue("@minPrice",  decimal.Parse(minPrice)); }
                if (!string.IsNullOrEmpty(maxPrice))  { conditions.Add("price <= @maxPrice");      cmd.Parameters.AddWithValue("@maxPrice",  decimal.Parse(maxPrice)); }

                if (conditions.Any())
                    sql += " AND " + string.Join(" AND ", conditions);

                sql += " ORDER BY id ASC";
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new {
                        id          = reader["id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["id"]),
                        name        = reader["name"]?.ToString() ?? "",
                        category    = reader["category"]?.ToString() ?? "",
                        width       = reader["width"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["width"]),
                        length_cm   = reader["length_cm"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["length_cm"]),
                        height      = reader["height"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["height"]),
                        price       = reader["price"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["price"]),
                        description = reader["description"]?.ToString() ?? "",
                        image_url   = reader["image_url"]?.ToString() ?? "",
                        thumb_path  = reader["thumb_path"]?.ToString() ?? "",
                        download_url = reader["model_url"]?.ToString() ?? ""
                    });
                }
                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // GET /api/furnitures/{id}/model - 給 Unity 用，只回傳 modelUrl
        app.MapGet("/api/furnitures/{id:int}/model", (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT model_url FROM furnitures WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                var result = cmd.ExecuteScalar()?.ToString();

                if (string.IsNullOrEmpty(result))
                    return Results.NotFound(new { message = "此家具沒有模型檔案" });

                return Results.Ok(new {
                    modelUrl = result
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // GET /api/furnitures/{id} - 取得單一家具
        app.MapGet("/api/furnitures/{id:int}", (int id, DbService db) =>
        {
            try
            {
                using var conn = db.GetConnection();
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT id, name, category, width, length_cm, height,
                           price, description, image_url, thumb_path, model_url
                    FROM furnitures
                    WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return Results.NotFound(new { message = "找不到此家具" });

                return Results.Ok(new {
                    id          = Convert.ToInt32(reader["id"]),
                    name        = reader["name"]?.ToString() ?? "",
                    category    = reader["category"]?.ToString() ?? "",
                    width       = Convert.ToDecimal(reader["width"]),
                    length_cm   = Convert.ToDecimal(reader["length_cm"]),
                    height      = Convert.ToDecimal(reader["height"]),
                    price       = Convert.ToDecimal(reader["price"]),
                    description = reader["description"]?.ToString() ?? "",
                    image_url   = reader["image_url"]?.ToString() ?? "",
                    thumb_path  = reader["thumb_path"]?.ToString() ?? "",
                    download_url = reader["model_url"]?.ToString() ?? ""
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // GET /api/furnitures/categories - 取得所有類型（給前端下拉選單）
        app.MapGet("/api/furnitures/categories", (DbService db) =>
        {
            var list = new List<object>();
            try
            {
                using var conn = db.GetConnection();
                conn.Open();

                using var cmd = new MySqlCommand(
                    "SELECT DISTINCT category FROM furnitures WHERE category IS NOT NULL ORDER BY category", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    list.Add(new { name = reader["category"]?.ToString() });

                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }
}
