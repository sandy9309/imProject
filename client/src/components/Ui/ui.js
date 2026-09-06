// src/components/Ui/ui.js
// 共用提示

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