// src/components/Ui/ui.js
// 全站共用的提示系統:任何檔案 import 這兩個函式即可使用
//   showToast('儲存成功', 'success')   → 右上角輕提示,自動消失
//   await showConfirm({ message: '確定要刪除嗎?' }) → 確認彈窗,回傳 true/false

export const showToast = (message, type = 'info') => {
  window.dispatchEvent(
    new CustomEvent('ui-toast', {
      detail: { message, type, id: Date.now() + Math.random() },
    })
  );
};

export const showConfirm = ({
  title = '確認操作',
  message = '',
  confirmText = '確定',
  cancelText = '取消',
  danger = false,
} = {}) => {
  return new Promise((resolve) => {
    const id = Date.now() + Math.random();
    const handler = (e) => {
      if (e.detail.id !== id) return;
      window.removeEventListener('ui-confirm-result', handler);
      resolve(e.detail.result);
    };
    window.addEventListener('ui-confirm-result', handler);
    window.dispatchEvent(
      new CustomEvent('ui-confirm', {
        detail: { id, title, message, confirmText, cancelText, danger },
      })
    );
  });
};