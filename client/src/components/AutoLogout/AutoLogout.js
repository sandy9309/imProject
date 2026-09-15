// src/components/AutoLogout/AutoLogout.js
import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { showToast } from '../Ui/ui';

// 自動登出(60min) 
const IDLE_LIMIT = 60 * 60 * 1000;

const ACTIVITY_EVENTS = ['mousedown', 'keydown', 'scroll', 'touchstart', 'click'];

const AutoLogout = () => {
  const navigate = useNavigate();
  const timerRef = useRef(null);

  useEffect(() => {
    const isLoggedIn = () => !!localStorage.getItem('token');

    const doLogout = () => {
      if (!isLoggedIn()) return;
      // 清掉登入狀態
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      localStorage.removeItem('user_id');
      localStorage.removeItem('username');
      showToast('因閒置過久,已自動登出,請重新登入', 'info');
      navigate('/login');
      window.dispatchEvent(new Event('user-updated'));
    };

    const resetTimer = () => {
      if (!isLoggedIn()) return;
      if (timerRef.current) clearTimeout(timerRef.current);
      localStorage.setItem('last_activity', String(Date.now()));
      timerRef.current = setTimeout(doLogout, IDLE_LIMIT);
    };

    // 綁定所有操作事件
    ACTIVITY_EVENTS.forEach(evt =>
      window.addEventListener(evt, resetTimer, { passive: true })
    );

    // 剛載入時:若上次操作已超過閒置上限,直接登出;否則重新開始計時
    const last = Number(localStorage.getItem('last_activity') || 0);
    if (isLoggedIn() && last && Date.now() - last > IDLE_LIMIT) {
      doLogout();
    } else {
      resetTimer();
    }

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
      ACTIVITY_EVENTS.forEach(evt => window.removeEventListener(evt, resetTimer));
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return null; 
};

export default AutoLogout;