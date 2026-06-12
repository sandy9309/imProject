// src/pages/Profile/Profile.js
import React, { useState, useEffect } from 'react';
import { User, Mail, Phone, Calendar, Settings, LogOut } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import './Profile.css';

const Profile = () => {
  const navigate = useNavigate();

  // 確保初始狀態全都是純字串
  const [user, setUser] = useState({
    name: "會員",
    email: "未綁定",
    phone: "未綁定",
    joinDate: "2026-05-28"
  });

  useEffect(() => {
    // 1. 拿到我們在 Login.js 存進去的完整物件字串
    const savedUser = localStorage.getItem('user');

    if (savedUser) {
      try {
        const userObj = JSON.parse(savedUser);
        
        // 偵錯小幫手：可以打開 F12 Console 看看瀏覽器實際到底有沒有存到電話
        console.log("Profile 頁面接收到的暫存資料：", userObj);

        // 修正 1：安全過濾，確保取出來的都是純文字，避免 React 報錯
        const safeName = typeof userObj.name === 'string' ? userObj.name : '';
        const safeEmail = typeof userObj.email === 'string' ? userObj.email : '';
        const safePhone = typeof userObj.phone === 'string' ? userObj.phone : '';

        // 修正 2：簡化邏輯！既然對齊了後端，我們直接用後端給的真資料
        // 如果名字是 Email，自動切成前半段當名字
        let displayName = safeName || '新會員';
        if (displayName.includes('@')) {
          displayName = displayName.split('@')[0];
        }

        // 修正 3：重新校正手機欄位的短路邏輯，有真手機就顯示，沒有就顯示提示
        setUser({
          name: displayName,
          email: safeEmail || '未綁定 Email', 
          phone: safePhone || '請至後端補齊電話欄位', // 備註：解決原本 safePhone || safePhone 的無效重複
          joinDate: userObj.joinDate || '2026-05-28'
        });
      } catch (e) {
        console.error("解析會員資料快取失敗", e);
      }
    }
  }, []);

  const handleLogout = () => {
    alert("已登出系統");
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    navigate('/login');
    window.location.reload();
  };

  return (
    <div className="profile-container">
      <div className="profile-card">
        <div className="profile-sidebar">
          <div className="avatar-section">
            <div className="avatar-circle">{user.name ? user.name[0] : '會'}</div>
            <h3>{user.name}</h3>
            <p>一般會員</p>
          </div>
          <nav className="profile-nav">
            <button className="active"><User size={18}/> 個人資訊</button>
            <button><Settings size={18}/> 帳號設定</button>
            <button className="logout-text" onClick={handleLogout}><LogOut size={18}/> 登出系統</button>
          </nav>
        </div>

        <div className="profile-main">
          <h2>個人資訊設定</h2>
          <div className="info-grid">
            <div className="info-item">
              <label><Mail size={16}/> 電子郵件</label>
              <p>{String(user.email)}</p>
            </div>
            <div className="info-item">
              <label><Phone size={16}/> 電話號碼</label>
              <p>{String(user.phone)}</p>
            </div>
            <div className="info-item">
              <label><Calendar size={16}/> 加入日期</label>
              <p>{String(user.joinDate)}</p>
            </div>
          </div>
          <button className="edit-profile-btn">編輯資料</button>
        </div>
      </div>
    </div>
  );
};

export default Profile;