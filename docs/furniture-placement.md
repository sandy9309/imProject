# 家具防穿牆

本次實作分支：`fix/project-defect-fixes`。

## 優先目標與規則

- 沿用 Meta MRUK 範例的房間資料流程：載入空間資料，從牆面錨點建立碰撞幾何，再限制虛擬物件移動。
- `SceneAutoScanner` 將 MRUK 的牆面建立為有厚度的 `BoxCollider`，也支援既有手動牆面流程。MRUK 牆面跟隨錨點的座標更新。
- `FurniturePlacementController` 統一提交目標，`FurnitureWallCollisionGuard` 負責平移與旋轉驗證。採世界座標定向包圍盒，使用分離軸的連續掃掠，檢查整段平移路徑。旋轉保留輸入角度，以位移修正避開牆與其他模擬家具，不再搜尋替代角度。
- 牆面額外留 1 公分，模擬家具彼此留 0.5 公分；牆體本身厚度另計。這些是程式緩衝，不能保證真實掃描的精度。
- 模擬家具彼此阻擋；不使用共同父物件來排除碰撞，因此同一個 ModelLoader 下的家具仍然互斥。
- 掃描家具不阻擋模擬家具。只有與其掃描範圍重疊的模型變成 35% 不透明度，離開後恢復原本的材質。半透明狀態使用保留基本顏色和貼圖的獨立 unlit 材質，不更改共用原始材質。
- 吸附停用，不新增紅色預覽或沿牆滑動。

## 輸入與位置更新的集中入口

`FurniturePlacementController` 是唯一的家具位置更新入口，統一管理抓取／放手、搖桿推拉、旋轉請求、放手後固定和自動儲存：

```text
Meta 抓取目標／XRI 手把姿態／搖桿／旋轉按鈕
                      ↓
FurniturePlacementController 組合目標位置與角度
                      ↓
FurnitureWallCollisionGuard.ResolvePose 驗證整段平移與最終角度
                      ↓
FurniturePlacementController.CommitPose 套用一次
                      ↓
外觀與根碰撞盒一起移動，更新合法位置與透明度
```

- Meta SDK 寫入獨立、沒有 Renderer 的 `Grab Input`，不再直接搬動模型或物理根物件。
- XRI 的直接位置／旋轉／縮放追蹤及拋擲關閉，由主控制器讀取選取手把的 attach pose。家具採直立姿態、單一選取手把控制，尺寸維持不變。
- 搖桿速度與旋轉速度在 `FurniturePlacementController` 調整；舊 Prefab 上的數值於初始化遷移。
- `RequestPose`、`RequestGrabbed`、`RequestRotation` 只記錄請求，不即刻更新模型。`ProcessFrame` 統一處理每幀，包含放手那一幀。
- `FurnitureWallCollisionGuard` 保留碰撞計算及真實家具重疊判斷，不再有 `LateUpdate`，也不寫 Transform 或 Rigidbody 的位置。
- `ObjectJoystickControl`、`VRFurnitureGrab`、`FurnitureSnapping` 保留原類別與 `.meta`，相容既有 Prefab／UnityEvent，僅初始化或轉送請求。舊掃掠、回退、模型拆卸重掛、分散的固定協程已移除。
- `SurfaceSnapper` 只保留序列化設定；目前吸附功能停用。
- 放手後既有重力落地仍由 Unity 物理提供候選位置，再走同一個驗證入口，約一秒後固定。
- `ModelLoader` 仍負責載入、建立模型外框碰撞盒與初始化控制器。牆面未就緒則拒絕生成；出生位置衝突時搜尋附近合法位置。

## 驗證

Unity 2022.3.62f3 的選單 `Tools > Furniture > Verify placement constraints`，或 batchmode 執行 `FurniturePlacementVerification.Run`。

檢查包含薄牆高速掃掠、遠離牆面、靠牆旋轉、自由空間旋轉、放手根座標修正、初始穿牆位置修復、兩件抓取家具互斥、刪除家具後釋放空間，以及重疊／離開真實家具範圍時切換材質。

電腦端測試以模擬牆與掃描家具包圍盒驗證，不能取代 Quest 實測。實機請確認：

