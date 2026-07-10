// src/pages/Profile/Profile.js
import React, { useState, useEffect } from 'react';
import { User, Mail, Phone, Calendar, Settings, LogOut, Moon, Sun, KeyRound, AlertTriangle } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import './Profile.css';

// 🌐 學校伺服器的正式內網 IP 網址
const API_BASE = 'http://163.13.202.116:5050';

const Profile = () => {
  const navigate = useNavigate();

  // 確保初始狀態全都是純字串
  const [user, setUser] = useState({
    name: "會員",
    email: "未綁定",
    phone: "未綁定",
    joinDate: "未提供"
  });

  // 🚀 頁籤切換：個人資訊 / 帳號設定
  const [activeTab, setActiveTab] = useState('info');

  // 🚀 編輯資料：只開放姓名、電話兩個欄位
  const [isEditing, setIsEditing] = useState(false);
  const [editName, setEditName] = useState('');
  const [editPhone, setEditPhone] = useState('');
  const [saving, setSaving] = useState(false);

  // 🚀 修改密碼
  const [pwForm, setPwForm] = useState({ current: '', next: '', confirm: '' });
  const [pwSaving, setPwSaving] = useState(false);

  // 🚀 深色模式（純前端功能，存在瀏覽器本機）
  const [darkMode, setDarkMode] = useState(() => localStorage.getItem('darkMode') === 'true');

  // 🚀 刪除帳號
  const [deleteConfirmText, setDeleteConfirmText] = useState('');
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    const savedUser = localStorage.getItem('user');

    if (savedUser) {
      try {
        const userObj = JSON.parse(savedUser);
        console.log("Profile 頁面接收到的暫存資料：", userObj);

        const safeName = typeof userObj.name === 'string' ? userObj.name : '';
        const safeEmail = typeof userObj.email === 'string' ? userObj.email : '';
        const safePhone = typeof userObj.phone === 'string' ? userObj.phone : '';

        let displayName = safeName || '新會員';
        if (displayName.includes('@')) {
          displayName = displayName.split('@')[0];
        }

        // 🚀 加入日期：格式化成 YYYY-MM-DD；後端沒給就顯示「未提供」，不再用寫死的假日期
        const rawJoinDate = userObj.joinDate || '';
        let displayJoinDate = '未提供';
        if (rawJoinDate) {
          const d = new Date(rawJoinDate);
          displayJoinDate = isNaN(d)
            ? String(rawJoinDate)
            : `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
        }

        const nextUser = {
          name: displayName,
          email: safeEmail || '未綁定 Email',
          phone: safePhone || '尚未填寫電話',
          joinDate: displayJoinDate
        };

        setUser(nextUser);
        setEditName(nextUser.name);
        setEditPhone(safePhone);
      } catch (e) {
        console.error("解析會員資料快取失敗", e);
      }
    }
  }, []);

  // ── 深色模式：套用 / 移除 <body> 的 class，並記住使用者的選擇 ──
  useEffect(() => {
    document.body.classList.toggle('dark-mode', darkMode);
    localStorage.setItem('darkMode', String(darkMode));
  }, [darkMode]);

  const handleLogout = () => {
    alert("已登出系統");
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    navigate('/login');
    window.location.reload();
  };

  // ── 開始編輯：把目前顯示的值帶入輸入框 ─────────────────────
  const startEditing = () => {
    setEditName(user.name);
    setEditPhone(user.phone === '尚未填寫電話' ? '' : user.phone);
    setIsEditing(true);
  };

  const cancelEditing = () => {
    setIsEditing(false);
  };

  // ── 儲存編輯：呼叫後端更新姓名 / 電話 ───────────────────────
  const saveProfile = async () => {
    const trimmedName = editName.trim();
    if (!trimmedName) {
      alert('姓名不能是空白喔！');
      return;
    }

    const userId = localStorage.getItem('user_id');
    if (!userId) {
      alert('找不到帳號 ID，請重新登入後再試一次');
      return;
    }

    try {
      setSaving(true);
      const res = await fetch(`${API_BASE}/api/users/${userId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: trimmedName,
          phone: editPhone.trim(),
        }),
      });

      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '更新失敗');
      }

      const updatedUser = { ...user, name: trimmedName, phone: editPhone.trim() || '尚未填寫電話' };
      setUser(updatedUser);

      const savedUser = JSON.parse(localStorage.getItem('user') || '{}');
      localStorage.setItem('user', JSON.stringify({
        ...savedUser,
        name: trimmedName,
        phone: editPhone.trim(),
      }));
      localStorage.setItem('username', trimmedName);

      setIsEditing(false);
      alert('✅ 資料已更新！');
    } catch (err) {
      console.error('更新會員資料失敗:', err);
      alert(`更新失敗：${err.message}`);
    } finally {
      setSaving(false);
    }
  };

  // ── 修改密碼 ─────────────────────────────────────────────────
  const handleChangePassword = async (e) => {
    e.preventDefault();

    if (!pwForm.current || !pwForm.next || !pwForm.confirm) {
      alert('請把三個欄位都填寫完整');
      return;
    }
    if (pwForm.next.length < 6) {
      alert('新密碼至少需要 6 個字元');
      return;
    }
    if (pwForm.next !== pwForm.confirm) {
      alert('兩次輸入的新密碼不一致，請再確認一次');
      return;
    }

    const userId = localStorage.getItem('user_id');
    if (!userId) {
      alert('找不到帳號 ID，請重新登入後再試一次');
      return;
    }

    try {
      setPwSaving(true);
      const res = await fetch(`${API_BASE}/api/users/${userId}/password`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          currentPassword: pwForm.current,
          newPassword: pwForm.next,
        }),
      });

      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '修改密碼失敗，請確認目前密碼是否正確');
      }

      alert('✅ 密碼已更新！下次登入請使用新密碼');
      setPwForm({ current: '', next: '', confirm: '' });
    } catch (err) {
      console.error('修改密碼失敗:', err);
      alert(`修改失敗：${err.message}`);
    } finally {
      setPwSaving(false);
    }
  };

  // ── 永久刪除帳號 ─────────────────────────────────────────────
  const handleDeleteAccount = async () => {
    if (deleteConfirmText !== '刪除我的帳號') {
      alert('請在輸入框裡準確輸入「刪除我的帳號」以進行最終確認');
      return;
    }

    const finalConfirm = window.confirm(
      '⚠️ 這是最後一次確認：帳號刪除後無法復原，所有專案與資料都會一併消失。真的要繼續嗎？'
    );
    if (!finalConfirm) return;

    const userId = localStorage.getItem('user_id');
    if (!userId) {
      alert('找不到帳號 ID，請重新登入後再試一次');
      return;
    }

    try {
      setDeleting(true);
      const res = await fetch(`${API_BASE}/api/users/${userId}`, {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
      });

      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '刪除帳號失敗');
      }

      alert('帳號已永久刪除，感謝您曾經使用本系統');
      localStorage.clear();
      navigate('/login');
      window.location.reload();
    } catch (err) {
      console.error('刪除帳號失敗:', err);
      alert(`刪除失敗：${err.message}`);
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="profile-container">
      <div className="profile-card">
        <div className="profile-sidebar">
          <div className="avatar-section">
            <div className="avatar-circle">{user.name ? user.name[0] : '會'}</div>
            <h3>{user.name}</h3>
            <p>一般會員</p>
          </div>
          <nav className="profile-nav">
            <button
              className={activeTab === 'info' ? 'active' : ''}
              onClick={() => setActiveTab('info')}
            >
              <User size={18}/> 個人資訊
            </button>
            <button
              className={activeTab === 'settings' ? 'active' : ''}
              onClick={() => setActiveTab('settings')}
            >
              <Settings size={18}/> 帳號設定
            </button>
            <button className="logout-text" onClick={handleLogout}><LogOut size={18}/> 登出系統</button>
          </nav>
        </div>

        <div className="profile-main">
          {activeTab === 'info' ? (
            <>
              <h2>個人資訊設定</h2>

              {!isEditing ? (
                <>
                  <div className="info-grid">
                    <div className="info-item">
                      <label><User size={16}/> 姓名</label>
                      <p>{String(user.name)}</p>
                    </div>
                    <div className="info-item">
                      <label><Mail size={16}/> 電子郵件</label>
                      <p>{String(user.email)}</p>
                    </div>
                    <div className="info-item">
                      <label><Phone size={16}/> 電話號碼</label>
                      <p>{String(user.phone)}</p>
                    </div>
                    <div className="info-item">
                      <label><Calendar size={16}/> 加入日期</label>
                      <p>{String(user.joinDate)}</p>
                    </div>
                  </div>
                  <button className="edit-profile-btn" onClick={startEditing}>編輯資料</button>
                </>
              ) : (
                <>
                  <div className="info-grid">
                    <div className="info-item">
                      <label><User size={16}/> 姓名</label>
                      <input
                        className="profile-edit-input"
                        type="text"
                        value={editName}
                        onChange={e => setEditName(e.target.value)}
                        maxLength={30}
                      />
                    </div>
                    <div className="info-item">
                      <label><Mail size={16}/> 電子郵件</label>
                      <p className="profile-readonly-hint">{String(user.email)}（不可修改）</p>
                    </div>
                    <div className="info-item">
                      <label><Phone size={16}/> 電話號碼</label>
                      <input
                        className="profile-edit-input"
                        type="tel"
                        placeholder="請輸入電話號碼"
                        value={editPhone}
                        onChange={e => setEditPhone(e.target.value)}
                        maxLength={20}
                      />
                    </div>
                    <div className="info-item">
                      <label><Calendar size={16}/> 加入日期</label>
                      <p className="profile-readonly-hint">{String(user.joinDate)}（不可修改）</p>
                    </div>
                  </div>
                  <div className="profile-edit-actions">
                    <button className="edit-profile-btn" onClick={saveProfile} disabled={saving}>
                      {saving ? '儲存中...' : '儲存變更'}
                    </button>
                    <button className="edit-profile-cancel-btn" onClick={cancelEditing} disabled={saving}>
                      取消
                    </button>
                  </div>
                </>
              )}
            </>
          ) : (
            <>
              <h2>帳號設定</h2>

              {/* ── 修改密碼 ── */}
              <section className="settings-section">
                <h3><KeyRound size={18}/> 修改密碼</h3>
                <form className="password-form" onSubmit={handleChangePassword}>
                  <div className="info-item">
                    <label>目前密碼</label>
                    <input
                      className="profile-edit-input"
                      type="password"
                      value={pwForm.current}
                      onChange={e => setPwForm({ ...pwForm, current: e.target.value })}
                      autoComplete="current-password"
                    />
                  </div>
                  <div className="info-item">
                    <label>新密碼</label>
                    <input
                      className="profile-edit-input"
                      type="password"
                      value={pwForm.next}
                      onChange={e => setPwForm({ ...pwForm, next: e.target.value })}
                      autoComplete="new-password"
                      placeholder="至少 6 個字元"
                    />
                  </div>
                  <div className="info-item">
                    <label>再輸入一次新密碼</label>
                    <input
                      className="profile-edit-input"
                      type="password"
                      value={pwForm.confirm}
                      onChange={e => setPwForm({ ...pwForm, confirm: e.target.value })}
                      autoComplete="new-password"
                    />
                  </div>
                  <button className="edit-profile-btn" type="submit" disabled={pwSaving}>
                    {pwSaving ? '更新中...' : '更新密碼'}
                  </button>
                </form>
              </section>

              {/* ── 深色模式 ── */}
              <section className="settings-section">
                <h3>{darkMode ? <Moon size={18}/> : <Sun size={18}/>} 外觀</h3>
                <div className="dark-mode-row">
                  <div>
                    <p className="dark-mode-label">深色模式</p>
                    <p className="profile-readonly-hint">調整整體介面的明暗配色</p>
                  </div>
                  <button
                    className={`toggle-switch ${darkMode ? 'on' : ''}`}
                    onClick={() => setDarkMode(v => !v)}
                    aria-label="切換深色模式"
                  >
                    <span className="toggle-knob" />
                  </button>
                </div>
              </section>

              {/* ── 危險區：永久刪除帳號 ── */}
              <section className="settings-section danger-zone">
                <h3><AlertTriangle size={18}/> 刪除帳號</h3>
                <p className="profile-readonly-hint">
                  帳號刪除後無法復原，所有專案、配置清單等資料都會一併消失。請謹慎操作。
                </p>
                <div className="info-item">
                  <label>請輸入「刪除我的帳號」以確認</label>
                  <input
                    className="profile-edit-input danger-input"
                    type="text"
                    value={deleteConfirmText}
                    onChange={e => setDeleteConfirmText(e.target.value)}
                    placeholder="刪除我的帳號"
                  />
                </div>
                <button
                  className="delete-account-btn"
                  onClick={handleDeleteAccount}
                  disabled={deleting || deleteConfirmText !== '刪除我的帳號'}
                >
                  {deleting ? '刪除中...' : '永久刪除我的帳號'}
                </button>
              </section>
            </>
          )}
        </div>
      </div>
    </div>
  );
};

export default Profile;