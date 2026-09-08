import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom'; 
import { LogIn, Mail, Lock, Eye, EyeOff } from 'lucide-react';
import './Login.css';
import { showToast, showConfirm } from '../../components/Ui/ui';
import { clearUserScopedStorage } from '../../components/Navbar/Navbar';

const Login = () => {
  const navigate = useNavigate();
  const [loginData, setLoginData] = useState({
    email: '',
    password: ''
  });

  const [isLoading, setIsLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [errors, setErrors] = useState({});
  const [touched, setTouched] = useState({});

  const validateField = (name, value) => {
    switch (name) {
      case 'email':
        if (!value.trim()) return '請輸入 Email';
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) return 'Email 格式不正確';
        return '';
      case 'password':
        if (!value) return '請輸入密碼';
        return '';
      default:
        return '';
    }
  };

  const handleChange = (e) => {
    const { name, value } = e.target;
    setLoginData({ ...loginData, [name]: value });
    if (touched[name]) {
      setErrors(prev => ({ ...prev, [name]: validateField(name, value) }));
    }
  };

  const handleBlur = (e) => {
    const { name, value } = e.target;
    setTouched(prev => ({ ...prev, [name]: true }));
    setErrors(prev => ({ ...prev, [name]: validateField(name, value) }));
  };

  const showErr = (name) => touched[name] && errors[name];

  const handleSubmit = async (e) => {
    e.preventDefault();

    const allErrors = {};
    Object.keys(loginData).forEach(key => {
      const msg = validateField(key, loginData[key]);
      if (msg) allErrors[key] = msg;
    });
    setErrors(allErrors);
    setTouched({ email: true, password: true });
    if (Object.keys(allErrors).length > 0) {
      showToast('請確認 Email 與密碼欄位', 'error');
      return;
    }

    setIsLoading(true);
    const BASE_URL = "http://163.13.202.116:5050";

    try {
      const response = await fetch(`${BASE_URL}/api/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(loginData) 
      });

      const data = await response.json();

      if (response.ok) {
        showToast("登入成功！歡迎回來", 'success');

        
        clearUserScopedStorage();

        localStorage.setItem('token', data.token || '');

        const realUserId = data.user_id || data.userId || data.id;
        const realUserName = data.username || '會員';

        if (realUserId) {
          localStorage.setItem('user_id', String(realUserId));
        }
        localStorage.setItem('username', String(realUserName));

        const realJoinDate = data.joinDate || data.join_date || data.created_at || '';
        localStorage.setItem('user', JSON.stringify({
          name: realUserName,
          email: data.email || '',       
          phone: data.phone || '',       
          user_id: realUserId || '',
          joinDate: realJoinDate,
        }));

        navigate('/catalog');
        window.location.reload();
      } else {
        showToast(`登入失敗：${data.message || '請檢查帳號密碼'}`, 'error');
      }
    } catch (error) {
      console.error("連線出錯：", error);
      showToast("無法連線到伺服器，請稍後再試一次。", 'error');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="login-container">
      <div className="login-card">
        <div className="login-header">
          <div className="login-icon">
            <LogIn size={32} color="var(--color-primary)" />
          </div>
          <h2>歡迎回來</h2>
          <p>請輸入您的帳號密碼以繼續</p>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="form-group">
            <label><Mail size={16} /> Email 帳號</label>
            <input 
              name="email" 
              type="email" 
              className={showErr('email') ? 'input-error' : ''}
              placeholder="example@gmail.com" 
              value={loginData.email} 
              onChange={handleChange} 
              onBlur={handleBlur}
            />
            {showErr('email') && <span className="field-error">{errors.email}</span>}
          </div>

          <div className="form-group">
            <label><Lock size={16} /> 密碼</label>
            <div className="pw-input-wrap">
              <input 
                name="password" 
                type={showPassword ? 'text' : 'password'} 
                className={showErr('password') ? 'input-error' : ''}
                placeholder="請輸入密碼" 
                value={loginData.password} 
                onChange={handleChange} 
                onBlur={handleBlur}
              />
              <button
                type="button"
                className="pw-toggle-btn"
                onClick={() => setShowPassword(v => !v)}
                aria-label={showPassword ? '隱藏密碼' : '顯示密碼'}
              >
                {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
              </button>
            </div>
            {showErr('password') && <span className="field-error">{errors.password}</span>}
          </div>

          <div className="login-options">
            <label><input type="checkbox" /> 記住我</label>
            <Link to="/forgot-password">忘記密碼？</Link>
          </div>

          <button type="submit" className="login-btn" disabled={isLoading}>
            {isLoading ? "登入中..." : "登入系統"}
          </button>
        </form>

        <div className="login-footer">
          還沒有帳號嗎？ <Link to="/register">立即註冊</Link>
        </div>
      </div>
    </div>
  );
};

export default Login;