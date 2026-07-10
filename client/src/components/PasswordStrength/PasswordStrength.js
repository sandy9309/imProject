// src/components/PasswordStrength/PasswordStrength.js
// 密碼強度檢測條:輸入密碼時即時顯示安全性等級
import React from 'react';
import './PasswordStrength.css';

// 計算密碼強度分數(0~5)
export const scorePassword = (pw) => {
  if (!pw) return 0;
  let score = 0;
  if (pw.length >= 8) score++;
  if (pw.length >= 12) score++;
  if (/[a-z]/.test(pw) && /[A-Z]/.test(pw)) score++;
  if (/\d/.test(pw)) score++;
  if (/[^a-zA-Z0-9]/.test(pw)) score++;
  return Math.min(score, 4); // 收斂到 0~4 級
};

const LEVELS = [
  { label: '',     className: '' },
  { label: '太弱', className: 'strength-weak' },
  { label: '普通', className: 'strength-fair' },
  { label: '不錯', className: 'strength-good' },
  { label: '很強', className: 'strength-strong' },
];

const PasswordStrength = ({ password }) => {
  // 沒輸入任何東西時不顯示,避免畫面雜訊
  if (!password) return null;

  const score = Math.max(scorePassword(password), 1); // 有輸入至少顯示第 1 級
  const level = LEVELS[score];

  return (
    <div className={`pw-strength ${level.className}`}>
      <div className="pw-strength-bars">
        {[1, 2, 3, 4].map(i => (
          <span
            key={i}
            className={`pw-strength-bar ${i <= score ? 'filled' : ''}`}
          />
        ))}
      </div>
      <span className="pw-strength-label">{level.label}</span>
    </div>
  );
};

export default PasswordStrength;