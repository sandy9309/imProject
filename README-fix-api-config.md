# `fix/api-config`

## 功能目的
集中管理 ModelLoader 使用的後端網址與 timeout，移除截圖上傳內寫死的伺服器網址。

## 修改範圍
- 新增 `BuildProjectApiUrl` 統一組合專案 API。
- models、positions、media 共用 `apiBaseUrl`。
- 所有 ModelLoader 網路請求共用 `requestTimeoutSeconds`。
- 自動處理網址斜線。

## 驗收條件
1. 更換 apiBaseUrl 後三類請求都使用新網址。
2. Base URL 有無結尾斜線都可正確組合。
3. 無回應時依設定秒數 timeout。

## 建議測試
- 開發／正式網址、有無結尾斜線、錯誤網址及慢速網路。

## 注意事項
Quest 若使用 HTTP，仍須確認 Android cleartext 設定；正式環境建議 HTTPS。

## 分支狀態
功能已完成修改，等待後端整合與 Quest 實機測試。
