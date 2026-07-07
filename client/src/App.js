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

function App() {
  return (
    <Router>
      <Navbar /> {/* 直接放進來就好 */}
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
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