import React, { useState, useEffect } from 'react';
import { ShoppingBag, Save, Plus, Minus } from 'lucide-react';
import './Cart.css';

// 🌐 學校伺服器的正式內網 IP 網址
const API_BASE = 'http://163.13.202.116:5050';
const MAX_QTY = 10;

const getHeaders = {
};

const mutateHeaders = {
  'Content-Type': 'application/json',
};

const Cart = () => {
  const [loading, setLoading] = useState(false);
  const [cartItems, setCartItems] = useState([]);
  const [projectName, setProjectName] = useState('');

  const currentUserId = localStorage.getItem('user_id');

  // 🚀 偵測是不是要「新增家具到既有專案」，而不是建立新專案
  const [editProjectId] = useState(() => localStorage.getItem('editProjectId'));
  const [editProjectName] = useState(() => localStorage.getItem('editProjectName') || '');

  // 🚀 編輯模式下：該專案原本就有的家具清單（可暫存編輯，按下儲存才真的送出），
  // originalExistingItems 是剛載入時的快照，用來比對「有沒有變動」
  const [existingItems, setExistingItems] = useState([]);
  const [originalExistingItems, setOriginalExistingItems] = useState([]);
  const [furnitureMap, setFurnitureMap] = useState({});

  // ── 編輯模式：載入該專案既有的家具 + 家具型錄對照表 ──────────
  useEffect(() => {
    if (!editProjectId || !currentUserId) return;

    const fetchExisting = async () => {
      try {
        const [listRes, furnitureRes] = await Promise.all([
          fetch(`${API_BASE}/api/projects?userId=${currentUserId}`, { headers: getHeaders }),
          fetch(`${API_BASE}/api/furnitures`, { headers: getHeaders }),
        ]);

        if (listRes.ok) {
          const listBody = await listRes.json();
          const targetProject = (listBody.data || []).find(
            p => String(p.id) === String(editProjectId)
          );
          const items = targetProject && Array.isArray(targetProject.items) ? targetProject.items : [];
          setExistingItems(items);
          setOriginalExistingItems(items);
        }

        if (furnitureRes.ok) {
          const data = await furnitureRes.json();
          const list = Array.isArray(data) ? data : (data.data || []);
          const map = {};
          list.forEach(f => { map[f.id] = f; });
          setFurnitureMap(map);
        }
      } catch (err) {
        console.error('載入既有家具清單失敗:', err);
      }
    };

    fetchExisting();
  }, [editProjectId, currentUserId]);

  // ── 載入購物車 ──────────────────────────────────────────────
  useEffect(() => {
    if (!currentUserId) return;

    const fetchUserCart = async () => {
      try {
        setLoading(true);

        const lastCartUserId = localStorage.getItem('cart_user_id');
        if (lastCartUserId && lastCartUserId !== currentUserId) {
          localStorage.removeItem('cart');
        }
        localStorage.setItem('cart_user_id', currentUserId);

        const response = await fetch(
          `${API_BASE}/api/cart?userId=${currentUserId}`,
          { headers: getHeaders }
        );

        if (response.ok) {
          const resBody = await response.json();
          const realCartList = resBody.data || [];
          if (realCartList.length > 0) {
            const formattedList = realCartList.map(item => ({
              ...item,
              id: item.id,
              product_id: item.product_id || item.id,
            }));
            setCartItems(formattedList);
            localStorage.setItem('cart', JSON.stringify(formattedList));
            return;
          }
        }
      } catch (err) {
        console.error('後端連線失敗，採用本地快取備案:', err);
      } finally {
        setLoading(false);
      }

      const savedCart = JSON.parse(localStorage.getItem('cart')) || [];
      setCartItems(savedCart);
    };

    fetchUserCart();
  }, [currentUserId]);

  // ── 取得單一 furniture_id 在 existingItems 裡目前的筆數 ─────────
  const getExistingId = (item) =>
    item.furniture_id ?? item.id ?? item.product_id ?? item.furnitureId;

  // ── 調整既有家具數量（+1 / -1，減到 0 時先確認）───────────────
  // 🚀 這裡只改「畫面上暫存的資料」，不會立刻打後端，要等按下「儲存變更」才會真的送出
  const changeExistingQty = (furnitureId, delta) => {
    const currentCount = existingItems.filter(
      it => getExistingId(it) === furnitureId
    ).length;
    const newCount = currentCount + delta;

    if (delta > 0 && currentCount >= MAX_QTY) {
      alert(`已達單款上限（${MAX_QTY} 個），無法再增加囉！`);
      return;
    }

    if (newCount <= 0) {
      const confirmed = window.confirm('確定要刪除嗎？');
      if (!confirmed) return; // 取消：維持在 1，不做任何變動
    }

    if (delta > 0) {
      setExistingItems(prev => [...prev, { furniture_id: furnitureId, x: 0, y: 0, z: 0 }]);
    } else {
      // 🚀 修正：removedOne 移到 updater 函式「裡面」，確保 StrictMode 底下
      // 每次呼叫（包含開發模式會多打的那一次）都會各自重新計算，不會共用同一份狀態
      setExistingItems(prev => {
        let removedOne = false;
        return prev.filter(it => {
          if (!removedOne && getExistingId(it) === furnitureId) {
            removedOne = true;
            return false;
          }
          return true;
        });
      });
    }
  };

  // ── 調整購物車項目數量（+1 / -1，減到 0 時先確認再真的移除）──────
  const changeQty = (cartItemId, delta) => {
    const target = cartItems.find(item => item.id === cartItemId);
    if (!target) return;
    const currentQty = target.quantity || 1;
    const newQty = currentQty + delta;

    if (newQty > MAX_QTY) {
      alert(`「${target.name}」已達單款上限（${MAX_QTY} 個）！`);
      return;
    }

    if (newQty <= 0) {
      const confirmed = window.confirm('確定要刪除嗎？');
      if (!confirmed) return; // 取消：維持在 1，不做任何變動

      const updatedCart = cartItems.filter(item => item.id !== cartItemId);
      setCartItems(updatedCart);
      localStorage.setItem('cart', JSON.stringify(updatedCart));
      fetch(`${API_BASE}/api/cart/${cartItemId}`, {
        method: 'DELETE',
        headers: mutateHeaders,
      }).catch(err => console.error('後端刪除失敗，仍從畫面移除:', err));
      return;
    }

    const updatedCart = cartItems.map(item =>
      item.id === cartItemId ? { ...item, quantity: newQty } : item
    );
    setCartItems(updatedCart);
    localStorage.setItem('cart', JSON.stringify(updatedCart));
  };

  // ── 儲存變更 → 合併「暫存的既有家具異動」+「這次新選的家具」→ PUT 更新 → 清購物車 → 跳轉 /projects ──
  const handleAddToExistingProject = async () => {
    if (!currentUserId || !editProjectId) return;
    // 沒有任何變動（既沒改既有家具、也沒選新家具）就不用送出
    const hasExistingChanges =
      JSON.stringify(existingItems) !== JSON.stringify(originalExistingItems);
    if (cartItems.length === 0 && !hasExistingChanges) return;

    try {
      setLoading(true);

      // 1. 把這次新選的家具轉成跟建立專案時一樣的格式（依 quantity 展開成多筆）
      const newItems = cartItems.flatMap(item => {
        const qty = item.quantity || 1;
        return Array.from({ length: qty }, () => ({
          furniture_id: item.product_id || item.id,
          x: 0,
          y: 0,
          z: 0,
        }));
      });

      // 2. 合併「畫面上暫存、含使用者調整的既有家具」+ 新選的家具，一起送出
      const mergedItems = [...existingItems, ...newItems];

      const res = await fetch(`${API_BASE}/api/projects/${editProjectId}`, {
        method: 'PUT',
        headers: mutateHeaders,
        body: JSON.stringify({
          name: editProjectName,
          itemsRaw: JSON.stringify(mergedItems),
        }),
      });

      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '新增家具到專案失敗');
      }

      // 4. 資料庫確認收到後，清掉伺服器端購物車項目
      const deleteResults = await Promise.allSettled(
        cartItems.map(item =>
          fetch(`${API_BASE}/api/cart/${item.id}`, {
            method: 'DELETE',
            headers: mutateHeaders,
          })
        )
      );
      const failedDeletes = deleteResults.filter(
        r => r.status === 'rejected' || (r.value && !r.value.ok)
      );
      if (failedDeletes.length > 0) {
        console.warn(`⚠️ 有 ${failedDeletes.length} 筆購物車項目在伺服器端刪除失敗`);
      }

      setCartItems([]);
      localStorage.removeItem('cart');
      localStorage.removeItem('cart_user_id');
      localStorage.removeItem('editProjectId');
      localStorage.removeItem('editProjectName');
      window.location.href = '/projects';
    } catch (err) {
      console.error('新增家具到專案失敗:', err);
      alert(`新增失敗：${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  // ── 取消編輯既有專案模式（不影響購物車內容，只是清掉編輯目標） ──
  const cancelEditMode = () => {
    localStorage.removeItem('editProjectId');
    localStorage.removeItem('editProjectName');
    window.location.href = '/projects';
  };

  // ── 儲存為專案 → 清購物車 → 跳轉 /projects ─────────────────
  const handleSaveAsProject = async () => {
    if (cartItems.length === 0 || !currentUserId) return;
    if (!projectName.trim()) {
      alert('請先為這個配置空間命名！');
      return;
    }

    try {
      setLoading(true);

      const items = cartItems.flatMap(item => {
        const qty = item.quantity || 1;
        return Array.from({ length: qty }, () => ({
          furniture_id: item.product_id || item.id,
          x: 0,
          y: 0,
          z: 0,
        }));
      });

      const response = await fetch(`${API_BASE}/api/projects`, {
        method: 'POST',
        headers: mutateHeaders,
        body: JSON.stringify({
          user_id: Number(currentUserId),
          name: projectName.trim(),
          l: null,
          w: null,
          itemsRaw: JSON.stringify(items), 
        }),
      });

      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || '建立專案失敗');
      }

      // 🚀 資料庫已確認收到清單（POST /api/projects 成功）後，
      // 再逐筆把伺服器端的購物車項目刪掉，確保「清空」不只是清瀏覽器快取
      const deleteResults = await Promise.allSettled(
        cartItems.map(item =>
          fetch(`${API_BASE}/api/cart/${item.id}`, {
            method: 'DELETE',
            headers: mutateHeaders,
          })
        )
      );
      const failedDeletes = deleteResults.filter(
        r => r.status === 'rejected' || (r.value && !r.value.ok)
      );
      if (failedDeletes.length > 0) {
        console.warn(
          `⚠️ 有 ${failedDeletes.length} 筆購物車項目在伺服器端刪除失敗，可能會在下次載入時重新出現`
        );
      }

      setCartItems([]);
      localStorage.removeItem('cart');
      localStorage.removeItem('cart_user_id');
      window.location.href = '/projects';
    } catch (err) {
      console.error('儲存專案失敗:', err);
      alert(`儲存失敗：${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  const totalPrice = cartItems.reduce(
    (sum, item) => sum + (item.price || 0) * (item.quantity || 1), 0
  );
  const totalQty = cartItems.reduce((sum, item) => sum + (item.quantity || 1), 0);
  // 🚀 既有家具是否有未儲存的變動（跟剛載入時的快照比對）
  const hasExistingChanges =
    JSON.stringify(existingItems) !== JSON.stringify(originalExistingItems);

  return (
    <div className="cart-container">
      <div className="cart-header">
        <h1><ShoppingBag /> 我的配置清單</h1>
        {!currentUserId && (
          <p style={{ color: '#dc2626', fontWeight: 'bold' }}>
            ⚠️ 請先登入系統才能進行配置
          </p>
        )}
      </div>

      {currentUserId && (cartItems.length > 0 || (editProjectId && existingItems.length > 0)) ? (
        <div className="cart-content">
          <div className="cart-list">
            {/* 🚀 既有家具（依 furniture_id 分組計數）：白底，+/- 直接呼叫後端更新 */}
            {editProjectId && Object.entries(
              existingItems.reduce((acc, it) => {
                const fid = getExistingId(it);
                acc[fid] = (acc[fid] || 0) + 1;
                return acc;
              }, {})
            ).map(([fidKey, count]) => {
              const furnitureId = isNaN(Number(fidKey)) ? fidKey : Number(fidKey);
              const info = furnitureMap[furnitureId];
              return (
                <div key={`existing-${fidKey}`} className="cart-item cart-item-existing">
                  <img
                    src={info?.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=500'}
                    alt={info?.name || '家具'}
                  />
                  <div className="item-info">
                    <h3>{info?.name || '未知家具'}</h3>
                    <p className="item-existing-tag">已在專案中</p>
                  </div>
                  <div className="qty-stepper">
                    <button
                      className="qty-btn"
                      onClick={() => changeExistingQty(furnitureId, -1)}
                      aria-label="減少數量"
                    >
                      <Minus size={16} />
                    </button>
                    <span className="qty-value">{count}</span>
                    <button
                      className="qty-btn"
                      onClick={() => changeExistingQty(furnitureId, 1)}
                      aria-label="增加數量"
                      disabled={count >= MAX_QTY}
                    >
                      <Plus size={16} />
                    </button>
                  </div>
                </div>
              );
            })}

            {/* 這次新選的家具：底色標示，讓使用者清楚看到「這次改了什麼」 */}
            {cartItems.map(item => (
              <div
                key={item.id}
                className={`cart-item ${editProjectId ? 'cart-item-new' : ''}`}
              >
                <img
                  src={item.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=500'}
                  alt={item.name}
                />
                <div className="item-info">
                  <h3>
                    {item.name}
                    {editProjectId && <span className="item-new-badge">新增</span>}
                  </h3>
                  <p>尺寸：{item.length_cm || '-'} x {item.width || '-'} x {item.height || '-'} cm</p>
                  <p className="item-price">NT$ {(item.price || 0).toLocaleString()}</p>
                </div>
                <div className="qty-stepper">
                  <button
                    className="qty-btn"
                    onClick={() => changeQty(item.id, -1)}
                    aria-label="減少數量"
                  >
                    <Minus size={16} />
                  </button>
                  <span className="qty-value">{item.quantity || 1}</span>
                  <button
                    className="qty-btn"
                    onClick={() => changeQty(item.id, 1)}
                    aria-label="增加數量"
                    disabled={(item.quantity || 1) >= MAX_QTY}
                  >
                    <Plus size={16} />
                  </button>
                </div>
              </div>
            ))}
          </div>

          <div className="cart-summary">
            <h3>預計總額</h3>
            <div className="summary-row">
              <span>商品數量</span>
              <span>{totalQty} 件</span>
            </div>
            <div className="summary-row total">
              <span>總計</span>
              <span>NT$ {totalPrice.toLocaleString()}</span>
            </div>

            <div className="cart-action-area">
              {editProjectId ? (
                <>
                  <p className="project-name-label">
                    🔧 正在為專案「{editProjectName || editProjectId}」新增家具
                  </p>
                  <button
                    className="save-btn"
                    onClick={handleAddToExistingProject}
                    disabled={loading || (cartItems.length === 0 && !hasExistingChanges)}
                  >
                    <Save size={18} />
                    {loading
                      ? '⏳ 正在儲存...'
                      : (cartItems.length === 0 && !hasExistingChanges)
                        ? '尚未有任何變動'
                        : '儲存變更'}
                  </button>
                  <button
                    className="cancel-edit-btn"
                    onClick={cancelEditMode}
                    disabled={loading}
                  >
                    取消，返回專案
                  </button>
                </>
              ) : (
                <>
                  <label className="project-name-label">* 為此配置空間命名：</label>
                  <input
                    className="project-name-input"
                    type="text"
                    placeholder="例如：客廳第一版、我的夢幻臥室"
                    value={projectName}
                    onChange={e => setProjectName(e.target.value)}
                  />
                  <button
                    className="save-btn"
                    onClick={handleSaveAsProject}
                    disabled={loading}
                  >
                    <Save size={18} />
                    {loading ? '⏳ 正在儲存...' : '儲存配置清單'}
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      ) : (
        <div className="empty-cart">
          <p>
            {currentUserId
              ? '配置清單目前是空的，快去型錄挑選喜歡的家具吧！'
              : '請登入後查看配置清單'}
          </p>
          <button className="empty-cart-btn" onClick={() => window.location.href = '/catalog'}>
            前往家具型錄
          </button>
        </div>
      )}
    </div>
  );
};

export default Cart;