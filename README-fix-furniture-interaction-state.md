# `fix/furniture-interaction-state`

## 功能目的
統一家具載入、放置、抓取、驗證與固定時的 Rigidbody 狀態。

## 新增狀態
- `Loading`：下載組裝中，關閉重力並設為 Kinematic。
- `Placed`：可受物理影響的已放置狀態。
- `Grabbed`：抓取中，解除限制並關閉重力。
- `Validating`：放手座標同步期間暫停物理。
- `Frozen`：穩定後鎖定全部位移與旋轉。

## 修改範圍
- 新增 `FurnitureInteractionStateController`。
- ModelLoader 在下載前後設定 Loading／Placed。
- ObjectJoystickControl 在抓取與放手流程切換狀態。
- 延遲固定完成後統一進入 Frozen。

## 驗收條件
1. 家具下載時不會被物理彈飛。
2. 抓取、放手、再次抓取可重複進行。
3. 放手同步期間不會產生殘留速度。
4. 穩定後家具固定，重新抓取時會解除限制。

## 建議測試
- 下載完成、連續抓放、放手一秒內再次抓取及固定後再抓取。

## 分支狀態
功能已完成修改，等待 Unity Editor 與 Quest 實機測試。
