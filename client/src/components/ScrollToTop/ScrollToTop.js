// src/components/ScrollToTop/ScrollToTop.js
// 自動把畫面捲回最頂端
import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

const ScrollToTop = () => {
  const { pathname } = useLocation();

  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
  }, [pathname]);

  return null; 
};

export default ScrollToTop;