// src/pages/Register/Register.js
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom'; 
import { Eye, EyeOff } from 'lucide-react';
import PasswordStrength from '../../components/PasswordStrength/PasswordStrength';
import './Register.css';
import { showToast } from '../../components/Ui/ui';

const Register = () => {
  const navigate = useNavigate(); 
  
  const [formData, setFormData] = useState({
    username: '',
    email: '',
    phone: '',
    password: '',
    confirmPassword: ''
  });

  const [isLoading, setIsLoading] = useState(false);
  const [showPw, setShowPw] = useState({ password: false, confirm: false });
  const [errors, setErrors] = useState({});
  const [touched, setTouched] = useState({});

  const validateField = (name, value, all = formData) => {
    switch (name) {
      case 'username':
        if (!value.trim()) return '請輸入使用者名稱';
        return '';
      case 'email':
        if (!value.trim()) return '請輸入 Email';
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) return 'Email 格式不正確';
        return '';
      case 'phone':
        if (!value.trim()) return '請輸入手機號碼';
        if (!/^09\d{8}$/.test(value)) return '請輸入 09 開頭的 10 位數字';
        return '';
      case 'password':
        if (!value) return '請輸入密碼';
        if (value.length < 6) return '密碼至少需要 6 個字元';
        return '';
      case 'confirmPassword':
        if (!value) return '請再次輸入密碼';
        if (value !== all.password) return '兩次輸入的密碼不一致';
        return '';
      default:
        return '';
    }
  };

  const handleChange = (e) => {
    const { name, value } = e.target;
    const next = { ...formData, [name]: value };
    setFormData(next);

    // 即時更新錯誤狀態
    if (touched[name]) {
      setErrors(prev => ({ ...prev, [name]: validateField(name, value, next) }));
    }
    if (name === 'password' && touched.confirmPassword) {
      setErrors(prev => ({ ...prev, confirmPassword: validateField('confirmPassword', next.confirmPassword, next) }));
    }
  };

  const handleBlur = (e) => {
    const { name, value } = e.target;
    setTouched(prev => ({ ...prev, [name]: true }));
    setErrors(prev => ({ ...prev, [name]: validateField(name, value) }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();

    const allErrors = {};
    Object.keys(formData).forEach(key => {
      const msg = validateField(key, formData[key]);
      if (msg) allErrors[key] = msg;
    });
    setErrors(allErrors);
    setTouched({ username: true, email: true, phone: true, password: true, confirmPassword: true });

    if (Object.keys(allErrors).length > 0) {
      showToast('請修正表單中標示的欄位', 'error');
      return;
    }

    setIsLoading(true);
    const BASE_URL = "http://163.13.202.116:5050";

    try {
      const response = await fetch(`${BASE_URL}/api/register`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: formData.username,
          email: formData.email,
          phone: formData.phone,
          password: formData.password
        })
      });

      const data = await response.json();

      if (response.ok) {
        showToast("註冊成功！準備前往登入頁面。", 'success');
        navigate('/login'); 
      } else {
        showToast(`註冊失敗：${data.message || '請檢查輸入欄位'}`, 'error');
      }
    } catch (error) {
      console.error("連線出錯：", error);
      showToast("無法連線到伺服器，請稍後再試一次。", 'error');
    } finally {
      setIsLoading(false);
    }
  };

  const showErr = (name) => touched[name] && errors[name];

  return (
    <div className="register-container">
      <div className="register-card">
        <h2>建立帳戶</h2>
        <form onSubmit={handleSubmit} noValidate>
          <div className="form-group">
            <label>使用者名稱</label>
            <input 
              name="username" 
              type="text" 
              className={showErr('username') ? 'input-error' : ''}
              placeholder="請輸入姓名" 
              value={formData.username} 
              onChange={handleChange} 
              onBlur={handleBlur}
            />
            {showErr('username') && <span className="field-error">{errors.username}</span>}
          </div>

          <div className="form-group">
            <label>Email</label>
            <input 
              name="email" 
              type="email" 
              className={showErr('email') ? 'input-error' : ''}
              placeholder="example@gmail.com" 
              value={formData.email} 
              onChange={handleChange} 
              onBlur={handleBlur}
            />
            {showErr('email') && <span className="field-error">{errors.email}</span>}
          </div>

          <div className="form-group">
            <label>手機</label>
            <input 
              name="phone" 
              type="tel" 
              className={showErr('phone') ? 'input-error' : ''}
              placeholder="0912345678" 
              value={formData.phone} 
              onChange={handleChange} 
              onBlur={handleBlur}
            />
            {showErr('phone') && <span className="field-error">{errors.phone}</span>}
          </div>

          <div className="form-group">
            <label>密碼</label>
            <div className="pw-input-wrap">
              <input 
                name="password" 
                type={showPw.password ? 'text' : 'password'} 
                className={showErr('password') ? 'input-error' : ''}
                placeholder="請輸入密碼" 
                value={formData.password} 
                onChange={handleChange} 
                onBlur={handleBlur}
              />
              <button
                type="button"
                className="pw-toggle-btn"
                onClick={() => setShowPw({ ...showPw, password: !showPw.password })}
                aria-label={showPw.password ? '隱藏密碼' : '顯示密碼'}
              >
                {showPw.password ? <EyeOff size={18} /> : <Eye size={18} />}
              </button>
            </div>
            {showErr('password') && <span className="field-error">{errors.password}</span>}
            <PasswordStrength password={formData.password} />
          </div>

          <div className="form-group">
            <label>確認密碼</label>
            <div className="pw-input-wrap">
              <input 
                name="confirmPassword" 
                type={showPw.confirm ? 'text' : 'password'} 
                className={showErr('confirmPassword') ? 'input-error' : ''}
                placeholder="請再次輸入密碼" 
                value={formData.confirmPassword} 
                onChange={handleChange} 
                onBlur={handleBlur}
              />
              <button
                type="button"
                className="pw-toggle-btn"
                onClick={() => setShowPw({ ...showPw, confirm: !showPw.confirm })}
                aria-label={showPw.confirm ? '隱藏密碼' : '顯示密碼'}
              >
                {showPw.confirm ? <EyeOff size={18} /> : <Eye size={18} />}
              </button>
            </div>
            {showErr('confirmPassword') && <span className="field-error">{errors.confirmPassword}</span>}
          </div>

          <button type="submit" className="submit-btn" disabled={isLoading}>
            {isLoading ? "註冊中..." : "註冊"}
          </button>
        </form>
      </div>
    </div>
  );
};

export default Register;