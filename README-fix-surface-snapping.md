# `fix/surface-snapping`

## 功能目的
讓家具靠近指定表面時使用設定值吸附及對齊，離開距離後恢復自由操作。

## 修改範圍
- `LateUpdate` 實際呼叫吸附邏輯。
- 使用 `snapDistance`、`offsetFromWall` 與 `surfaceLayer`。
- 排除 Trigger，並安全處理沒有 Rigidbody 的物件。
- 動態 Rigidbody 使用 MovePosition／MoveRotation。

## 驗收條件
1. 進入吸附距離時貼齊表面。
2. 離開距離後不再強制吸附。
3. Inspector 設定值確實生效。
4. 沒有 Rigidbody 時不會發生 NullReference。

## 建議測試
- 不同方向牆面、牆角、快速靠近與拉離、開關 snapEnabled。

## 分支狀態
功能已完成修改，等待 Unity Editor 與 Quest 實機測試。
