# `fix/wall-collision`

## 功能目的
限制搖桿移動家具時只有 MR 牆面與指定固定障礙物會阻擋移動。

## 修改範圍
- `ObjectJoystickControl` 新增 `obstacleLayers`。
- SweepTest 結果依 LayerMask 過濾。
- 保留自身、玩家、控制器、地板與天花板排除條件。

## 驗收條件
1. 指定 Layer 的牆面可阻止家具穿透。
2. 未指定 Layer 的物件不會錯誤阻擋家具。
3. 玩家、手和控制器不會阻止家具移動。

## 建議測試
- 正面撞牆、斜向撞牆、牆角及切換 obstacleLayers。

## Inspector 設定
將 `obstacleLayers` 設為 MRUK 牆面與不可穿越障礙物所在 Layer。

## 分支狀態
功能已完成修改，等待 Unity Editor 與 Quest 實機測試。