1. 完成房間掃描並授予空間資料權限，確認牆面載入，再載入家具。
2. 對真實牆慢推、快速推、旋轉長桌，放手後確認不越牆。
3. 讓兩件模擬家具互相推，確認不能重疊。
4. 將一件模擬家具移進已掃描的真實桌子，再移出，確認只有該模型變半透明並恢復。

未掃描、沒有家具語意範圍，或只有手動牆面的房間，無法判斷真實家具重疊。模型外框是保守近似；椅腳間等空洞也視為占用空間。完成旋轉時以模型外框尋找合法角度，細小空洞不納入判斷。

## 官方參考

- [Meta MRUK samples（含 Bouncing Ball）](https://github.com/oculus-samples/Unity-MRUtilityKitSample)
- [Meta Project Phanto](https://github.com/oculus-samples/Unity-Phanto)

沿用的是其 MRUK 房間資料與碰撞幾何方向；家具抓取限制為本專案的延伸實作，並非直接把球換成家具即可得到的功能。


## 房間啟動、重掃與表面標示

啟動時先嘗試載入頭顯已儲存的房間，但不因失敗就自動進入手動建牆。無論是否載入成功，都先顯示選單：

- **A：使用目前房間**，僅在有可用牆面時啟用。
- **B：掃描／重新掃描**，呼叫 Meta 官方 `OVRScene.RequestSpaceSetup()`。
- **X：手動建牆**，先顯示現有可用牆數及取代提醒，再按 A 確認才進入加牆角；B 返回選單。返回系統掃描或切換選單後，必須先放開按鍵，避免上一個畫面的按鍵延續到下一步。

選單顯示 MRUK 載入結果、空間資料權限與 WALL 數量；地板與天花板數量及空間中的所有表面文字標示暫時隱藏。載入結果如 `NoScenePermission`、`NoRoomsFound`、`DiscoveryOngoing` 可用來區分權限、找不到房間與探索尚在進行；不能只看到加牆角就斷言頭顯沒有掃描。

MRUK 牆面、地板、天花板的空間文字標示暫時隱藏；選單保留牆壁數量，碰撞幾何照常使用。

掃描取消、失敗或成功後，均回到選單確認，不自動開始手動建牆。既有長按 Y 重設手動牆面的流程仍保留。

程式修改需重新 Build 並安裝到 Quest 才能在頭顯看到。電腦測試不會自動更新已安裝的 APK。

## 真實物件不承托模擬家具

物理接觸只保留已建立的牆、MRUK 地板／手動地板及其他模擬家具。模型所有子碰撞盒使用 Virtual Furniture 層及 Collider 的接觸層排除設定，只接受 Placement Geometry 和其他 Virtual Furniture；不依賴附近碰撞查詢是否找到掃描物件。因此沒有家具標籤的鞋架、整體空間網格或手部碰撞盒也不應把模型托起或推動。手動房間另建立沒有外觀的地板碰撞盒，牆角依序連成的周界仍負責防穿牆。

透明度仍只使用具家具語意的範圍。未標示／UNKNOWN 的物件可重疊而不變透明，這是目前允許的行為。天花板量測及手動高度校正暫不處理。

回歸測試另外在隔離的物理場景模擬家具落下，確認穿過未標示平台後停在地板，而非平台上。這項測試與外觀重疊測試分開驗證，避免只確認材質卻遺漏物理承托。


## 保留角度與跟隨手把

模型保留使用者要求的角度；碰到牆或其他模擬家具時，沿上一個合法位置所在側退開。平移使用目前角度作連續掃掠，碰到阻擋後保留切線方向，讓家具可沿牆移動。若狹窄空間完全無法容納該角度，保留上一個合法姿態，不把家具傳到牆外或自動轉成別的方向。

搖桿採 0.2 死區與主軸判斷：前後推拉時不再同時觸發左右旋轉。XRI 的抓取位置偏移採世界座標，避免轉手腕時位置繞著手把跑；位置與角度仍統一經過 FurniturePlacementController 更新。

掃描原始網格不作接觸面；只有專用牆、平坦地板與模擬家具可阻擋。已標示真實家具僅用來判斷半透明。
