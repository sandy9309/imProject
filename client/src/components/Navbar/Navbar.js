// src/components/Navbar/Navbar.js
import React, { useState, useEffect, useRef } from 'react'; 
import { Link, useNavigate } from 'react-router-dom'; 
import { Home, Layout, Folder, LogIn, UserPlus, ShoppingCart, User, LogOut, ChevronDown, BookOpen } from 'lucide-react'; 
import './Navbar.css';
import { showToast, showConfirm } from '../../components/Ui/ui';

const Navbar = () => {
  const navigate = useNavigate();
  
  const [isLoggedIn, setIsLoggedIn] = useState(!!localStorage.getItem('token')); 
  // 使用者姓名（預設為訪客，等登入成功會改掉）
  const [userName, setUserName] = useState('訪客');
  const [showUserMenu, setShowUserMenu] = useState(false);
  const userMenuRef = useRef(null);

  // 自動關閉下拉選單
  useEffect(() => {
    const handleClickOutside = (e) => {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target)) {
        setShowUserMenu(false);
      }
    };
    if (showUserMenu) {
      document.addEventListener('mousedown', handleClickOutside);
      document.addEventListener('touchstart', handleClickOutside);
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('touchstart', handleClickOutside);
    };
  }, [showUserMenu]);

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
    showToast("已登出系統", 'success');
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    
    setIsLoggedIn(false);
    setShowUserMenu(false);
    navigate('/login');
  };

  return (
    <nav className="navbar">
      <Link to="/" className="nav-logo">FitRoom - 適室</Link>
      
      <div className="nav-links">
        <Link to="/" className="nav-item"><Home size={18} /> 首頁簡介</Link>
        <Link to="/catalog" className="nav-item"><Layout size={18} /> 家具型錄</Link>

        {isLoggedIn ? (
          // --- 已登入狀態 ---
          <>
            {/* 我的專案（登入後才會顯示） */}
            <Link to="/projects" className="nav-item">
              <Folder size={18} /> 我的專案
            </Link>

            <Link to="/cart" className="nav-item">
              <ShoppingCart size={18} /> 配置清單
            </Link>

            <div className="user-menu-container" ref={userMenuRef}>
              <div 
                className="nav-avatar-wrapper" 
                onClick={() => setShowUserMenu(!showUserMenu)}
              >
                <div className="nav-avatar">{userName[0]}</div> 
                <ChevronDown size={14} className={showUserMenu ? 'rotate' : ''} />
              </div>

              {showUserMenu && (
                <div className="user-dropdown">
                  <div className="dropdown-info">
                    <p className="user-name">{userName}</p>
                    <p className="user-role">一般會員</p>
                  </div>
                  <hr />
                  <Link to="/profile" className="dropdown-item" onClick={() => setShowUserMenu(false)}>
                    <User size={16} /> 會員中心
                  </Link>
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