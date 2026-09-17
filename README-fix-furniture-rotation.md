# `fix/furniture-rotation`

## 功能目的
提供保持直立、固定角度且不會旋轉進其他家具內的旋轉操作。

## 修改範圍
- 旋轉角度改為 Inspector 可設定的 `rotationStep`。
- 強制只保留 Y 軸旋轉。
- 同步 Rigidbody 並清除角速度。
- 旋轉後若重疊其他 FurnitureTag，回復原始角度。

## 驗收條件
1. 每次旋轉符合設定角度。
2. 家具不產生 X、Z 傾斜。
3. 不可旋轉進另一件家具。
4. 沒有 Rigidbody 或 Collider 時仍不會拋出例外。

## 建議測試
- 空曠區連續旋轉、家具旁旋轉、不同 rotationStep。

## 分支狀態
功能已完成修改，等待 Unity Editor 與 Quest 實機測試。
