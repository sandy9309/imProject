// src/components/Guide/GuideTour.js
// 📖 首次登入自動跳出的分頁式導覽(可跳過,看過就不再顯示)
import React, { useState, useEffect } from 'react';
import {
  X, ChevronLeft, ChevronRight, Layout, ShoppingCart,
  Save, Send, Glasses, Move3d, Camera,
} from 'lucide-react';
import { GUIDE_STEPS } from './guideSteps';
import './Guide.css';

const ICONS = { Layout, ShoppingCart, Save, Send, Glasses, Move3d, Camera };

const GuideTour = ({ forceOpen = false, onClose }) => {
  const [open, setOpen] = useState(false);
  const [step, setStep] = useState(0);

  // 首次登入自動跳出:登入後且沒看過導覽就顯示
  useEffect(() => {
    if (forceOpen) {
      setOpen(true);
      setStep(0);
      return;
    }
    const isLoggedIn = !!localStorage.getItem('token');
    const seen = localStorage.getItem('guide_seen') === 'true';
    if (isLoggedIn && !seen) setOpen(true);
  }, [forceOpen]);

  const close = () => {
    localStorage.setItem('guide_seen', 'true');
    setOpen(false);
    setStep(0);
    onClose?.();
  };

  // 鍵盤操作
  useEffect(() => {
    if (!open) return;
    const onKey = (e) => {
      if (e.key === 'Escape') close();
      if (e.key === 'ArrowLeft') setStep(s => Math.max(0, s - 1));
      if (e.key === 'ArrowRight') setStep(s => Math.min(GUIDE_STEPS.length - 1, s + 1));
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  if (!open) return null;

  const current = GUIDE_STEPS[step];
  const Icon = ICONS[current.icon] || Layout;
  const isFirst = step === 0;
  const isLast = step === GUIDE_STEPS.length - 1;

  return (
    <div className="guide-overlay" onClick={close}>
      <div className="guide-box" onClick={e => e.stopPropagation()}>
        <button className="guide-close" onClick={close} aria-label="關閉導覽">
          <X size={20} />
        </button>

        <div className="guide-step-badge">STEP {step + 1} / {GUIDE_STEPS.length}</div>

        <div className="guide-icon"><Icon size={38} /></div>

        <h2 className="guide-title">{current.title}</h2>
        <p className="guide-subtitle">{current.subtitle}</p>
        <p className="guide-desc">{current.desc}</p>
        {current.tip && <p className="guide-tip">{current.tip}</p>}

        {/* 進度圓點 */}
        <div className="guide-dots">
          {GUIDE_STEPS.map((_, i) => (
            <button
              key={i}
              className={`guide-dot ${i === step ? 'active' : ''}`}
              onClick={() => setStep(i)}
              aria-label={`第 ${i + 1} 步`}
            />
          ))}
        </div>

        <div className="guide-actions">
          <button className="guide-skip" onClick={close}>
            {isLast ? '' : '略過導覽'}
          </button>

          <div className="guide-nav">
            {!isFirst && (
              <button className="guide-btn-prev" onClick={() => setStep(s => s - 1)}>
                <ChevronLeft size={16} /> 上一步
              </button>
            )}
            {isLast ? (
              <button className="guide-btn-next" onClick={close}>開始使用!</button>
            ) : (
              <button className="guide-btn-next" onClick={() => setStep(s => s + 1)}>
                下一步 <ChevronRight size={16} />
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default GuideTour;