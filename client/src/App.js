import React from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';

import Navbar from './components/Navbar/Navbar';
import Home from './pages/Home/Home';          
import Register from './pages/Register/Register';
import Login from './pages/Login/Login';       
import Catalog from './pages/Catalog/Catalog';
import Footer from './components/Footer/Footer';
import Cart from './pages/Cart/Cart';
import Profile from './pages/Profile/Profile';
import Projects from './pages/Projects/Projects'; // 🚀 1. 新增：引入妳的專案管理頁面
import UiRoot from './components/Ui/UiRoot'; // 🚀 全站提示系統(Toast + 確認彈窗)
import AiAssistant from './components/AiAssistant/AiAssistant'; // 🤖 AI 空間設計小幫手(右下角懸浮)
import ForgotPassword from './pages/ForgotPassword/ForgotPassword'; // 🔑 忘記密碼
import ResetPassword from './pages/ResetPassword/ResetPassword'; // 🔑 重設密碼(信件連結進入)

function App() {
  return (
    <Router>
      <Navbar /> {/* 直接放進來就好 */}
      <UiRoot /> {/* 🚀 全站唯一的提示渲染中心 */}
      <AiAssistant /> {/* 🤖 右下角 AI 小幫手,全站可用 */}
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route path="/forgot-password" element={<ForgotPassword />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        <Route path="/catalog" element={<Catalog />} />
        <Route path="/cart" element={<Cart />} />
        <Route path="/profile" element={<Profile />} />
        
        {/* 🚀 2. 新增：註冊 /projects 路由，讓 Navbar 的連結找得到對應的房間 */}
        <Route path="/projects" element={<Projects />} />
      </Routes>
      <Footer />
    </Router>
  );
}

export default App;