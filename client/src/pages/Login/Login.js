import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom'; 
import { LogIn, Mail, Lock } from 'lucide-react';
import './Login.css';

const Login = () => {
  const navigate = useNavigate();
  const [loginData, setLoginData] = useState({
    email: '',
    password: ''
  });

  // 載入中狀態，防止使用者重複點擊
  const [isLoading, setIsLoading] = useState(false);

  const handleChange = (e) => {
    setLoginData({ ...loginData, [e.target.name]: e.target.value });
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setIsLoading(true); // 備註：開始連線，進入載入狀態

    // 1. 設定妳們後端的 Ngrok 網址
    const BASE_URL = "https://refulgently-unavailing-mathilda.ngrok-free.dev";

    try {
      // 2. 發送 POST 請求給後端
      const response = await fetch(`${BASE_URL}/api/login`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          // 提示：加上這個 header 可以跳過 Ngrok 的警告頁面
          'ngrok-skip-browser-warning': 'true' 
        },
        body: JSON.stringify(loginData) 
      });

      const data = await response.json();
      console.log("後端回傳資料：", JSON.stringify(data));

      if (response.ok) {
        // 3. 登入成功！
        alert("登入成功！歡迎回來");
        
        // 除錯紀錄：在 Console 印出資料，方便隨時檢查後端欄位
        console.log("後端登入 API 真正回傳的原始資料：", data);
        
        // 4. 把後端回傳的 Token 存入瀏覽器暫存
        localStorage.setItem('token', data.token || '');
        
        // 🚀 關鍵修改點 1：獨立儲存單獨的 username 與 user_id，讓 Navbar 與 Cart.js 能直接、安全地讀取
        const realUserId = data.user_id || data.userId || data.id;
        const realUserName = data.username || '會員';
        
        if (realUserId) {
          localStorage.setItem('user_id', String(realUserId)); // 這樣 Cart.js 就能直接拿到 "13" 或 "14" 了！
        }
        localStorage.setItem('username', String(realUserName)); // 方便 Navbar 認人

        // 5. 保留原本的打包儲存，維持其他頁面功能不損壞
        localStorage.setItem('user', JSON.stringify({
          name: realUserName,
          email: data.email || '',       
          phone: data.phone || '',       
          user_id: realUserId || ''    
        }));

        // 🚀 關鍵修改點 2：安全防禦！在登入新帳號時，強制洗掉前一個人殘留的購物車本地快取，杜絕隱私大混亂
        localStorage.removeItem('cart');
        localStorage.removeItem('cart_user_id');
        
        // 6. 自動跳轉到家具型錄
        navigate('/catalog');
        
        // 7. 重新整理一下網頁，讓 Navbar 立刻去抓最新的 localStorage 狀態
        window.location.reload();
      } else {
        // 4. 登入失敗處理
        alert(`登入失敗：${data.message || '請檢查帳號密碼'}`);
      }
    } catch (error) {
      console.error("連線出錯：", error);
      alert("無法連線到伺服器，請確認後端同學的 Ngrok 是否有開，或者網路是否正常。");
    } finally {
      setIsLoading(false); // 備註：不管成功還是失敗，最後都要結束連線狀態
    }
  };

  return (
    <div className="login-container">
      <div className="login-card">
        <div className="login-header">
          <div className="login-icon">
            <LogIn size={32} color="#2563eb" />
          </div>
          <h2>歡迎回來</h2>
          <p>請輸入您的帳號密碼以繼續</p>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label><Mail size={16} /> Email 帳號</label>
            <input 
              name="email" 
              type="email" 
              placeholder="example@gmail.com" 
              value={loginData.email} 
              onChange={handleChange} 
              required 
            />
          </div>

          <div className="form-group">
            <label><Lock size={16} /> 密碼</label>
            <input 
              name="password" 
              type="password" 
              placeholder="請輸入密碼" 
              value={loginData.password} 
              onChange={handleChange} 
              required 
            />
          </div>

          <div className="login-options">
            <label><input type="checkbox" /> 記住我</label>
            <Link to="/forgot-password">忘記密碼？</Link>
          </div>

          {/* 修正：綁定 disabled={isLoading}，並在連線中時將文字改成「登入中...」 */}
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