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
import Projects from './pages/Projects/Projects';
import UiRoot from './components/Ui/UiRoot'; 
import AiAssistant from './components/AiAssistant/AiAssistant'; 
import ForgotPassword from './pages/ForgotPassword/ForgotPassword';
import ResetPassword from './pages/ResetPassword/ResetPassword'; 
import GuideTour from './components/Guide/GuideTour';
import GuidePage from './pages/Guide/GuidePage';
import ScrollToTop from './components/ScrollToTop/ScrollToTop';

function App() {
  return (
    <Router>
      <ScrollToTop />
      <Navbar /> 
      <UiRoot /> 
      <GuideTour />   
      <AiAssistant /> 
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route path="/forgot-password" element={<ForgotPassword />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        <Route path="/catalog" element={<Catalog />} />
        <Route path="/cart" element={<Cart />} />
        <Route path="/profile" element={<Profile />} />
        <Route path="/guide" element={<GuidePage />} />
        <Route path="/projects" element={<Projects />} />
      </Routes>
      <Footer />
    </Router>
  );
}

export default App;