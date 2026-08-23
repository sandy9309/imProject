# `fix/project-switching`

## 功能目的
安全切換專案，避免刪除 UI、`idDisplay` 空值錯誤，以及舊專案回應覆蓋新專案。

## 修改範圍
- 切換時只清除具有 `FurnitureTag` 的家具。
- 不再直接存取可能為 null 的 `idDisplay.transform`。
- 每次請求加入版本號，忽略較舊專案的逾期回應。
- 切換時重設家具清單與選擇索引。

## 驗收條件
1. 未綁定 idDisplay 時切換不會 NullReference。
2. UI 與管理物件不會被當成家具刪除。
3. 快速切換時舊回應不會覆蓋新專案。

## 建議測試
- idDisplay 未綁定、下載中切換、快速來回切換兩個專案。

## 分支狀態
功能已完成修改，等待後端整合與 Quest 實機測試。
