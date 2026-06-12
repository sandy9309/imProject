# Changelog

## [2026-06-05]

### 修改 `Endpoints/FurnitureEndpoints.cs`

新增一個給 Unity 使用的端點：

| 方法 | 路徑 | 說明 |
|---|---|---|
| GET | `/api/furnitures/{id}/model` | 只回傳單一家具的 `modelUrl`，找不到或無模型回傳 404 |

- 路由加在 `/api/furnitures/{id:int}` 之前，避免路由衝突

### 修改 `Endpoints/ProjectEndpoints.cs`

新增一個給 Unity 使用的端點：

| 方法 | 路徑 | 說明 |
|---|---|---|
| GET | `/api/projects/{id}/models` | 取得專案內所有家具的 `model_url`，回傳含位置資訊的陣列 |

邏輯：
1. 查 `projects.items`（JSON 字串），project 不存在回傳 404
2. 解析 items 陣列，取出每個物件的 `furniture_id`
3. 以 `IN` 查詢 `furnitures.model_url`
4. 依 items 原始順序回傳，無 `model_url` 的家具略過

回傳格式：
```json
{
  "furnitures": [
    { "url": "https://...", "x": 0, "y": 0, "z": 0 }
  ]
}
```

---

## [2026-05-20]

### 資料庫欄位變更 — `furnitures` 表

| 舊欄位名稱 | 新欄位名稱 |
|---|---|
| `f_id` | `id` |
| `depth` | `length_cm` |
| `model_path` | `model_url` |

### 新增資料表 — `cart_items`

```sql
CREATE TABLE `cart_items` (
  `id`         int       NOT NULL AUTO_INCREMENT,
  `user_id`    int       NOT NULL,
  `product_id` int       NOT NULL,
  `added_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  FOREIGN KEY (`user_id`)    REFERENCES `users`      (`user_id`) ON DELETE CASCADE,
  FOREIGN KEY (`product_id`) REFERENCES `furnitures` (`id`)      ON DELETE CASCADE
);
```

### 修改 `FurnitureEndpoints.cs`

- SELECT、WHERE、ORDER BY 裡的 `f_id` 全數改為 `id`
- SELECT、WHERE 裡的 `depth` 全數改為 `length_cm`
- SELECT 裡的 `model_path` 全數改為 `model_url`
- `reader["f_id"]` → `reader["id"]`（3 處）
- `reader["depth"]` → `reader["length_cm"]`（3 處），回傳屬性 `depth` 同步改為 `length_cm`
- `reader["model_path"]` → `reader["model_url"]`（6 處，含 `IsNullOrEmpty` 檢查與字串插值）

### 新增 `Endpoints/CartEndpoints.cs`

實作購物車 API，共四個端點：

| 方法 | 路徑 | 說明 |
|---|---|---|
| GET | `/api/cart?userId={id}` | 取得購物車，JOIN `furnitures` 回傳家具完整資料 |
| POST | `/api/cart` | 加入家具（Body: `user_id`, `product_id`），重複加入回傳 409 |
| DELETE | `/api/cart/{id}` | 移除單筆（`cart_items.id`） |
| DELETE | `/api/cart?userId={id}` | 清空該使用者的購物車 |

GET 回傳欄位：`id`（cart_items.id）、`product_id`、`name`、`category`、`width`、`length_cm`、`height`、`price`、`image_url`、`model_url`

### 修改 `Program.cs`

新增一行路由註冊：

```csharp
app.MapCartEndpoints();      // 購物車模組
```

### 修改 `schema.sql`

- `furnitures` 表欄位同步更新（`f_id`→`id`、`depth`→`length_cm`、`model_path`→`model_url`）
- 新增 `cart_items` 表定義
