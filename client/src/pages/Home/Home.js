import React from 'react';
import { useNavigate } from 'react-router-dom';
import './Home.css';

const Home = () => {
  const navigate = useNavigate();

  return (
    <div className="home-container">
      {/* 英雄區 Banner */}
      <header className="hero-section">
        <h1>讓想像中的家具，在現實中落地</h1>
        <p>結合 MR 技術與 3D 模擬，為您打造最直觀的居家配置體驗。</p>
        <div className="hero-btns">
          <button className="primary-btn" onClick={() => navigate('/catalog')}>開始探索</button>
          <button className="secondary-btn" onClick={() => navigate('/register')}>加入會員</button>
        </div>
      </header>

      {/* 特色介紹 */}
      <section className="features">
        <div className="feature-card">
          <div className="icon">🥽</div>
          <h3>沈浸式體驗</h3>
          <p>透過 VR/MR 設備，1:1 預覽家具在房間的實際比例。</p>
        </div>
        <div className="feature-card">
          <div className="icon">🛋️</div>
          <h3>多樣化型錄</h3>
          <p>數百款精選家具模型，支援即時材質與色彩更換。</p>
        </div>
        <div className="feature-card">
          <div className="icon">📊</div>
          <h3>精準測量</h3>
          <p>內置空間測量工具，確保家具完美契合您的房間。</p>
        </div>
      </section>
    </div>
  );
};

export default Home;