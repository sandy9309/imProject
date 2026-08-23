# `fix/model-collider`

## 功能目的

修正高面數家具合併成單一 Convex MeshCollider 時，可能發生烘焙失敗、碰撞體過度簡化及執行效能不穩定的問題。

## 修改範圍

- `ModelLoader.cs` 下載模型後的 Collider 建立流程。
- 依所有 Renderer 的實際範圍建立根物件 BoxCollider。
- XR Grab Interactable 與 Meta ColliderSurface 改綁新的 Collider。
- 重複載入時重用根物件既有 BoxCollider。

## 不在此分支處理

- 模型下載失敗。
- 家具互撞、牆面吸附與位置儲存。
- 複雜形狀的多 Collider 或 Convex Decomposition。

## 驗收條件

1. 高面數模型不需要建立 Convex MeshCollider。
2. BoxCollider 可以包覆模型的所有 Renderer。
3. XR 射線及抓取元件使用新 Collider。
4. 沒有 Renderer 的模型會顯示錯誤且不建立無效 Collider。

## 建議測試案例

- 一般桌椅模型。
- 旋轉或縮放過的模型子物件。
- 高面數 GLB 模型。
- 沒有 Renderer 的空模型。

## 分支狀態

功能程式已完成修改，等待 Unity Editor 與 Quest 實機測試。
