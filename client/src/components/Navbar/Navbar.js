// src/components/Navbar/Navbar.js
import React, { useState, useEffect, useRef } from 'react'; // 修正 1: 引入 useEffect 用於監聽瀏覽器暫存
import { Link, useNavigate } from 'react-router-dom'; 
// 🚀 這裡幫妳多引入了 Folder 圖示，用於「我的專案」
import { Home, Layout, Folder, LogIn, UserPlus, ShoppingCart, User, LogOut, ChevronDown, BookOpen } from 'lucide-react'; 
import './Navbar.css';
import { showToast, showConfirm } from '../../components/Ui/ui';

const Navbar = () => {
  const navigate = useNavigate();
  
  // 修正 2: 不要寫死 true！改成「自動檢查 localStorage 有沒有 token」，有的話是 true，沒有就是 false
  // 備註：!! 符號可以把原本的資料轉換成真實的 boolean 值
  const [isLoggedIn, setIsLoggedIn] = useState(!!localStorage.getItem('token')); 
  
  // 修正 3: 新增狀態管理動態使用者姓名（預設為訪客，等登入成功會被改掉）
  const [userName, setUserName] = useState('訪客');
  
  // 備註：控制頭像下拉選單顯示的狀態維持不變
  const [showUserMenu, setShowUserMenu] = useState(false);

  // 🚀 選單容器的參照,用來判斷「點擊是否發生在選單外面」
  const userMenuRef = useRef(null);

  // 🚀 點擊選單以外的任何地方,自動關閉下拉選單
  useEffect(() => {
    const handleClickOutside = (e) => {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target)) {
        setShowUserMenu(false);
      }
    };
    // 選單開著的時候才需要監聽,關著就把監聽拆掉,避免浪費效能
    // 🚀 同時監聽 mousedown 與 touchstart,手機觸控也能點外面關閉
    if (showUserMenu) {
      document.addEventListener('mousedown', handleClickOutside);
      document.addEventListener('touchstart', handleClickOutside);
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('touchstart', handleClickOutside);
    };
  }, [showUserMenu]);

  // 修正 4: 網頁載入時撈 localStorage 的 user 物件,
  // 🚀 並監聽「user-updated」事件:會員中心改名後會發出這個事件,Navbar 立刻同步新名稱
  useEffect(() => {
    const loadUserName = () => {
      const savedUser = localStorage.getItem('user');
      if (savedUser) {
        try {
          const userObj = JSON.parse(savedUser);
          setUserName(userObj.name || '會員');
        } catch (e) {
          setUserName('會員');
        }
      }
    };
    loadUserName();
    window.addEventListener('user-updated', loadUserName);
    return () => window.removeEventListener('user-updated', loadUserName);
  }, []);

  const handleLogout = () => {
    // 備註：執行登出邏輯
    showToast("已登出系統", 'success');
    
    // 修正 5: 登出時，要把瀏覽器的這兩項關鍵暫存全部清空，否則網頁會一直以為妳還在登入狀態
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    
    setIsLoggedIn(false);
    setShowUserMenu(false);
    navigate('/login');
  };

  return (
    <nav className="navbar">
      <Link to="/" className="nav-logo">FitRoom</Link>
      
      <div className="nav-links">
        <Link to="/" className="nav-item"><Home size={18} /> 首頁簡介</Link>
        <Link to="/catalog" className="nav-item"><Layout size={18} /> 家具型錄</Link>

        {/* 備註：條件渲染判斷開始 */}
        {isLoggedIn ? (
          // --- 已登入狀態 ---
          <>
            {/* 🚀 這裡就是新加入的：我的專案連結（限定登入後才會顯示） */}
            <Link to="/projects" className="nav-item">
              <Folder size={18} /> 我的專案
            </Link>

            {/* 備註：購物車連結 */}
            <Link to="/cart" className="nav-item">
              <ShoppingCart size={18} /> 配置清單
            </Link>

            {/* 備註：頭像與下拉選單 */}
            <div className="user-menu-container" ref={userMenuRef}>
              <div 
                className="nav-avatar-wrapper" 
                onClick={() => setShowUserMenu(!showUserMenu)}
              >
                {/* 修正 6: 頭像動態抓取姓名的第一個字（例如：「鄧詠妍」就會顯示「鄧」） */}
                <div className="nav-avatar">{userName[0]}</div> 
                <ChevronDown size={14} className={showUserMenu ? 'rotate' : ''} />
              </div>

              {/* 備註：下拉選單內容 */}
              {showUserMenu && (
                <div className="user-dropdown">
                  <div className="dropdown-info">
                    {/* 修正 7: 顯示後端傳過來的動態姓名 */}
                    <p className="user-name">{userName}</p>
                    <p className="user-role">一般會員</p>
                  </div>
                  <hr />
                  <Link to="/profile" className="dropdown-item" onClick={() => setShowUserMenu(false)}>
                    <User size={16} /> 會員中心
                  </Link>
                  {/* 📖 使用說明入口 */}
                  <Link to="/guide" className="dropdown-item" onClick={() => setShowUserMenu(false)}>
                    <BookOpen size={16} /> 使用說明
                  </Link>
                  <button className="dropdown-logout" onClick={handleLogout}>
                    <LogOut size={16} /> 登出系統
                  </button>
                </div>
              )}
            </div>
          </>
        ) : (
          // --- 未登入狀態 ---
          <>
            {/* 📖 未登入的訪客也能看使用說明 */}
            <Link to="/guide" className="nav-item"><BookOpen size={18} /> 使用說明</Link>
            <Link to="/login" className="nav-item"><LogIn size={18} /> 登入</Link>
            <Link to="/register" className="nav-item register-btn">
              <UserPlus size={18} /> 註冊
            </Link>
          </>
        )}
      </div>
    </nav>
  );
};

export default Navbar;