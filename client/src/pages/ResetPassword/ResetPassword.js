// src/pages/ResetPassword/ResetPassword.js
// (方案A專用)使用者點信件裡的重設連結會到這頁:
// 網址格式:/reset-password?token=xxxxx
import React, { useState, useMemo } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Lock, Eye, EyeOff, KeyRound } from 'lucide-react';
import { showToast } from '../../components/Ui/ui';
import PasswordStrength from '../../components/PasswordStrength/PasswordStrength';
import '../ForgotPassword/ForgotPassword.css';

const API_BASE = 'http://163.13.202.116:5050';

const ResetPassword = () => {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);
  const [form, setForm] = useState({ newPassword: '', confirm: '' });
  const [showPw, setShowPw] = useState({ next: false, confirm: false });
  const [failMessage, setFailMessage] = useState('');

  // 從網址 query string 取出 token
  const token = useMemo(
    () => new URLSearchParams(window.location.search).get('token') || '',
    []
  );

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!token) {
      showToast('重設連結無效,請重新申請', 'error');
      return;
    }
    if (form.newPassword.length < 6) {
      showToast('新密碼至少需要 6 個字元', 'error');
      return;
    }
    if (form.newPassword !== form.confirm) {
      showToast('兩次輸入的新密碼不一致', 'error');
      return;
    }
    try {
      setLoading(true);
      const res = await fetch(`${API_BASE}/api/reset-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, password: form.newPassword }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        // 400:token 無效/過期 → 顯示後端 message + 引導重新申請
        setFailMessage(data.message || '連結已失效,請重新申請');
        return;
      }
      showToast(data.message || '✅ 密碼已重設,請用新密碼登入', 'success');
      navigate('/login');
    } catch (err) {
      console.error('重設失敗:', err);
      showToast(`重設失敗:${err.message}`, 'error');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="forgot-container">
      <div className="forgot-card">
        <h2><KeyRound size={22} /> 設定新密碼</h2>

        {failMessage ? (
          <div className="forgot-sent">
            <p>⚠️ {failMessage}</p>
            <p className="forgot-hint">重設連結可能已過期或已被使用,請重新申請一次。</p>
            <Link className="forgot-back" to="/forgot-password">重新申請</Link>
          </div>
        ) : !token ? (
          <div className="forgot-sent">
            <p>⚠️ 連結無效或已遺失</p>
            <p className="forgot-hint">請回到忘記密碼頁重新申請一次。</p>
            <Link className="forgot-back" to="/forgot-password">重新申請</Link>
          </div>
        ) : (
          <form onSubmit={handleSubmit}>
            <div className="form-group">
              <label><Lock size={16} /> 新密碼</label>
              <div className="pw-input-wrap">
                <input
                  type={showPw.next ? 'text' : 'password'}
                  placeholder="至少 6 個字元"
                  value={form.newPassword}
                  onChange={e => setForm({ ...form, newPassword: e.target.value })}
                  required
                />
                <button
                  type="button"
                  className="pw-toggle-btn"
                  onClick={() => setShowPw({ ...showPw, next: !showPw.next })}
                >
                  {showPw.next ? <EyeOff size={18} /> : <Eye size={18} />}
                </button>
              </div>
              <PasswordStrength password={form.newPassword} />
            </div>
            <div className="form-group">
              <label><Lock size={16} /> 再輸入一次新密碼</label>
              <div className="pw-input-wrap">
                <input
                  type={showPw.confirm ? 'text' : 'password'}
                  placeholder="請再次輸入新密碼"
                  value={form.confirm}
                  onChange={e => setForm({ ...form, confirm: e.target.value })}
                  required
                />
                <button
                  type="button"
                  className="pw-toggle-btn"
                  onClick={() => setShowPw({ ...showPw, confirm: !showPw.confirm })}
                >
                  {showPw.confirm ? <EyeOff size={18} /> : <Eye size={18} />}
                </button>
              </div>
            </div>
            <button className="forgot-submit" type="submit" disabled={loading}>
              {loading ? '重設中...' : '確認重設'}
            </button>
          </form>
        )}
      </div>
    </div>
  );
};

export default ResetPassword;