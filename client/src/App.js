// src/App.js
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
      </Routes>
      <Footer />
    </Router>
  );
}

export default App;