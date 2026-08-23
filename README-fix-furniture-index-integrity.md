# 家具 index 唯一性修正

## 解決的問題

原本 `GET /api/projects/{id}/models` 使用家具在 `projects.items` JSON 陣列中的位置作為 `index`。這只能保證單次回傳中的數字不重複；網頁重新排列或重建清單後，同一件家具可能得到不同 index。

原本 `PUT /api/projects/{id}/positions` 也直接以 `items[pos.index]` 更新資料，因此 Quest 若持有舊清單，可能把位置寫到另一件家具。

眼鏡內讓家具消失只會刪除 Unity 場景物件，不會刪除後端資料，因此該操作本身不會改變後端 index。本修正主要防止網頁更新、排序、舊資料及異常 API 資料造成錯誤辨識。

## 修正內容

- 後端在每筆 `projects.items` JSON 中永久保存整數 `index`。
- 建立新家具實例時配置未使用的下一個 index。
- 更新既有專案時優先沿用原有 index。
- GET models 回傳保存的 index，不再固定使用陣列位置。
- PUT positions 依保存的 index 搜尋家具，不再直接存取 `items[pos.index]`。
- 舊專案缺少 index 時，暫時以原陣列位置相容，首次位置更新或清單更新後補存 index。
- Unity 使用 `HashSet<int>` 檢查 API 結果；index 為負數或同一專案出現重複值時，拒絕載入並在 Console／debugText 顯示錯誤。

## 驗收方式

1. 專案加入兩張相同型號家具，確認 API 回傳不同 index。
2. 在網頁調整其他家具或重新排序，再呼叫 models API，確認原家具 index 不變。
3. 在 Quest 移動其中一張家具，確認 positions API 只更新相同 index 的 JSON 物件。
4. 人工建立重複 index 的 API 回應，確認 Unity 顯示「家具資料拒絕載入」且不生成家具。

## 資料範例

```json
{
  "index": 3,
  "furniture_id": 25,
  "x": 1.2,
  "y": 0,
  "z": 2.1,
  "ry": 90,
  "isPlaced": true
}
```

`furniture_id` 代表家具型號，可以重複；`index` 代表專案內的家具實例，必須唯一。
