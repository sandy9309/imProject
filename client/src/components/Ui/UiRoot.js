// src/components/Ui/UiRoot.js
// 全站唯一的提示渲染中心:在 App.js 掛一次 <UiRoot /> 即可
import React, { useState, useEffect } from 'react';
import { CheckCircle2, AlertCircle, Info } from 'lucide-react';
import './UiRoot.css';

const TOAST_DURATION = 2800;

const UiRoot = () => {
  const [toasts, setToasts] = useState([]);
  const [confirmBox, setConfirmBox] = useState(null);

  useEffect(() => {
    const onToast = (e) => {
      const toast = e.detail;
      setToasts(prev => [...prev, toast]);
      setTimeout(() => {
        setToasts(prev => prev.filter(t => t.id !== toast.id));
      }, TOAST_DURATION);
    };
    const onConfirm = (e) => setConfirmBox(e.detail);

    window.addEventListener('ui-toast', onToast);
    window.addEventListener('ui-confirm', onConfirm);
    return () => {
      window.removeEventListener('ui-toast', onToast);
      window.removeEventListener('ui-confirm', onConfirm);
    };
  }, []);

  const answer = (result) => {
    if (!confirmBox) return;
    window.dispatchEvent(
      new CustomEvent('ui-confirm-result', {
        detail: { id: confirmBox.id, result },
      })
    );
    setConfirmBox(null);
  };

  const iconFor = (type) => {
    if (type === 'success') return <CheckCircle2 size={18} />;
    if (type === 'error') return <AlertCircle size={18} />;
    return <Info size={18} />;
  };

  return (
    <>
      {/* ── 輕提示(右上角堆疊,自動消失) ── */}
      <div className="toast-stack">
        {toasts.map(t => (
          <div key={t.id} className={`toast toast-${t.type}`}>
            {iconFor(t.type)}
            <span>{t.message}</span>
          </div>
        ))}
      </div>

      {/* ── 確認彈窗 ── */}
      {confirmBox && (
        <div className="confirm-overlay" onClick={() => answer(false)}>
          <div className="confirm-box" onClick={e => e.stopPropagation()}>
            <h3 className="confirm-title">{confirmBox.title}</h3>
            {confirmBox.message && (
              <p className="confirm-message">{confirmBox.message}</p>
            )}
            <div className="confirm-actions">
              <button className="confirm-cancel" onClick={() => answer(false)}>
                {confirmBox.cancelText}
              </button>
              <button
                className={`confirm-ok ${confirmBox.danger ? 'danger' : ''}`}
                onClick={() => answer(true)}
                autoFocus
              >
                {confirmBox.confirmText}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default UiRoot;