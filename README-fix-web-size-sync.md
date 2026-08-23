# `fix/web-size-sync`

## 功能目的
完成網頁尺寸字串到 Unity 家具實際尺寸、Collider 與自動儲存的同步流程。

## 修改範圍
- 使用 TryParse 驗證 `x,y,z`。
- 支援公分轉 Unity 公尺。
- 依 Renderer 實際 Bounds 計算縮放比例。
- 縮放後重算 AutoFitCollider 並觸發自動儲存。

## 驗收條件
1. 合法尺寸可正確套用。
2. 非數字、缺少欄位、零或負值不會拋出例外。
3. 公分與公尺設定結果正確。
4. 有 AutoFitCollider 時會同步更新。

## Inspector 設定
- `targetFurniture`：指定要縮放的家具。
- `inputUsesCentimeters`：網頁輸入為公分時保持開啟。

## 建議測試
- `120,60,75`、小數、錯誤文字、零值、負值與公尺模式。

## 分支狀態
功能已完成修改，等待 Web 串接與 Unity 測試。
