// src/pages/ForgotPassword/ForgotPassword.js
// 忘記密碼頁:支援兩種模式,改 RESET_MODE 一行即可切換
//   'simple' → 方案B:驗證 Email+手機 後直接重設(不用寄信)
//   'email'  → 方案A:寄重設連結到信箱(需後端接寄信服務)
import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Mail, Phone, Lock, Eye, EyeOff, KeyRound } from 'lucide-react';
import { showToast } from '../../components/Ui/ui';
import PasswordStrength from '../../components/PasswordStrength/PasswordStrength';
import './ForgotPassword.css';

const API_BASE = 'http://163.13.202.116:5050';

// 🔧 切換重設模式:'simple'(驗證手機) 或 'email'(寄信)
const RESET_MODE = 'email';

const ForgotPassword = () => {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);
  const [emailSent, setEmailSent] = useState(false);
  const [serverMessage, setServerMessage] = useState('');

  const [form, setForm] = useState({
    email: '',
    phone: '',
    newPassword: '',
    confirm: '',
  });
  const [showPw, setShowPw] = useState({ next: false, confirm: false });

  const handleChange = (e) => {
    setForm({ ...form, [e.target.name]: e.target.value });
  };

  // ── 方案A:寄重設信 ─────────────────────────────────────────
  const handleEmailRequest = async (e) => {
    e.preventDefault();
    if (!form.email.trim()) {
      showToast('請輸入註冊時使用的 Email', 'error');
      return;
    }
    try {
      setLoading(true);
      const res = await fetch(`${API_BASE}/api/forgot-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: form.email.trim() }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        throw new Error(data.message || '申請失敗');
      }
      // 後端不論 email 是否存在都回 200 + 同一句 message(防帳號猜測),直接顯示它
      setServerMessage(data.message || '重設連結已寄出,請查收信箱');
      setEmailSent(true);
    } catch (err) {
      console.error('申請重設失敗:', err);
      showToast(`申請失敗:${err.message}`, 'error');
    } finally {
      setLoading(false);
    }
  };

  // ── 方案B:驗證 Email+手機 後直接重設 ───────────────────────
  const handleSimpleReset = async (e) => {
    e.preventDefault();
    if (!form.email.trim() || !form.phone.trim()) {
      showToast('請填寫 Email 與註冊時的手機號碼', 'error');
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
      const res = await fetch(`${API_BASE}/api/password-reset/simple`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: form.email.trim(),
          phone: form.phone.trim(),
          newPassword: form.newPassword,
        }),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Email 或手機號碼驗證失敗');
      }
      showToast('✅ 密碼已重設,請用新密碼登入', 'success');
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
        <h2><KeyRound size={22} /> 忘記密碼</h2>

        {/* ══ 方案A:寄信模式 ══ */}
        {RESET_MODE === 'email' && (
          emailSent ? (
            <div className="forgot-sent">
              <p>📮 {serverMessage}</p>
              <p className="forgot-hint">
                (連結 30 分鐘內有效,沒收到請檢查垃圾郵件)
              </p>
              <Link className="forgot-back" to="/login">返回登入</Link>
            </div>
          ) : (
            <form onSubmit={handleEmailRequest}>
              <p className="forgot-hint">
                輸入註冊時使用的 Email,我們會寄一封重設密碼的連結給你。
              </p>
              <div className="form-group">
                <label><Mail size={16} /> Email</label>
                <input
                  name="email"
                  type="email"
                  placeholder="請輸入註冊 Email"
                  value={form.email}
                  onChange={handleChange}
                  required
                />
              </div>
              <button className="forgot-submit" type="submit" disabled={loading}>
                {loading ? '寄送中...' : '寄送重設連結'}
              </button>
              <Link className="forgot-back" to="/login">返回登入</Link>
            </form>
          )
        )}

        {/* ══ 方案B:驗證手機模式 ══ */}
        {RESET_MODE === 'simple' && (
          <form onSubmit={handleSimpleReset}>
            <p className="forgot-hint">
              請輸入註冊時的 Email 與手機號碼進行身分驗證,通過後即可設定新密碼。
            </p>
            <div className="form-group">
              <label><Mail size={16} /> Email</label>
              <input
                name="email"
                type="email"
                placeholder="請輸入註冊 Email"
                value={form.email}
                onChange={handleChange}
                required
              />
            </div>
            <div className="form-group">
              <label><Phone size={16} /> 手機號碼</label>
              <input
                name="phone"
                type="tel"
                placeholder="請輸入註冊時填寫的手機"
                value={form.phone}
                onChange={handleChange}
                required
              />
            </div>
            <div className="form-group">
              <label><Lock size={16} /> 新密碼</label>
              <div className="pw-input-wrap">
                <input
                  name="newPassword"
                  type={showPw.next ? 'text' : 'password'}
                  placeholder="至少 6 個字元"
                  value={form.newPassword}
                  onChange={handleChange}
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
                  name="confirm"
                  type={showPw.confirm ? 'text' : 'password'}
                  placeholder="請再次輸入新密碼"
                  value={form.confirm}
                  onChange={handleChange}
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
              {loading ? '重設中...' : '重設密碼'}
            </button>
            <Link className="forgot-back" to="/login">返回登入</Link>
          </form>
        )}
      </div>
    </div>
  );
};

export default ForgotPassword;