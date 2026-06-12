import React from 'react';
import './Footer.css';

const Footer = () => {
  return (
    <footer className="footer">
      <div className="footer-content">
        <div className="footer-section">
          <h3>TKUIM VR Furniture</h3>
          <p>沈浸式虛擬實境家具模擬系統</p>
        </div>
        <div className="footer-section">
          <h4>專題小組</h4>
          <p>淡江資管 3C - 鄧詠妍 & 組員</p>
          <p>指導教授：魏世傑 老師</p>
        </div>
        <div className="footer-section">
          <h4>快速連結</h4>
          <ul>
            <li>使用條款</li>
            <li>隱私權政策</li>
            <li>常見問題</li>
          </ul>
        </div>
      </div>
      <div className="footer-bottom">
        &copy; 2026 TKUIM VR Project. All rights reserved.
      </div>
    </footer>
  );
};

export default Footer;