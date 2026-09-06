// src/components/PasswordStrength/PasswordStrength.js
// 密碼強度檢測條
import React from 'react';
import './PasswordStrength.css';

export const scorePassword = (pw) => {
  if (!pw) return 0;
  let score = 0;
  if (pw.length >= 8) score++;
  if (pw.length >= 12) score++;
  if (/[a-z]/.test(pw) && /[A-Z]/.test(pw)) score++;
  if (/\d/.test(pw)) score++;
  if (/[^a-zA-Z0-9]/.test(pw)) score++;
  return Math.min(score, 4); 
};

const LEVELS = [
  { label: '',     className: '' },
  { label: '太弱', className: 'strength-weak' },
  { label: '普通', className: 'strength-fair' },
  { label: '不錯', className: 'strength-good' },
  { label: '很強', className: 'strength-strong' },
];

const PasswordStrength = ({ password }) => {
  if (!password) return null;

  const score = Math.max(scorePassword(password), 1); 
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