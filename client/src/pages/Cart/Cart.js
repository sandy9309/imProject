import React, { useState, useEffect } from 'react';
import { ShoppingBag, Save, Plus, Minus } from 'lucide-react';
import './Cart.css';
import { showToast, showConfirm } from '../../components/Ui/ui';

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
  const [editProjectId] = useState(() => localStorage.getItem('editProjectId'));
  const [editProjectName] = useState(() => localStorage.getItem('editProjectName') || '');
  const [existingItems, setExistingItems] = useState([]);
  const [originalExistingItems, setOriginalExistingItems] = useState([]);
  const [furnitureMap, setFurnitureMap] = useState({});

  // 編輯模式
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

  // 載入購物車（純前端 localStorage）
  useEffect(() => {
    if (!currentUserId) return;

    const lastCartUserId = localStorage.getItem('cart_user_id');
    if (lastCartUserId && lastCartUserId !== currentUserId) {
      localStorage.removeItem('cart');
    }
    localStorage.setItem('cart_user_id', currentUserId);

    const savedCart = JSON.parse(localStorage.getItem('cart')) || [];
    setCartItems(savedCart);
  }, [currentUserId]);

  const keyOf = (raw) =>
    (raw === undefined || raw === null) ? 'unknown' : String(raw);
  const getExistingId = (item) =>
    keyOf(item.furniture_id ?? item.id ?? item.product_id ?? item.furnitureId);
  const getCartId = (item) =>
    keyOf(item.product_id ?? item.id);

  const buildMergedGroups = () => {
    const groups = {}; // key -> { info, existingCount, cartCount, originalCount, sampleName, samplePrice }

    // 1. 既有家具計數
    existingItems.forEach(it => {
      const k = getExistingId(it);
      if (!groups[k]) groups[k] = { key: k, existingCount: 0, cartCount: 0, originalCount: 0 };
      groups[k].existingCount += 1;
    });

    // 2. 原始既有數量（判斷有無變動）
    originalExistingItems.forEach(it => {
      const k = getExistingId(it);
      if (!groups[k]) groups[k] = { key: k, existingCount: 0, cartCount: 0, originalCount: 0 };
      groups[k].originalCount += 1;
    });

    cartItems.forEach(it => {
      const k = getCartId(it);
      if (!groups[k]) groups[k] = { key: k, existingCount: 0, cartCount: 0, originalCount: 0 };
      groups[k].cartCount += (it.quantity || 1);
      groups[k].cartSample = it;
    });

    return Object.values(groups).map(g => {
      const fid = isNaN(Number(g.key)) ? g.key : Number(g.key);
      const info = furnitureMap[fid];
      const total = g.existingCount + g.cartCount;
      const isModified = total !== g.originalCount; 
      return { ...g, fid, info, total, isModified };
    }).filter(g => g.total > 0); 
  };

  const changeMergedQty = async (group, delta) => {
    const { key, fid, existingCount, cartCount, total } = group;

    // 增加
    if (delta > 0) {
      if (total >= MAX_QTY) {
        showToast(`已達單款上限（${MAX_QTY} 個），無法再增加囉！`, 'error');
        return;
      }
      // 加到購物車那批（新增）
      addOneToCart(fid, group);
      return;
    }

    if (total <= 1) {
      const confirmed = await showConfirm({ message: '確定要刪除嗎？', danger: true });
      if (!confirmed) return;
    }

    if (cartCount > 0) {
      removeOneFromCart(key);
    } else if (existingCount > 0) {
      removeOneFromExisting(key);
    }
  };

  const addOneToCart = (fid, group) => {
    const info = group.info || group.cartSample || {};
    setCartItems(prev => {
      const idx = prev.findIndex(it => getCartId(it) === String(fid));
      let next;
      if (idx > -1) {
        next = [...prev];
        next[idx] = { ...next[idx], quantity: (next[idx].quantity || 1) + 1 };
      } else {
        next = [...prev, {
          id: fid,
          product_id: fid,
          name: info.name || '家具',
          price: Number(info.price || 0),
          image_url: info.image_url || '',
          length_cm: info.length_cm,
          width: info.width,
          height: info.height,
          quantity: 1,
        }];
      }
      localStorage.setItem('cart', JSON.stringify(next));
      return next;
    });
  };

  const removeOneFromCart = (key) => {
    setCartItems(prev => {
      const idx = prev.findIndex(it => getCartId(it) === key);
      if (idx === -1) return prev;
      const next = [...prev];
      const q = next[idx].quantity || 1;
      if (q <= 1) {
        next.splice(idx, 1);
      } else {
        next[idx] = { ...next[idx], quantity: q - 1 };
      }
      localStorage.setItem('cart', JSON.stringify(next));
      return next;
    });
  };

  const removeOneFromExisting = (key) => {
    setExistingItems(prev => {
      let removed = false;
      return prev.filter(it => {
        if (!removed && getExistingId(it) === key) {
          removed = true;
          return false;
        }
        return true;
      });
    });
  };

  const changeQty = async (cartItemId, delta) => {
    const target = cartItems.find(item => item.id === cartItemId);
    if (!target) return;
    const currentQty = target.quantity || 1;
    const newQty = currentQty + delta;

    if (newQty > MAX_QTY) {
      showToast(`「${target.name}」已達單款上限（${MAX_QTY} 個）！`, 'error');
      return;
    }

    if (newQty <= 0) {
      const confirmed = await showConfirm({ message: '確定要刪除嗎？', danger: true });
      if (!confirmed) return;
      const updatedCart = cartItems.filter(item => item.id !== cartItemId);
      setCartItems(updatedCart);
      localStorage.setItem('cart', JSON.stringify(updatedCart));
      return;
    }

    const updatedCart = cartItems.map(item =>
      item.id === cartItemId ? { ...item, quantity: newQty } : item
    );
    setCartItems(updatedCart);
    localStorage.setItem('cart', JSON.stringify(updatedCart));
  };

  // 儲存變更 → 合併既有 + 新加 
  const handleAddToExistingProject = async () => {
    if (!currentUserId || !editProjectId) return;
    const hasChanges =
      JSON.stringify(existingItems) !== JSON.stringify(originalExistingItems) ||
      cartItems.length > 0;
    if (!hasChanges) return;

    try {
      setLoading(true);

      const newItems = cartItems.flatMap(item => {
        const qty = item.quantity || 1;
        return Array.from({ length: qty }, () => ({
          furniture_id: item.product_id || item.id,
          x: 0, y: 0, z: 0,
        }));
      });

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
        throw new Error(text || '新增失敗');
      }

      setCartItems([]);
      localStorage.removeItem('cart');
      localStorage.removeItem('cart_user_id');
      localStorage.removeItem('editProjectId');
      localStorage.removeItem('editProjectName');
      window.location.href = '/projects';
    } catch (err) {
      console.error('新增家具到專案失敗:', err);
      showToast(`新增失敗：${err.message}`, 'error');
    } finally {
      setLoading(false);
    }
  };

  const cancelEditMode = () => {
    localStorage.removeItem('editProjectId');
    localStorage.removeItem('editProjectName');
    window.location.href = '/projects';
  };

  const handleSaveAsProject = async () => {
    if (cartItems.length === 0 || !currentUserId) return;
    if (!projectName.trim()) {
      showToast('請先為這個配置空間命名！', 'error');
      return;
    }

    try {
      setLoading(true);

      const items = cartItems.flatMap(item => {
        const qty = item.quantity || 1;
        return Array.from({ length: qty }, () => ({
          furniture_id: item.product_id || item.id,
          x: 0, y: 0, z: 0,
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

      setCartItems([]);
      localStorage.removeItem('cart');
      localStorage.removeItem('cart_user_id');
      window.location.href = '/projects';
    } catch (err) {
      console.error('儲存專案失敗:', err);
      showToast(`儲存失敗：${err.message}`, 'error');
    } finally {
      setLoading(false);
    }
  };

  const mergedGroups = editProjectId ? buildMergedGroups() : [];

  const totalPrice = editProjectId
    ? mergedGroups.reduce((sum, g) => sum + Number(g.info?.price || g.cartSample?.price || 0) * g.total, 0)
    : cartItems.reduce((sum, item) => sum + (item.price || 0) * (item.quantity || 1), 0);
  const totalQty = editProjectId
    ? mergedGroups.reduce((sum, g) => sum + g.total, 0)
    : cartItems.reduce((sum, item) => sum + (item.quantity || 1), 0);

  const hasAnyChange =
    JSON.stringify(existingItems) !== JSON.stringify(originalExistingItems) ||
    cartItems.length > 0;

  const showList = editProjectId ? mergedGroups.length > 0 : cartItems.length > 0;

  return (
    <div className="cart-container">
      <div className="cart-header">
        <h1><ShoppingBag /> 我的配置清單</h1>
        {!currentUserId && (
          <p style={{ color: 'var(--color-danger)', fontWeight: 'bold' }}>
            ⚠️ 請先登入系統才能進行配置
          </p>
        )}
      </div>

      {currentUserId && showList ? (
        <div className="cart-content">
          <div className="cart-list">
            {/* ═══ 編輯模式：合併後的家具卡片（同款一張，有變動才變色）═══ */}
            {editProjectId && mergedGroups.map(g => {
              const info = g.info || g.cartSample || {};
              const unitPrice = Number(info.price || 0);
              return (
                <div
                  key={`grp-${g.key}`}
                  className={`cart-item ${g.isModified ? 'cart-item-changed' : ''}`}
                >
                  <img
                    src={info.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=500'}
                    alt={info.name || '家具'}
                  />
                  <div className="item-info">
                    <h3>{info.name || '未知家具'}</h3>
                    <p>尺寸：{info.length_cm || '-'} x {info.width || '-'} x {info.height || '-'} cm</p>
                    <p className="item-price">NT$ {unitPrice.toLocaleString()}</p>
                  </div>
                  <div className="qty-stepper">
                    <button
                      className="qty-btn"
                      onClick={() => changeMergedQty(g, -1)}
                      aria-label="減少數量"
                    >
                      <Minus size={16} />
                    </button>
                    <span className="qty-value">{g.total}</span>
                    <button
                      className="qty-btn"
                      onClick={() => changeMergedQty(g, 1)}
                      aria-label="增加數量"
                      disabled={g.total >= MAX_QTY}
                    >
                      <Plus size={16} />
                    </button>
                  </div>
                </div>
              );
            })}

            {/* ═══ 非編輯模式（建立新專案）：純購物車 ═══ */}
            {!editProjectId && cartItems.map(item => (
              <div key={item.id} className="cart-item">
                <img
                  src={item.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=500'}
                  alt={item.name}
                />
                <div className="item-info">
                  <h3>{item.name}</h3>
                  <p>尺寸：{item.length_cm || '-'} x {item.width || '-'} x {item.height || '-'} cm</p>
                  <p className="item-price">NT$ {(item.price || 0).toLocaleString()}</p>
                </div>
                <div className="qty-stepper">
                  <button className="qty-btn" onClick={() => changeQty(item.id, -1)} aria-label="減少數量">
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
                    正在編輯專案「{editProjectName || editProjectId}」
                  </p>
                  <button
                    className="save-btn"
                    onClick={handleAddToExistingProject}
                    disabled={loading || !hasAnyChange}
                  >
                    <Save size={18} />
                    {loading
                      ? '⏳ 正在儲存...'
                      : !hasAnyChange
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