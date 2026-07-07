// src/pages/Register/Register.js
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom'; 
import './Register.css';

const Register = () => {
  const navigate = useNavigate(); 
  
  const [formData, setFormData] = useState({
    username: '',
    email: '',
    phone: '',
    password: '',
    confirmPassword: ''
  });

  // 新增載入中狀態控制，避免連線時使用者瘋狂重複點擊註冊
  const [isLoading, setIsLoading] = useState(false);

  const handleChange = (e) => {
    setFormData({ ...formData, [e.target.name]: e.target.value });
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    
    // --- 前端欄位格式驗證（維持原樣） ---
    if (formData.password.length < 6) {
      alert("為了安全，密碼請至少設定 6 位數喔！");
      return;
    }

    const phoneRegex = /^09\d{8}$/;
    if (!phoneRegex.test(formData.phone)) {
      alert("手機格式好像不太對，請輸入 09 開頭的 10 位數字。");
      return;
    }

    if (formData.password !== formData.confirmPassword) {
      alert("兩次密碼輸入不一致，再檢查一下吧！");
      return;
    }

    // --- 正式啟動後端 API 串接 ---
    setIsLoading(true); // 進入連線中狀態
    
    // 🌐 1. 已更新為學校伺服器的正式內網 IP 網址
    const BASE_URL = "http://163.13.202.116:5050";

    try {
      // 2. 使用 fetch 發送 POST 請求到 /api/register
      const response = await fetch(`${BASE_URL}/api/register`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
          // 💡 已移除 Ngrok 專用的破防標頭
        },
        body: JSON.stringify({
          username: formData.username,
          email: formData.email,
          phone: formData.phone,
          password: formData.password
        })
      });

      const data = await response.json();

      if (response.ok) {
        // 當後端回傳 status 200~299 (成功寫入 MySQL)
        alert("註冊成功！準備前往登入頁面。");
        navigate('/login'); 
      } else {
        // 當後端回傳錯誤（例如：此 Email 已經被註冊過）
        alert(`註冊失敗：${data.message || '請檢查輸入欄位'}`);
      }
    } catch (error) {
      // 攔截連線失敗
      console.error("連線出錯：", error);
      alert("無法連線到伺服器。請確認學校伺服器（163.13.202.116:5050）是否正常在線！");
    } finally {
      setIsLoading(false); // 不論連線成功或失敗，最後都要解除鎖定狀態
    }
  };

  return (
    <div className="register-container">
      <div className="register-card">
        <h2>建立帳戶</h2>
        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label>使用者名稱</label>
            <input 
              name="username" 
              type="text" 
              placeholder="請輸入姓名" 
              value={formData.username} 
              onChange={handleChange} 
              required 
            />
          </div>
          <div className="form-group">
            <label>Email</label>
            <input 
              name="email" 
              type="email" 
              placeholder="example@gmail.com" 
              value={formData.email} 
              onChange={handleChange} 
              required 
            />
          </div>
          <div className="form-group">
            <label>手機</label>
            <input 
              name="phone" 
              type="tel" 
              placeholder="0912345678" 
              value={formData.phone} 
              onChange={handleChange} 
              required 
            />
          </div>
          <div className="form-group">
            <label>密碼</label>
            <input 
              name="password" 
              type="password" 
              placeholder="請輸入密碼" 
              value={formData.password} 
              onChange={handleChange} 
              required 
            />
          </div>
          <div className="form-group">
            <label>確認密碼</label>
            <input 
              name="confirmPassword" 
              type="password" 
              placeholder="請再次輸入密碼" 
              value={formData.confirmPassword} 
              onChange={handleChange} 
              required 
            />
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